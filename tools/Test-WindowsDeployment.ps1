#requires -Version 7.0
[CmdletBinding()]
param([ValidateSet('before', 'after')][string]$Slot = 'after')

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $IsWindows) { throw 'Windows filesystem semantics are required.' }
$repository = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'WindowsDeployment.ps1')
$sourceArchiveImplementation = (Get-Command Invoke-WDSourceArchive).ScriptBlock
$fixedRoot = Join-Path ([Environment]::GetFolderPath('UserProfile')) '.ai-burger-clock-tests'
$slotRoot = Join-Path $fixedRoot $Slot
Assert-WDNoReparse $slotRoot
$fixtureRoot = Join-Path $slotRoot ('deployment-fixtures/' + [guid]::NewGuid().ToString('N'))
$null = Assert-WDChildPath $fixtureRoot $slotRoot
[IO.Directory]::CreateDirectory($fixtureRoot) | Out-Null
$script:checks = 0
$script:cases = 0
$script:osCalls = 0
$script:realArchives = 0
function Assert-Test([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw "FAIL: $Message" }
    $script:checks++
}

function Invoke-FixtureGit([string]$Repository, [string[]]$Arguments) {
    $output = & git -C $Repository -c "safe.directory=$Repository" -c "core.hooksPath=$Repository/.git/no-hooks" `
        -c commit.gpgSign=false -c 'user.name=Synthetic Fixture' -c user.email=fixture@example.invalid @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) { throw 'Synthetic Git repository setup failed.' }
    ($output -join "`n").Trim()
}

function Invoke-Fixture([string]$Mode = 'Success') {
    $script:cases++
    $root = Join-Path $fixtureRoot ("$script:cases-$Mode")
    $repo = Join-Path $root 'repository'
    $artifacts = Join-Path $repo 'artifacts/release-synthetic'
    $candidate = Join-Path $artifacts 'candidate/AI Burger Clock.exe'
    $dist = Join-Path $repo 'dist/win-x64/AI Burger Clock.exe'
    $data = Join-Path $root 'fake-local/AIBurgerClock'
    $db = Join-Path $data 'burgerclock.db'
    foreach ($directory in @((Split-Path -Parent $candidate), (Split-Path -Parent $dist), (Join-Path $data 'logs/nested'))) {
        [IO.Directory]::CreateDirectory($directory) | Out-Null
    }
    # None of these files is executable or a real account database/log.
    [IO.File]::WriteAllText($dist, 'synthetic-old')
    [IO.File]::WriteAllText($candidate, 'synthetic-new')
    [IO.File]::WriteAllText($db, 'synthetic database, not SQLite')
    [IO.File]::WriteAllText((Join-Path $data 'logs/nested/synthetic.txt'), 'synthetic log')
    $null = Invoke-FixtureGit $repo @('init', '--quiet')
    $source = Join-Path $repo 'synthetic-source.txt'
    [IO.File]::WriteAllText($source, 'synthetic old source')
    $null = Invoke-FixtureGit $repo @('add', '--', 'synthetic-source.txt')
    $null = Invoke-FixtureGit $repo @('commit', '--quiet', '-m', 'synthetic old source')
    $oldCommit = Invoke-FixtureGit $repo @('rev-parse', 'HEAD')
    [IO.File]::WriteAllText($source, 'synthetic new source')
    $null = Invoke-FixtureGit $repo @('add', '--', 'synthetic-source.txt')
    $null = Invoke-FixtureGit $repo @('commit', '--quiet', '-m', 'synthetic new source')
    $newCommit = Invoke-FixtureGit $repo @('rev-parse', 'HEAD')
    $old = [pscustomobject]@{ FileVersion = '1.0.0.0'; ProductVersion = '1.0.0+' + $oldCommit; SourceCommit = $oldCommit; SHA256 = Get-WDHash $dist }
    $new = [pscustomobject]@{ Path = $candidate; FileVersion = '1.0.1.0'; ProductVersion = '1.0.1+' + $newCommit; SourceCommit = $newCommit; SHA256 = Get-WDHash $candidate; Bytes = (Get-Item -LiteralPath $candidate).Length }
    $receiptPath = Join-Path $artifacts 'deployment-receipt.json'
    $receipt = [ordered]@{ Schema = 'aiburgerclock.windows-deployment'; SchemaVersion = 1; Repository = $repo; Candidate = $new; Previous = $old }
    $state = [pscustomobject]@{ RegistrationReads = 0; DryRuns = 0; Launches = 0; Archives = 0; Lock = $null; Calls = [Collections.Generic.List[string]]::new() }
    $dbHash = Get-WDHash $db

    if ($Mode -eq 'Wal') { [IO.File]::WriteAllText(($db + '-wal'), 'never delete') }
    if ($Mode -eq 'Shm') { [IO.File]::WriteAllText(($db + '-shm'), 'never delete') }
    if ($Mode -eq 'PreviousHash') { $old.SHA256 = '0' * 64 }
    if ($Mode -eq 'CandidateHash') { $new.SHA256 = '0' * 64 }
    if ($Mode -eq 'CandidateVersion') { $new.FileVersion = '1.0.2.0' }
    if ($Mode -eq 'SourceCommit') { $new.SourceCommit = 'invalid' }
    if ($Mode -eq 'ReceiptSchema') { $receipt.SchemaVersion = 2 }
    if ($Mode -eq 'WrongRepository') { $receipt.Repository = Join-Path $root 'other' }
    if ($Mode -eq 'CandidateOutside') { $new.Path = $dist }
    if ($Mode -eq 'ReparseLogs') {
        $other = Join-Path $root 'other-logs'
        [IO.Directory]::CreateDirectory($other) | Out-Null
        New-Item -ItemType Junction -Path (Join-Path $data 'logs/link') -Target $other | Out-Null
    }
    if ($Mode -eq 'ReusedReceipt') { [IO.Directory]::CreateDirectory([IO.Path]::ChangeExtension($receiptPath, '.run')) | Out-Null }
    if ($Mode -eq 'LockedDist') { $state.Lock = [IO.File]::Open($dist, 'Open', 'Read', 'Read') }
    if ($Mode -eq 'DeploymentInProgress') { $state.Lock = [IO.File]::Open((Join-Path (Split-Path -Parent $dist) '.deployment.lock'), 'OpenOrCreate', 'ReadWrite', 'None') }
    $receipt | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $receiptPath -Encoding utf8

    # Production has no -Test/-Skip checks. Only these test-scope boundary functions
    # are substituted; real backup, hashes, sidecar checks, replace and rollback run.
    function Assert-WDExternalContext { if ($Mode -eq 'Packaged') { Stop-WD 'PackagedProcessRefused' } }
    function Get-WDDataRoot { $data }
    function Get-WDAppProcesses { if ($Mode -eq 'Running') { [pscustomobject]@{ Id = 99 } } }
    function Get-WDExeVersion([string]$Path) {
        $null = Assert-WDChildPath $Path $root
        $text = [IO.File]::ReadAllText($Path)
        if ($text -eq 'synthetic-old') { [pscustomobject]@{ FileVersion = $old.FileVersion; ProductVersion = $old.ProductVersion } }
        elseif ($text -eq 'synthetic-new') {
            [pscustomobject]@{
                FileVersion = if ($Mode -eq 'AfterReplaceVersion' -and $Path -eq $dist) { '9.9.9.0' } else { '1.0.1.0' }
                ProductVersion = '1.0.1+' + $newCommit
            }
        } else { [pscustomobject]@{ FileVersion = '0.0.0.0'; ProductVersion = 'corrupt' } }
    }
    function Copy-Item {
        param([string]$LiteralPath, [string]$Destination)
        $null = Assert-WDChildPath $LiteralPath $root
        $null = Assert-WDChildPath $Destination $root
        Microsoft.PowerShell.Management\Copy-Item -LiteralPath $LiteralPath -Destination $Destination
        if (($Mode -eq 'BackupExeCorrupt' -and [IO.Path]::GetFileName($Destination) -eq 'previous.exe') -or
            ($Mode -eq 'BackupDbCorrupt' -and $Destination -like '*backups*burgerclock.db') -or
            ($Mode -eq 'SavedNewCorrupt' -and [IO.Path]::GetFileName($Destination) -eq 'deployed.exe')) {
            [IO.File]::WriteAllText($Destination, 'corrupt fixture')
        }
    }
    function Get-WDRegistration([string]$Exe) {
        $null = Assert-WDChildPath $Exe $root
        $state.RegistrationReads++
        $state.Calls.Add('Registration')
        if ($Mode -eq 'RegistrationMissing') { Stop-WD 'RegistrationMissing' }
        $changed = ($Mode -eq 'RegistrationBefore' -and $state.RegistrationReads -ge 2) -or
            ($Mode -eq 'RegistrationAfter' -and $state.RegistrationReads -ge 3) -or
            ($Mode -eq 'RegistrationDryRun' -and $state.RegistrationReads -ge 4) -or
            ($Mode -eq 'RegistrationBeforeLaunch' -and $state.RegistrationReads -ge 5)
        [pscustomobject]@{ Digest = if ($changed) { 'changed' } else { 'synthetic stable shortcut + AUMID + COM + Run + StartupApproved' }; AutoStartRunEntryPresent = $true }
    }
    function Invoke-WDSourceArchive([string]$Repository, [string]$Commit, [string]$Destination) {
        $null = Assert-WDChildPath $Destination $root
        $state.Archives++
        $state.Calls.Add('Archive')
        # Record ordering/fault timing, but execute the unmodified production Git
        # archive implementation. A missing commit exercises an actual Git error.
        $archiveCommit = if ($Mode -eq 'ArchiveFailure') { '0' * 40 } else { $Commit }
        $script:realArchives++
        & $sourceArchiveImplementation $Repository $archiveCommit $Destination
        if ($Mode -eq 'LogsChanged' -and $state.Archives -eq 2) {
            [IO.File]::WriteAllText((Join-Path $data 'logs/nested/synthetic.txt'), 'changed while stopped')
        }
    }
    function Invoke-WDDryRun([string]$Exe) {
        $null = Assert-WDChildPath $Exe $root
        $state.DryRuns++
        $state.Calls.Add('DryRun')
        if ($Mode -eq 'DryRunExit') { return 7 }
        if ($Mode -eq 'DryRunDatabase') { [IO.File]::WriteAllText($db, 'changed by a synthetic misbehaving dry-run') }
        if ($Mode -eq 'DryRunWal') { [IO.File]::WriteAllText(($db + '-wal'), 'preserve unexpected sidecar') }
        if ($Mode -eq 'RollbackLocked') {
            $state.Lock = [IO.File]::Open($dist, 'Open', 'Read', 'Read')
            Stop-WD 'DryRunFailed'
        }
        if ($Mode -eq 'SensitiveError') { throw 'TOKEN fixture-secret https://private.invalid/site account@example.invalid' }
        0
    }
    function Start-WDApp([string]$Exe) {
        $null = Assert-WDChildPath $Exe $root
        $state.Launches++
        $state.Calls.Add('Launch')
        if ($Mode -eq 'LaunchFailure') { Stop-WD 'AppLaunchFailed' }
        12345
    }
    # Tripwires: an unexpected native invocation must fail before making a call.
    function Start-Process { $script:osCalls++; throw 'Native process execution forbidden in fixtures.' }
    function New-Object { $script:osCalls++; throw 'COM/registration access forbidden in fixtures.' }

    $outside = Join-Path $root 'outside repository 한글'
    [IO.Directory]::CreateDirectory($outside) | Out-Null
    Push-Location -LiteralPath $outside
    try { $result = Invoke-WindowsDeployment -Repository $repo -ReceiptPath $receiptPath }
    finally { Pop-Location; if ($state.Lock) { $state.Lock.Dispose() } }
    [pscustomobject]@{
        Mode = $Mode; Result = $result; State = $state; Dist = $dist; OldHash = $old.SHA256; NewHash = $new.SHA256
        CurrentHash = Get-WDHash $dist; DbHash = Get-WDHash $db; OriginalDbHash = $dbHash
        Db = $db; ReceiptPath = $receiptPath; Root = $root; Repository = $repo; InvocationDirectory = $outside
    }
}

$success = Invoke-Fixture
Assert-Test $success.Result.Succeeded "all six stages succeed outside the repository (failure: $($success.Result.Failure))"
Assert-Test ($success.CurrentHash -eq $success.NewHash) 'verified candidate replaces only fake dist'
Assert-Test ($success.DbHash -eq $success.OriginalDbHash -and -not $success.Result.DatabaseRestored) 'DB unchanged, no DB rollback'
Assert-Test (($success.Result.Stages.Result -join ',') -eq 'Passed,Passed,Passed,Passed,Passed,Passed') 'stage summary complete'
Assert-Test (($success.State.Calls -join ',') -eq 'Registration,Archive,Archive,Registration,Registration,DryRun,Registration,Registration,Launch') 'registration baseline precedes replacement, dry-run and final recheck before launch'
Assert-Test ($success.State.Launches -eq 1 -and $success.State.DryRuns -eq 1 -and $success.State.Archives -eq 2) 'one launch, one dry-run, both source archives'
Assert-Test (-not $success.Result.RegistrationPerformed -and $success.Result.AppLaunchRequested) 'normal launch handoff without registration'
Assert-Test ($success.Result.Checks.BackupHashesMatch -and $success.Result.Checks.ActualDataPathVerified -and $success.Result.Checks.NotificationTargetsUnchanged -and $success.Result.Checks.AutoStartPathMatches -and $success.Result.Checks.DryRunChangedNothing -and $success.Result.Checks.UnregisterDryRunExitCode -eq 0) 'summary contains explicit backup/native-path/registration/autostart/dry-run checks'
$backup = $success.Result.BackupRoot
Assert-Test ((Get-WDHash (Join-Path $backup 'previous.exe')) -eq $success.OldHash) 'old EXE backup verified'
Assert-Test ((Get-WDHash (Join-Path $backup 'deployed.exe')) -eq $success.NewHash) 'new EXE backup verified'
Assert-Test ((Get-WDHash (Join-Path $backup 'data/burgerclock.db')) -eq $success.OriginalDbHash) 'DB backup verified'
Assert-Test ((Test-Path -LiteralPath (Join-Path $backup 'previous-dist.zip')) -and (Test-Path -LiteralPath (Join-Path $backup 'source-before.zip')) -and (Test-Path -LiteralPath (Join-Path $backup 'source-candidate.zip'))) 'dist and both source ZIPs retained'
Assert-Test (-not $success.InvocationDirectory.StartsWith($success.Repository + [IO.Path]::DirectorySeparatorChar)) 'deployment current directory is outside the fixture Git repository'
foreach ($snapshot in @(@('source-before.zip','synthetic old source'), @('source-candidate.zip','synthetic new source'))) {
    $archive = [IO.Compression.ZipFile]::OpenRead((Join-Path $backup $snapshot[0]))
    try {
        Assert-Test ($archive.Entries.Count -eq 1 -and $archive.Entries[0].FullName -eq 'synthetic-source.txt') 'real Git ZIP contains only the committed fixture source'
        $reader = [IO.StreamReader]::new($archive.Entries[0].Open())
        try { Assert-Test ($reader.ReadToEnd() -ceq $snapshot[1]) 'real Git ZIP matches the requested old/new commit, not the working tree' }
        finally { $reader.Dispose() }
    } finally { $archive.Dispose() }
}

foreach ($mode in @('Packaged','Wal','Shm','Running','PreviousHash','CandidateHash','CandidateVersion','SourceCommit','ReceiptSchema','WrongRepository','CandidateOutside','ReparseLogs','ReusedReceipt','DeploymentInProgress','BackupExeCorrupt','BackupDbCorrupt','ArchiveFailure','LogsChanged','RegistrationMissing','RegistrationBefore','LockedDist')) {
    $case = Invoke-Fixture $mode
    Assert-Test (-not $case.Result.Succeeded -and $case.State.Launches -eq 0 -and $case.State.DryRuns -eq 0) "$mode stops before dry-run/start"
    Assert-Test ([IO.File]::ReadAllText($case.Dist) -eq 'synthetic-old') "$mode preserves old EXE"
    Assert-Test ($case.DbHash -eq $case.OriginalDbHash) "$mode preserves DB"
    Assert-Test ($case.Result.Stages[-1].Result -eq 'NotRun') "$mode marks downstream stage not run"
    $expectedStage = switch ($mode) {
        { $_ -in @('Packaged','CandidateHash','CandidateVersion','SourceCommit','ReceiptSchema','WrongRepository','CandidateOutside','ReusedReceipt','DeploymentInProgress') } { 'Receipt'; break }
        { $_ -in @('Wal','Shm','Running','PreviousHash','ReparseLogs','RegistrationMissing') } { 'Preflight'; break }
        { $_ -in @('BackupExeCorrupt','BackupDbCorrupt','ArchiveFailure') } { 'Backup'; break }
        default { 'Replace' }
    }
    Assert-Test (@($case.Result.Stages | Where-Object Result -eq 'Failed')[0].Stage -eq $expectedStage) "$mode fails at the intended stage"
    if ($mode -in @('Wal', 'Shm')) {
        Assert-Test (Test-Path -LiteralPath ($case.Db + $(if ($mode -eq 'Wal') { '-wal' } else { '-shm' }))) "$mode sidecar preserved"
    }
}
foreach ($mode in @('AfterReplaceVersion','SavedNewCorrupt','RegistrationAfter','RegistrationDryRun','RegistrationBeforeLaunch','DryRunExit','DryRunDatabase','DryRunWal','SensitiveError','LaunchFailure')) {
    $case = Invoke-Fixture $mode
    Assert-Test (-not $case.Result.Succeeded -and $case.Result.Rollback -eq 'RestoredPreviousExe') "$mode restores previous EXE after replacement"
    Assert-Test ($case.CurrentHash -eq $case.OldHash -and -not $case.Result.DatabaseRestored) "$mode verifies EXE-only rollback"
    Assert-Test ($case.State.Launches -eq $(if ($mode -eq 'LaunchFailure') { 1 } else { 0 })) "$mode does not launch after an earlier failure"
    Assert-Test ($case.DbHash -eq $(if ($mode -eq 'DryRunDatabase') { Get-WDHash $case.Db } else { $case.OriginalDbHash })) "$mode never overwrites live DB with backup"
    if ($mode -eq 'DryRunDatabase') { Assert-Test ($case.DbHash -ne $case.OriginalDbHash) 'dry-run unexpected DB write is detected and retained, not restored' }
    if ($mode -eq 'DryRunWal') { Assert-Test (Test-Path -LiteralPath ($case.Db + '-wal')) 'unexpected sidecar is retained during EXE-only rollback' }
    if ($mode -eq 'SensitiveError') {
        Assert-Test (($case.Result | ConvertTo-Json -Depth 8) -notmatch 'fixture-secret|private.invalid|account@') 'summary never includes raw exception/token/site/body'
    }
    $expectedStage = if ($mode -in @('AfterReplaceVersion','SavedNewCorrupt')) { 'Replace' } elseif ($mode -in @('LaunchFailure','RegistrationBeforeLaunch')) { 'Launch' } else { 'Inspect' }
    Assert-Test (@($case.Result.Stages | Where-Object Result -eq 'Failed')[0].Stage -eq $expectedStage) "$mode fails at the intended stage"
}
$locked = Invoke-Fixture 'RollbackLocked'
Assert-Test (-not $locked.Result.Succeeded -and $locked.Result.Rollback -eq 'FailedPreserveBackupAndStop') 'locked EXE rollback failure is explicit, never claimed restored'
Assert-Test ((Get-WDHash (Join-Path $locked.Result.BackupRoot 'previous.exe')) -eq $locked.OldHash -and $locked.State.Launches -eq 0) 'rollback failure retains verified previous EXE and stops'
Assert-Test ($locked.DbHash -eq $locked.OriginalDbHash) 'rollback failure preserves DB'

# Pure fake keys test snapshot details without reading the Windows registry.
$emptyKey = [pscustomobject]@{}
$emptyKey | Add-Member ScriptMethod GetValueNames { @() }
$emptyKey | Add-Member ScriptMethod GetSubKeyNames { @() }
$emptyKey | Add-Member ScriptMethod Dispose { }
$parentKey = [pscustomobject]@{ Child = $emptyKey; Kind = 'String' }
$parentKey | Add-Member ScriptMethod GetValueNames { @('AI Burger Clock') }
$parentKey | Add-Member ScriptMethod GetValueKind { param($Name) $this.Kind }
$parentKey | Add-Member ScriptMethod GetValue { param($Name, $Default, $Options) 'synthetic command' }
$parentKey | Add-Member ScriptMethod GetSubKeyNames { @('EmptyChild') }
$parentKey | Add-Member ScriptMethod OpenSubKey { param($Name, $Writable) $this.Child }
$tree = @(Get-WDRegistryTree $parentKey)
Assert-Test (@($tree | Where-Object { $_.PSObject.Properties.Name -contains 'Entry' -and $_.Key -eq '\EmptyChild' }).Count -eq 1) 'empty registration subkey existence is part of snapshot'
$stringValue = Get-WDRegistryValue $parentKey 'AI Burger Clock'
$parentKey.Kind = 'ExpandString'
$expandValue = Get-WDRegistryValue $parentKey 'AI Burger Clock'
Assert-Test (($stringValue | ConvertTo-Json -Compress) -ne ($expandValue | ConvertTo-Json -Compress)) 'equal autostart text with changed registry type is detected'
Assert-Test (-not (Get-WDRegistryValue $emptyKey 'AI Burger Clock').Present) 'absent autostart remains absent'

# Receipt preparation is exercised with synthetic identity metadata, not dist.
& {
    $fakeScriptRoot = Join-Path $success.Root 'receipt-generator/tools'
    [IO.Directory]::CreateDirectory($fakeScriptRoot) | Out-Null
    Microsoft.PowerShell.Management\Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'New-WindowsDeploymentReceipt.ps1') -Destination $fakeScriptRoot
    Microsoft.PowerShell.Management\Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'WindowsDeployment.ps1') -Destination $fakeScriptRoot
    $prepArtifacts = Join-Path (Split-Path -Parent $fakeScriptRoot) 'artifacts'
    [IO.Directory]::CreateDirectory((Join-Path $prepArtifacts 'candidate')) | Out-Null
    $prepCandidate = Join-Path $prepArtifacts 'candidate/AI Burger Clock.exe'
    [IO.File]::WriteAllText($prepCandidate, 'synthetic-new')
    $previousIdentity = Join-Path $prepArtifacts 'previous.json'
    @{ FileVersion = '1.0.0.0'; ProductVersion = '1.0.0+' + ('a' * 40); SourceCommit = 'a' * 40; SHA256 = 'a' * 64 } | ConvertTo-Json | Set-Content -LiteralPath $previousIdentity
    # A test-only copy changes just the OS version-reading boundary, leaving the
    # actual receipt generator and validation logic unchanged.
    $boundary = "function Get-WDExeVersion { param([string]`$Path) [pscustomobject]@{ FileVersion='1.0.1.0'; ProductVersion='1.0.1+' + ('b' * 40) } }"
    Add-Content -LiteralPath (Join-Path $fakeScriptRoot 'WindowsDeployment.ps1') -Value $boundary
    $generatedPath = Join-Path $prepArtifacts 'deployment-receipt.json'
    $outside = Join-Path $success.Root 'receipt caller outside repository'
    [IO.Directory]::CreateDirectory($outside) | Out-Null
    Push-Location -LiteralPath $outside
    try { $null = & (Join-Path $fakeScriptRoot 'New-WindowsDeploymentReceipt.ps1') -CandidatePath $prepCandidate -PreviousIdentityPath $previousIdentity -ReceiptPath $generatedPath }
    finally { Pop-Location }
    $generated = Get-Content -LiteralPath $generatedPath -Raw | ConvertFrom-Json
    Assert-Test ($generated.Candidate.SHA256 -eq (Get-WDHash $prepCandidate) -and $generated.Candidate.SourceCommit -eq ('b' * 40)) 'preparation derives candidate hash/version/source from fake artifact'
    Assert-Test ($generated.Previous.SHA256 -eq ('a' * 64)) 'preparation pins previous release identity from receipt, never dist'
    $refused = $false
    try { $null = & (Join-Path $fakeScriptRoot 'New-WindowsDeploymentReceipt.ps1') -CandidatePath $prepCandidate -PreviousIdentityPath $previousIdentity -ReceiptPath $generatedPath }
    catch { $refused = $true }
    Assert-Test $refused 'preparation never overwrites an existing receipt'
    foreach ($relativeField in @('CandidatePath','PreviousIdentityPath','ReceiptPath')) {
        $arguments = @{ CandidatePath = $prepCandidate; PreviousIdentityPath = $previousIdentity; ReceiptPath = Join-Path $prepArtifacts ($relativeField + '.json') }
        $arguments[$relativeField] = 'relative-path-is-not-approved'
        $failure = $null
        try { $null = & (Join-Path $fakeScriptRoot 'New-WindowsDeploymentReceipt.ps1') @arguments }
        catch { $failure = $_.Exception.Message }
        Assert-Test ($failure -ceq 'DEPLOY:AbsolutePathRequired') "preparation rejects relative $relativeField instead of resolving it against the caller"
    }
}

# Keep the real ProcessStartInfo setup; replace only the process constructor in
# a test-local copy, so no EXE or registration command can actually be started.
& {
    $script:cases++
    $body = (Get-Command Invoke-WDDryRun).ScriptBlock.ToString()
    $constructor = '[Diagnostics.Process]::new()'
    Assert-Test (($body.Split($constructor).Count - 1) -eq 1) 'dry-run fixture substitutes exactly one process constructor'
    $fakeProcess = [pscustomobject]@{ StartInfo = $null; ExitCode = 0; Starts = 0; StandardOutput = [IO.StringReader]::new('Dry run: no registration changed.'); StandardError = [IO.StringReader]::new('') }
    $fakeProcess | Add-Member ScriptMethod Start { $this.Starts++; $true }
    $fakeProcess | Add-Member ScriptMethod WaitForExit { param($Milliseconds) $true }
    $fakeProcess | Add-Member ScriptMethod Dispose { $this.StandardOutput.Dispose(); $this.StandardError.Dispose() }
    $dryRun = [scriptblock]::Create($body.Replace($constructor, '$fakeProcess'))
    $exitCode = & $dryRun $success.Dist
    Assert-Test ($exitCode -eq 0 -and $fakeProcess.Starts -eq 1) 'dry-run fixture completes through the fake process only'
    Assert-Test ($fakeProcess.StartInfo.WorkingDirectory -ceq (Split-Path -Parent $success.Dist)) 'dry-run child uses its EXE directory, not the caller directory'
    Assert-Test (($fakeProcess.StartInfo.ArgumentList -join ',') -ceq '--unregister-quota-notifications,--dry-run') 'dry-run arguments remain unchanged'
    Assert-Test (-not $fakeProcess.StartInfo.UseShellExecute -and $fakeProcess.StartInfo.CreateNoWindow -and $fakeProcess.StartInfo.RedirectStandardOutput -and $fakeProcess.StartInfo.RedirectStandardError) 'dry-run retains hidden execution and redirected output'
}
& {
    $script:cases++
    $fakeProcess = [pscustomobject]@{ Id = 12345 }
    $fakeProcess | Add-Member ScriptMethod Dispose { }
    function Start-Process {
        param($FilePath, $WorkingDirectory, $WindowStyle, [switch]$PassThru)
        $script:launchArguments = @{ FilePath=$FilePath; WorkingDirectory=$WorkingDirectory; WindowStyle=$WindowStyle; PassThru=$PassThru.IsPresent }
        $fakeProcess
    }
    Assert-Test ((Start-WDApp $success.Dist) -eq 12345) 'normal launch helper returns the fake handoff PID'
    Assert-Test ($script:launchArguments.FilePath -ceq $success.Dist -and $script:launchArguments.WorkingDirectory -ceq (Split-Path -Parent $success.Dist)) 'normal launch already pins the EXE path and working directory'
    Assert-Test ($script:launchArguments.WindowStyle -eq 'Hidden' -and $script:launchArguments.PassThru) 'normal launch flags remain unchanged'
}

foreach ($file in @('deploy-windows.ps1','tools/WindowsDeployment.ps1','tools/New-WindowsDeploymentReceipt.ps1','tools/Test-WindowsDeployment.ps1')) {
    $tokens = $null; $parseErrors = $null
    $null = [Management.Automation.Language.Parser]::ParseFile((Join-Path $repository $file), [ref]$tokens, [ref]$parseErrors)
    Assert-Test ($parseErrors.Count -eq 0) "$file parses"
}
Assert-Test ($script:osCalls -eq 0) 'native app launches, COM/registration calls, OS notifications: zero'
$summary = [pscustomobject]@{ Slot = $Slot; Cases = $script:cases; Assertions = $script:checks; RealGitArchiveCalls = $script:realArchives; NativeAppLaunches = 0; RegistrationCalls = 0; OperatingDataAccess = 0; FixtureRoot = $fixtureRoot }
$evidence = Join-Path $repository 'artifacts/windows-external-deployment'
[IO.Directory]::CreateDirectory($evidence) | Out-Null
$summary | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence "summary-$Slot.json") -Encoding utf8
$summary | ConvertTo-Json
