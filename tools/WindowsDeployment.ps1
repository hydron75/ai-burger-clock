# Functions only. Dot-sourcing does not inspect or change the operating installation.
# Tests replace OS boundary functions in their own scope, never production switches.
Set-StrictMode -Version Latest

function Stop-WD([string]$Code) { throw [InvalidOperationException]::new("DEPLOY:$Code") }

function Get-WDFullPath([string]$Path) {
    if (-not $Path -or -not [IO.Path]::IsPathFullyQualified($Path)) { Stop-WD 'AbsolutePathRequired' }
    [IO.Path]::GetFullPath($Path).TrimEnd([char[]]'\/')
}

function Assert-WDNoReparse([string]$Path) {
    $cursor = Get-WDFullPath $Path
    while ($cursor) {
        if ((Test-Path -LiteralPath $cursor) -and
            ((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            Stop-WD 'ReparsePoint'
        }
        $parent = [IO.Directory]::GetParent($cursor)
        $cursor = if ($parent) { $parent.FullName } else { $null }
    }
}

function Assert-WDChildPath([string]$Path, [string]$Parent) {
    $child = Get-WDFullPath $Path
    $root = Get-WDFullPath $Parent
    if (-not $child.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        Stop-WD 'PathOutsideApprovedRoot'
    }
    Assert-WDNoReparse $child
    $child
}

function Initialize-WDNativePath {
    if (-not ('WindowsDeploymentNative' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
public static class WindowsDeploymentNative {
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    public static extern uint GetFinalPathNameByHandle(SafeFileHandle h, StringBuilder p, uint n, uint f);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode)]
    public static extern int GetCurrentPackageFullName(ref uint n, StringBuilder p);
}
'@
    }
}

function Assert-WDExternalContext {
    if (-not $IsWindows -or -not [Environment]::Is64BitProcess -or -not [Environment]::UserInteractive) {
        Stop-WD 'OutsideCodex64BitInteractivePowerShellRequired'
    }
    Initialize-WDNativePath
    [uint32]$length = 0
    if ([WindowsDeploymentNative]::GetCurrentPackageFullName([ref]$length, $null) -ne 15700) {
        Stop-WD 'PackagedProcessRefused'
    }
}

function Get-WDActualPath([string]$Path) {
    $requested = Get-WDFullPath $Path
    Assert-WDNoReparse $requested
    Initialize-WDNativePath
    $file = [IO.File]::Open($requested, 'Open', 'Read', 'ReadWrite')
    try {
        $buffer = [Text.StringBuilder]::new(32768)
        $length = [WindowsDeploymentNative]::GetFinalPathNameByHandle($file.SafeFileHandle, $buffer, $buffer.Capacity, 0)
        if ($length -eq 0 -or $length -ge $buffer.Capacity) { Stop-WD 'OpenedPathUnavailable' }
        $actual = $buffer.ToString()
        if ($actual.StartsWith('\\?\')) { $actual = $actual.Substring(4) }
        if (-not $requested.Equals($actual, [StringComparison]::OrdinalIgnoreCase)) { Stop-WD 'RedirectedPath' }
        $actual
    } finally { $file.Dispose() }
}

function Get-WDHash([string]$Path) {
    (Get-FileHash -LiteralPath (Get-WDActualPath $Path) -Algorithm SHA256).Hash
}

function Get-WDExeVersion([string]$Path) {
    $info = (Get-Item -LiteralPath (Get-WDActualPath $Path)).VersionInfo
    [pscustomobject]@{ FileVersion = $info.FileVersion; ProductVersion = $info.ProductVersion }
}

function Assert-WDIdentityShape($Identity) {
    if ($Identity.FileVersion -notmatch '^\d+\.\d+\.\d+\.\d+$' -or
        $Identity.SourceCommit -notmatch '^[0-9a-f]{40}$' -or
        $Identity.SHA256 -notmatch '^[0-9A-Fa-f]{64}$' -or
        $Identity.ProductVersion -notmatch ('^\d+\.\d+\.\d+(?:\.\d+)?\+' + $Identity.SourceCommit + '$')) {
        Stop-WD 'InvalidExeIdentity'
    }
}

function Assert-WDExe([string]$Path, $Identity) {
    $version = Get-WDExeVersion $Path
    if ((Get-WDHash $Path) -ne $Identity.SHA256 -or $version.FileVersion -ne $Identity.FileVersion -or
        $version.ProductVersion -ne $Identity.ProductVersion) { Stop-WD 'ExeIdentityMismatch' }
}

function Read-WDReceipt([string]$Repository, [string]$Path) {
    $repo = Get-WDFullPath $Repository
    $receiptPath = Assert-WDChildPath $Path (Join-Path $repo 'artifacts')
    if ((Get-Item -LiteralPath $receiptPath).Length -gt 16KB) { Stop-WD 'ReceiptTooLarge' }
    $receipt = [IO.File]::ReadAllText((Get-WDActualPath $receiptPath)) | ConvertFrom-Json
    if ($receipt.Schema -ne 'aiburgerclock.windows-deployment' -or $receipt.SchemaVersion -ne 1 -or
        (Get-WDFullPath $receipt.Repository) -ne $repo) { Stop-WD 'ReceiptRepositoryOrSchemaMismatch' }
    Assert-WDIdentityShape $receipt.Candidate
    Assert-WDIdentityShape $receipt.Previous
    $candidate = Assert-WDChildPath $receipt.Candidate.Path (Split-Path -Parent $receiptPath)
    if ([IO.Path]::GetFileName($candidate) -ne 'AI Burger Clock.exe' -or
        [version]$receipt.Candidate.FileVersion -le [version]$receipt.Previous.FileVersion -or
        $receipt.Candidate.SHA256 -eq $receipt.Previous.SHA256 -or
        (Get-Item -LiteralPath $candidate).Length -ne $receipt.Candidate.Bytes) { Stop-WD 'InvalidCandidate' }
    Assert-WDExe $candidate $receipt.Candidate
    # Ignore unrecognized receipt fields rather than copying them into evidence.
    [pscustomobject]@{
        Candidate = [pscustomobject]@{
            Path = $candidate; FileVersion = $receipt.Candidate.FileVersion
            ProductVersion = $receipt.Candidate.ProductVersion; SourceCommit = $receipt.Candidate.SourceCommit
            SHA256 = $receipt.Candidate.SHA256; Bytes = $receipt.Candidate.Bytes
        }
        Previous = [pscustomobject]@{
            FileVersion = $receipt.Previous.FileVersion; ProductVersion = $receipt.Previous.ProductVersion
            SourceCommit = $receipt.Previous.SourceCommit; SHA256 = $receipt.Previous.SHA256
        }
    }
}

function Get-WDAppProcesses { @(Get-Process -ErrorAction Stop | Where-Object ProcessName -eq 'AI Burger Clock') }
function Get-WDDataRoot { Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'AIBurgerClock' }

function Assert-WDStopped([string]$Database) {
    if (@(Get-WDAppProcesses).Count) { Stop-WD 'AppStillRunning' }
    foreach ($suffix in @('-wal', '-shm')) {
        if (Test-Path -LiteralPath ($Database + $suffix)) { Stop-WD 'SqliteSidecarPresent' }
    }
}

function Get-WDTreeFiles([string]$Directory) {
    Assert-WDNoReparse $Directory
    foreach ($child in Get-ChildItem -LiteralPath $Directory -Force) {
        Assert-WDNoReparse $child.FullName
        if ($child.PSIsContainer) { Get-WDTreeFiles $child.FullName }
        else { $child.FullName }
    }
}

function Get-WDDataManifest([string]$DataRoot) {
    $files = @((Join-Path $DataRoot 'burgerclock.db'))
    $logs = Join-Path $DataRoot 'logs'
    if (Test-Path -LiteralPath $logs) { $files += @(Get-WDTreeFiles $logs) }
    @($files | Sort-Object | ForEach-Object {
        [pscustomobject]@{
            Path = [IO.Path]::GetRelativePath($DataRoot, $_)
            Bytes = (Get-Item -LiteralPath $_).Length
            SHA256 = Get-WDHash $_
        }
    })
}

function Assert-WDManifest([string]$DataRoot, $Manifest) {
    $actual = @(Get-WDDataManifest $DataRoot) | ConvertTo-Json -Compress -Depth 4
    $expected = @($Manifest) | ConvertTo-Json -Compress -Depth 4
    if ($actual -cne $expected) { Stop-WD 'DatabaseOrLogsChanged' }
}

function Invoke-WDSourceArchive([string]$Repository, [string]$Commit, [string]$Destination) {
    & git -C $Repository -c "safe.directory=$Repository" archive --format=zip "--output=$Destination" $Commit 2>$null
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $Destination)) { Stop-WD 'SourceArchiveFailed' }
}

function Assert-WDExeZip([string]$Zip, [string]$Hash) {
    $archive = [IO.Compression.ZipFile]::OpenRead($Zip)
    try {
        $entries = @($archive.Entries)
        if ($entries.Count -ne 1 -or $entries[0].Name -ne 'AI Burger Clock.exe') { Stop-WD 'DistArchiveMismatch' }
        $stream = $entries[0].Open()
        $sha = [Security.Cryptography.SHA256]::Create()
        try { $actual = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '') }
        finally { $sha.Dispose(); $stream.Dispose() }
        if ($actual -ne $Hash) { Stop-WD 'DistArchiveMismatch' }
    } finally { $archive.Dispose() }
}

function Get-WDRegistryTree($Key, [string]$Relative = '') {
    # Include key existence, even when it contains no values or descendants.
    [pscustomobject]@{ Key = $Relative; Entry = 'Key' }
    foreach ($name in @($Key.GetValueNames() | Sort-Object)) {
        [pscustomobject]@{
            Key = $Relative; Name = $name; Kind = [string]$Key.GetValueKind($name)
            Value = $Key.GetValue($name, $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
        }
    }
    foreach ($name in @($Key.GetSubKeyNames() | Sort-Object)) {
        $subkey = $Key.OpenSubKey($name, $false)
        try { Get-WDRegistryTree $subkey "$Relative\$name" } finally { $subkey.Dispose() }
    }
}

function Get-WDRegistryValue($Key, [string]$Name) {
    $present = $null -ne $Key -and @($Key.GetValueNames()) -contains $Name
    [pscustomobject]@{
        Present = $present
        Kind = if ($present) { [string]$Key.GetValueKind($Name) } else { $null }
        Value = if ($present) { $Key.GetValue($Name, $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames) } else { $null }
    }
}

function Get-WDRegistration([string]$Exe) {
    $aumid = 'Hydron75.AIBurgerClock.Quota'
    $clsid = [guid]'10B09E94-A6B4-4ABA-9B9F-C1551C7A409B'
    $owner = 'AiBurgerClock.QuotaToast.v1'
    $shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'AI Burger Clock - quota notifications.lnk'
    $null = Get-WDActualPath $shortcut
    $wsh = $shell = $link = $folder = $item = $null
    $root = $app = $class = $server = $run = $approved = $null
    try {
        $wsh = New-Object -ComObject WScript.Shell
        $shell = New-Object -ComObject Shell.Application
        $link = $wsh.CreateShortcut($shortcut) # Read existing shortcut; never Save.
        $folder = $shell.Namespace((Split-Path -Parent $shortcut))
        $item = $folder.ParseName([IO.Path]::GetFileName($shortcut))
        if ([string]$link.TargetPath -ne $Exe -or [string]$link.Arguments -ne '' -or
            [string]$link.WorkingDirectory -ne (Split-Path -Parent $Exe) -or
            [string]$item.ExtendedProperty('System.AppUserModel.ID') -ne $aumid -or
            [guid]$item.ExtendedProperty('System.AppUserModel.ToastActivatorCLSID') -ne $clsid) {
            Stop-WD 'ShortcutMismatch'
        }
        $root = [Microsoft.Win32.RegistryKey]::OpenBaseKey('CurrentUser', 'Registry64')
        $app = $root.OpenSubKey("Software\Classes\AppUserModelId\$aumid", $false)
        $class = $root.OpenSubKey(('Software\Classes\CLSID\' + $clsid.ToString('B')), $false)
        $server = if ($class) { $class.OpenSubKey('LocalServer32', $false) } else { $null }
        if (-not $app -or -not $class -or -not $server) { Stop-WD 'RegistrationMissing' }
        if ($app.GetValue('Owner') -ne $owner -or $class.GetValue('Owner') -ne $owner -or
            [string]$app.GetValue('ExecutablePath') -ne $Exe -or [string]$class.GetValue('ExecutablePath') -ne $Exe -or
            [guid]$app.GetValue('CustomActivator') -ne $clsid -or
            [string]$server.GetValue($null) -ne ('"' + $Exe + '" --quota-toast-activated')) {
            Stop-WD 'RegistrationMismatch'
        }
        $run = $root.OpenSubKey('Software\Microsoft\Windows\CurrentVersion\Run', $false)
        $approved = $root.OpenSubKey('Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run', $false)
        $runValue = Get-WDRegistryValue $run 'AI Burger Clock'
        if ($runValue.Present -and [string]$runValue.Value -ne ('"' + $Exe + '" --autostart')) { Stop-WD 'AutoStartMismatch' }
        $state = [ordered]@{
            ShortcutSHA256 = Get-WDHash $shortcut
            App = @(Get-WDRegistryTree $app)
            Class = @(Get-WDRegistryTree $class)
            Run = $runValue
            StartupApproved = Get-WDRegistryValue $approved 'AI Burger Clock'
        }
        # Only a digest leaves this function. No raw registry strings are written.
        $sha = [Security.Cryptography.SHA256]::Create()
        try { $digest = [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes(($state | ConvertTo-Json -Compress -Depth 12)))).Replace('-', '') }
        finally { $sha.Dispose() }
        [pscustomobject]@{ Digest = $digest; AutoStartRunEntryPresent = $runValue.Present }
    } finally {
        foreach ($key in @($approved, $run, $server, $class, $app, $root)) { if ($key) { $key.Dispose() } }
        foreach ($obj in @($item, $folder, $link, $shell, $wsh)) {
            if ($obj -and [Runtime.InteropServices.Marshal]::IsComObject($obj)) {
                [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($obj)
            }
        }
    }
}

function Invoke-WDDryRun([string]$Exe) {
    $info = [Diagnostics.ProcessStartInfo]::new($Exe)
    $info.WorkingDirectory = Split-Path -Parent $Exe
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $info.ArgumentList.Add('--unregister-quota-notifications')
    $info.ArgumentList.Add('--dry-run')
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $info
    try {
        if (-not $process.Start()) { Stop-WD 'DryRunStartFailed' }
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(30000)) {
            $process.Kill() # Only this helper's own dry-run child, never an operating app.
            $process.WaitForExit()
            Stop-WD 'DryRunTimeout'
        }
        $text = $stdout.GetAwaiter().GetResult()
        $null = $stderr.GetAwaiter().GetResult() # Do not log raw child output/errors.
        if ($process.ExitCode -ne 0 -or -not $text.Contains('Dry run: no registration changed.')) { Stop-WD 'DryRunFailed' }
        [int]$process.ExitCode
    } finally { $process.Dispose() }
}

function Start-WDApp([string]$Exe) {
    $process = Start-Process -FilePath $Exe -WorkingDirectory (Split-Path -Parent $Exe) -WindowStyle Hidden -PassThru
    try { if (-not $process) { Stop-WD 'AppLaunchFailed' }; $process.Id }
    finally { if ($process) { $process.Dispose() } }
}

function Invoke-WindowsDeployment([string]$Repository, [string]$ReceiptPath) {
    $steps = @('Receipt', 'Preflight', 'Backup', 'Replace', 'Inspect', 'Launch') | ForEach-Object {
        [pscustomobject]@{ Stage = $_; Result = 'NotRun' }
    }
    $result = [ordered]@{
        Succeeded = $false; Stages = $steps; Failure = $null
        BackupRoot = $null; FileVersion = $null; SourceCommit = $null; SHA256 = $null
        Rollback = 'NotNeeded'; DatabaseRestored = $false; RegistrationPerformed = $false
        AppLaunchRequested = $false; AppProcessId = $null; UserScreenCheckRequired = $true
        Checks = [ordered]@{
            ProcessAbsent = $false; ActualDataPathVerified = $false; WalAndShmAbsent = $false
            BackupHashesMatch = $false; CandidateMatches = $false; NotificationTargetsUnchanged = $false
            AutoStartPathMatches = $false; UnregisterDryRunExitCode = $null; DryRunChangedNothing = $false
        }
    }
    $current = $steps[0]
    $replacementAttempted = $false
    $backup = $null
    $lock = $null
    try {
        Assert-WDExternalContext
        $repo = Get-WDFullPath $Repository
        $receipt = Read-WDReceipt $repo $ReceiptPath
        $candidate = $receipt.Candidate.Path
        $dist = Join-Path $repo 'dist/win-x64/AI Burger Clock.exe'
        $data = Get-WDFullPath (Get-WDDataRoot)
        $db = Join-Path $data 'burgerclock.db'
        $result.FileVersion = $receipt.Candidate.FileVersion
        $result.SourceCommit = $receipt.Candidate.SourceCommit
        $result.SHA256 = $receipt.Candidate.SHA256
        # One receipt can only start one attempt. Keep its evidence even on failure.
        $runDirectory = [IO.Path]::ChangeExtension((Get-WDFullPath $ReceiptPath), '.run')
        Assert-WDNoReparse $runDirectory
        if (Test-Path -LiteralPath $runDirectory) { Stop-WD 'ReceiptAlreadyUsed' }
        # CreateNew is atomic across different receipts/processes for this dist.
        $lockPath = Join-Path (Split-Path -Parent $dist) '.deployment.lock'
        Assert-WDNoReparse $lockPath
        $lock = [IO.File]::Open($lockPath, 'OpenOrCreate', 'ReadWrite', 'None')
        if (Test-Path -LiteralPath $runDirectory) { Stop-WD 'ReceiptAlreadyUsed' }
        [IO.Directory]::CreateDirectory($runDirectory) | Out-Null
        $current.Result = 'Passed'

        $current = $steps[1]
        Assert-WDStopped $db
        $null = Get-WDActualPath $db
        Assert-WDExe $dist $receipt.Previous
        $registration = Get-WDRegistration $dist
        $manifest = @(Get-WDDataManifest $data)
        $result.Checks.ProcessAbsent = $true
        $result.Checks.ActualDataPathVerified = $true
        $result.Checks.WalAndShmAbsent = $true
        $current.Result = 'Passed'

        $current = $steps[2]
        $backupParent = Get-WDFullPath (Join-Path $repo '../backups')
        Assert-WDNoReparse $backupParent
        $stamp = [TimeZoneInfo]::ConvertTimeBySystemTimeZoneId([DateTime]::UtcNow, 'Korea Standard Time').ToString('yyyyMMdd-HHmmss')
        $backup = Join-Path $backupParent ('release-' + $receipt.Candidate.FileVersion + '-' + $stamp + '-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
        $null = Assert-WDChildPath $backup $backupParent
        [IO.Directory]::CreateDirectory((Join-Path $backup 'data')) | Out-Null
        $result.BackupRoot = $backup
        $previous = Join-Path $backup 'previous.exe'
        Copy-Item -LiteralPath $dist -Destination $previous
        $zip = Join-Path $backup 'previous-dist.zip'
        Compress-Archive -LiteralPath $dist -DestinationPath $zip
        Assert-WDExeZip $zip $receipt.Previous.SHA256
        foreach ($file in $manifest) {
            $destination = Join-Path (Join-Path $backup 'data') $file.Path
            [IO.Directory]::CreateDirectory((Split-Path -Parent $destination)) | Out-Null
            Copy-Item -LiteralPath (Join-Path $data $file.Path) -Destination $destination
        }
        Assert-WDStopped $db
        Assert-WDExe $previous $receipt.Previous
        Assert-WDManifest (Join-Path $backup 'data') $manifest
        Assert-WDManifest $data $manifest
        Invoke-WDSourceArchive $repo $receipt.Previous.SourceCommit (Join-Path $backup 'source-before.zip')
        Invoke-WDSourceArchive $repo $receipt.Candidate.SourceCommit (Join-Path $backup 'source-candidate.zip')
        $manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $backup 'data-manifest.json') -Encoding utf8
        $result.Checks.BackupHashesMatch = $true
        $current.Result = 'Passed'

        $current = $steps[3]
        Assert-WDStopped $db
        Assert-WDManifest $data $manifest
        Assert-WDExe $dist $receipt.Previous
        Assert-WDExe $candidate $receipt.Candidate
        if ((Get-WDRegistration $dist).Digest -ne $registration.Digest) { Stop-WD 'RegistrationChangedBeforeReplace' }
        $stage = Join-Path (Split-Path -Parent $dist) ('.deployment-' + [guid]::NewGuid().ToString('N') + '.exe')
        Assert-WDNoReparse $stage
        Copy-Item -LiteralPath $candidate -Destination $stage
        Assert-WDExe $stage $receipt.Candidate
        Assert-WDStopped $db
        $replacementAttempted = $true
        [IO.File]::Replace($stage, $dist, (Join-Path $backup 'replaced.exe'))
        Assert-WDExe $dist $receipt.Candidate
        Copy-Item -LiteralPath $dist -Destination (Join-Path $backup 'deployed.exe')
        Assert-WDExe (Join-Path $backup 'deployed.exe') $receipt.Candidate
        Assert-WDExe (Join-Path $backup 'replaced.exe') $receipt.Previous
        $result.Checks.CandidateMatches = $true
        $current.Result = 'Passed'

        $current = $steps[4]
        $beforeDryRun = Get-WDRegistration $dist
        if ($beforeDryRun.Digest -ne $registration.Digest) { Stop-WD 'RegistrationChangedAfterReplace' }
        $dryExit = Invoke-WDDryRun $dist
        if ($dryExit -ne 0) { Stop-WD 'DryRunFailed' }
        if ((Get-WDRegistration $dist).Digest -ne $registration.Digest) { Stop-WD 'DryRunChangedRegistration' }
        Assert-WDStopped $db
        Assert-WDManifest $data $manifest
        Assert-WDExe $dist $receipt.Candidate
        $result.Checks.NotificationTargetsUnchanged = $true
        $result.Checks.AutoStartPathMatches = $true
        $result.Checks.UnregisterDryRunExitCode = $dryExit
        $result.Checks.DryRunChangedNothing = $true
        $current.Result = 'Passed'

        $current = $steps[5]
        # All fallible verification/evidence writes precede normal app startup.
        # A successful Start-Process is a launch handoff, not proof of tray/UI health.
        $pending = [pscustomobject]@{ Candidate = $receipt.Candidate; Previous = $receipt.Previous; Stages = $steps; BackupRoot = $backup }
        $pending | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $runDirectory 'verified-before-launch.json') -Encoding utf8
        Assert-WDStopped $db
        Assert-WDExe $dist $receipt.Candidate
        Assert-WDManifest $data $manifest
        if ((Get-WDRegistration $dist).Digest -ne $registration.Digest) { Stop-WD 'RegistrationChangedBeforeLaunch' }
        $result.AppProcessId = Start-WDApp $dist
        $result.AppLaunchRequested = $true
        $current.Result = 'Passed'
        $result.Succeeded = $true
    } catch {
        $current.Result = 'Failed'
        # Raw errors can contain arbitrary child output/paths. Emit only known codes.
        $message = $_.Exception.Message
        $result.Failure = if ($message -cmatch '^DEPLOY:[A-Za-z]+$') { $message.Substring(7) } else { 'UnexpectedFailure' }
        if ($replacementAttempted) {
            try {
                # Sidecars/DB changes caused the deployment to stop, but do not
                # prevent an EXE-only rollback. Never remove/restore those files.
                if (@(Get-WDAppProcesses).Count) { Stop-WD 'AppStillRunning' }
                Assert-WDExe (Join-Path $backup 'previous.exe') $receipt.Previous
                if (-not (Test-Path -LiteralPath $dist) -or (Get-WDHash $dist) -ne $receipt.Previous.SHA256) {
                    $restore = Join-Path (Split-Path -Parent $dist) ('.rollback-' + [guid]::NewGuid().ToString('N') + '.exe')
                    Assert-WDNoReparse $restore
                    Copy-Item -LiteralPath (Join-Path $backup 'previous.exe') -Destination $restore
                    Assert-WDExe $restore $receipt.Previous
                    if (Test-Path -LiteralPath $dist) {
                        [IO.File]::Replace($restore, $dist, (Join-Path $backup 'failed-deployment.exe'))
                    } else { [IO.File]::Move($restore, $dist) }
                }
                Assert-WDExe $dist $receipt.Previous
                $result.Rollback = 'RestoredPreviousExe'
            } catch { $result.Rollback = 'FailedPreserveBackupAndStop' }
        }
    } finally { if ($lock) { $lock.Dispose() } }
    [pscustomobject]$result
}
