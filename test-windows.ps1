#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('before', 'after')][string]$Slot,
    [Parameter(Mandatory)][string]$SourceDirectory,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-fA-F]{40}$')][string]$SourceCommit,
    [Parameter(Mandatory)][ValidatePattern('^[a-zA-Z0-9][a-zA-Z0-9._-]{0,79}$')][string]$ReportGroup,
    [ValidateRange(1, 20)][int]$Repeat = 3,
    [switch]$StageOnly
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $IsWindows) { throw 'This runner requires Windows and PowerShell 7.' }
$Slot = $Slot.ToLowerInvariant()
$exeName = 'AI Burger Clock.exe'
$markerName = '.aiburgerclock-test-slot.json'
# A user-profile directory avoids packaged-desktop AppData virtualization changing the registered EXE path.
$fixedRoot = Join-Path ([Environment]::GetFolderPath('UserProfile')) '.ai-burger-clock-tests'
$slotDirectory = Join-Path $fixedRoot $Slot
$slotExe = Join-Path $slotDirectory $exeName
$sourceDirectoryPath = (Resolve-Path -LiteralPath $SourceDirectory).Path
$sourceExe = Join-Path $sourceDirectoryPath $exeName
$reportBase = Join-Path $PSScriptRoot "artifacts\$ReportGroup\windows-tests\$Slot"
$ownerFormat = 'AiBurgerClock.WindowsTestSlot.v1'

function Test-Within([string]$Child, [string]$Parent) {
    $childPath = [IO.Path]::GetFullPath($Child).TrimEnd('\')
    $parentPath = [IO.Path]::GetFullPath($Parent).TrimEnd('\')
    return $childPath.Equals($parentPath, [StringComparison]::OrdinalIgnoreCase) -or
        $childPath.StartsWith($parentPath + '\', [StringComparison]::OrdinalIgnoreCase)
}

function Assert-NoReparse([string]$Path) {
    $cursor = [IO.Path]::GetFullPath($Path)
    while ($cursor) {
        if (Test-Path -LiteralPath $cursor) {
            $item = Get-Item -LiteralPath $cursor -Force
            if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Reparse point refused: $cursor" }
        }
        $parent = [IO.Directory]::GetParent($cursor)
        $cursor = if ($parent) { $parent.FullName } else { $null }
    }
}

function Get-SlotFile([string]$RelativePath) {
    if ([IO.Path]::IsPathRooted($RelativePath) -or $RelativePath -eq $markerName) { throw 'Invalid slot manifest path.' }
    $path = [IO.Path]::GetFullPath((Join-Path $slotDirectory $RelativePath))
    if ($path -eq $slotDirectory -or -not (Test-Within $path $slotDirectory)) { throw 'Slot manifest escapes its directory.' }
    Assert-NoReparse $path
    return $path
}

function Assert-RuntimePath([string]$RelativePath) {
    $extension = [IO.Path]::GetExtension($RelativePath)
    if ($extension -notin @('.exe', '.dll', '.json', '.pdb', '.xml') -or
        ($extension -ieq '.json' -and $RelativePath -notin @('AI Burger Clock.deps.json', 'AI Burger Clock.runtimeconfig.json'))) {
        throw "Non-runtime file refused in payload: $RelativePath"
    }
}

function Save-Json([string]$Path, $Value) {
    [IO.File]::WriteAllText($Path, (ConvertTo-Json -InputObject $Value -Depth 12), [Text.UTF8Encoding]::new($false))
}

function Assert-AppStopped {
    $running = @(Get-CimInstance Win32_Process -Filter "Name = 'AI Burger Clock.exe'" -ErrorAction Stop)
    if ($running.Count) { throw 'AI Burger Clock is running. Exit it normally before staging or running smoke; no process was stopped.' }
}

function Read-TrayRecords {
    $root = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Control Panel\NotifyIconSettings', $false)
    if (-not $root) { throw 'Windows NotifyIconSettings is unavailable; tray-history growth cannot be verified.' }
    try {
        foreach ($id in ($root.GetSubKeyNames() | Sort-Object)) {
            $key = $root.OpenSubKey($id, $false)
            if (-not $key) { continue }
            try {
                $path = [string]$key.GetValue('ExecutablePath')
                if ([IO.Path]::GetFileName($path) -ieq $exeName) {
                    [pscustomobject]@{ Key = $id; ExecutablePath = $path; UID = $key.GetValue('UID'); IsPromoted = $key.GetValue('IsPromoted') }
                }
            } finally { $key.Dispose() }
        }
    } finally { $root.Dispose() }
}

function Assert-Payload($Files) {
    $rootMarker = Join-Path $slotDirectory $markerName
    $actual = @(Get-ChildItem -LiteralPath $slotDirectory -Force -Recurse -File | Where-Object FullName -ne $rootMarker)
    if ($actual.Count -ne $Files.Count) { throw 'Staged payload file count differs from the manifest.' }
    $expectedPaths = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($file in $Files) {
        Assert-RuntimePath $file.RelativePath
        $path = Get-SlotFile $file.RelativePath
        if (-not $expectedPaths.Add($path)) { throw 'Duplicate canonical path in the payload manifest.' }
        if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or
            (Get-Item -LiteralPath $path).Length -ne $file.Length -or
            (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $file.SHA256) {
            throw "Staged payload differs from the manifest: $($file.RelativePath)"
        }
    }
    foreach ($item in $actual) {
        Assert-NoReparse $item.FullName
        if (-not $expectedPaths.Contains($item.FullName)) { throw 'Unexpected file in the staged payload.' }
    }
}

Assert-NoReparse $sourceDirectoryPath
Assert-NoReparse $fixedRoot
Assert-NoReparse $reportBase
if (-not (Test-Path -LiteralPath $sourceExe -PathType Leaf)) { throw 'Source executable is missing.' }
if ((Test-Within $sourceDirectoryPath $fixedRoot) -or (Test-Within $fixedRoot $sourceDirectoryPath) -or
    (Test-Within $reportBase $fixedRoot) -or (Test-Within $fixedRoot $reportBase) -or
    (Test-Within $reportBase $sourceDirectoryPath) -or (Test-Within $sourceDirectoryPath $reportBase)) {
    throw 'Source, fixed slots, and reports must not overlap.'
}
$version = (Get-Item -LiteralPath $sourceExe).VersionInfo.ProductVersion
if (-not $version.EndsWith('+' + $SourceCommit, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Source executable commit does not match -SourceCommit. ProductVersion: $version"
}
$sourceItems = @(Get-ChildItem -LiteralPath $sourceDirectoryPath -Force -Recurse)
$files = @(foreach ($item in $sourceItems) {
    Assert-NoReparse $item.FullName
    if ($item.PSIsContainer) { continue }
    $relativePath = [IO.Path]::GetRelativePath($sourceDirectoryPath, $item.FullName)
    Assert-RuntimePath $relativePath
    [pscustomobject]@{
        RelativePath = $relativePath
        Length = $item.Length
        SHA256 = (Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash
    }
})
Assert-AppStopped
[IO.Directory]::CreateDirectory($fixedRoot) | Out-Null
$lock = $null
try {
    $lockPath = Join-Path $fixedRoot 'runner.lock'
    Assert-NoReparse $lockPath
    try { $lock = [IO.File]::Open($lockPath, 'OpenOrCreate', 'ReadWrite', 'None') }
    catch { throw 'Another fixed-slot test runner holds the lock.' }
    Assert-AppStopped
    Assert-NoReparse $slotDirectory
    [IO.Directory]::CreateDirectory($slotDirectory) | Out-Null
    $markerPath = Join-Path $slotDirectory $markerName
    $existing = @(Get-ChildItem -LiteralPath $slotDirectory -Force -Recurse -File)
    if ($existing.Count) {
        if (-not (Test-Path -LiteralPath $markerPath -PathType Leaf)) { throw 'Nonempty slot has no ownership manifest; nothing was overwritten.' }
        Assert-NoReparse $markerPath
        $old = Get-Content -LiteralPath $markerPath -Raw | ConvertFrom-Json
        if ($old.Format -ne $ownerFormat -or $old.Slot -ne $Slot -or $old.Directory -ne $slotDirectory) { throw 'Slot ownership mismatch.' }
        foreach ($entry in (Get-ChildItem -LiteralPath $slotDirectory -Force -Recurse)) { Assert-NoReparse $entry.FullName }
        Assert-Payload @($old.Files)
        foreach ($file in $old.Files) {
            if ($file.RelativePath -notin $files.RelativePath) {
                # Remove only an unchanged, previously owned runtime file, never a directory or report.
                Remove-Item -LiteralPath (Get-SlotFile $file.RelativePath) -Force
            }
        }
    }
    foreach ($file in $files) {
        $destination = Get-SlotFile $file.RelativePath
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination)) | Out-Null
        Copy-Item -LiteralPath (Join-Path $sourceDirectoryPath $file.RelativePath) -Destination $destination -Force
    }
    Assert-Payload $files
    $manifest = [pscustomobject]@{ Format = $ownerFormat; Slot = $Slot; Directory = $slotDirectory; SourceCommit = $SourceCommit; ProductVersion = $version; Files = $files }
    Save-Json $markerPath $manifest
    $kst = [TimeZoneInfo]::ConvertTimeBySystemTimeZoneId([DateTime]::UtcNow, 'Korea Standard Time')
    $session = Join-Path $reportBase ($kst.ToString('yyyyMMdd-HHmmss-fff') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 6))
    Assert-NoReparse $session
    [IO.Directory]::CreateDirectory($session) | Out-Null
    Save-Json (Join-Path $session 'payload-manifest.json') $manifest
    Write-Host "Verified fixed $Slot slot: $slotExe"
    Write-Host "Reports: $session"
    if ($StageOnly) { return }

    $initial = @(Read-TrayRecords)
    Save-Json (Join-Path $session 'tray-before.json') $initial
    $runs = @()
    $firstSlotKeys = $null
    for ($run = 1; $run -le $Repeat; $run++) {
        Assert-AppStopped
        Assert-Payload $files
        $directory = Join-Path $session ('run-{0:D2}' -f $run)
        $images = Join-Path $directory 'images'
        [IO.Directory]::CreateDirectory($images) | Out-Null
        $stdout = Join-Path $directory 'stdout.txt'
        $stderr = Join-Path $directory 'stderr.txt'
        $before = @(Read-TrayRecords)
        Save-Json (Join-Path $directory 'tray-before.json') $before
        $watch = [Diagnostics.Stopwatch]::StartNew()
        $process = $null
        $failure = $null
        $exitCode = $null
        $timedOut = $false
        try {
            $process = Start-Process -FilePath $slotExe -ArgumentList @('--smoke-test', '--report-directory', ('"' + $images + '"')) -WorkingDirectory $slotDirectory -WindowStyle Hidden -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
            if (-not $process.WaitForExit(180000)) {
                $timedOut = $true
                # Only this synthetic smoke child is ours to stop, never an existing app.
                if (-not $process.HasExited) { $process.Kill($true); $process.WaitForExit() }
                throw "Smoke timed out after 180 seconds. Only spawned test PID $($process.Id) was terminated."
            }
            $exitCode = $process.ExitCode
        } catch { $failure = $_.Exception.Message }
        finally { if ($process) { $process.Dispose() }; $watch.Stop() }
        Start-Sleep -Seconds 2 # Observe registry persistence after native tray disposal; not a smoke assertion delay.
        $after = @()
        try { $after = @(Read-TrayRecords) }
        catch { $failure = "Tray snapshot unavailable: $($_.Exception.Message); smoke failure: $failure" }
        Save-Json (Join-Path $directory 'tray-after.json') $after
        $slotKeys = @($after | Where-Object { $_.ExecutablePath -ieq $slotExe } | ForEach-Object Key | Sort-Object)
        if ($run -eq 1) { $firstSlotKeys = @($slotKeys) }
        $beforeKeys = @($before | ForEach-Object Key)
        $newKeys = @($after | Where-Object { $_.Key -notin $beforeKeys } | ForEach-Object Key)
        $growthAfterFirst = @($slotKeys | Where-Object { $_ -notin $firstSlotKeys })
        $passes = if (Test-Path -LiteralPath $stdout) { @(Select-String -LiteralPath $stdout -Pattern '^PASS:').Count } else { 0 }
        $pngCount = @(Get-ChildItem -LiteralPath $images -File -Filter '*.png' -Recurse).Count
        $runs += [pscustomobject]@{ Run = $run; ExitCode = $exitCode; Failure = $failure; TimedOut = $timedOut; Passes = $passes; PngFiles = $pngCount; Seconds = [Math]::Round($watch.Elapsed.TotalSeconds, 2); TotalTrayRecords = $after.Count; SlotTrayRecords = $slotKeys.Count; NewKeys = $newKeys; AddedSinceFirstRun = $growthAfterFirst }
        $growthStatus = if ($Repeat -eq 1) { 'NotEvaluated' }
            elseif ($failure -or $exitCode -ne 0 -or $passes -eq 0 -or $pngCount -eq 0 -or $slotKeys.Count -eq 0) { 'Indeterminate' }
            elseif ($growthAfterFirst.Count) { 'Failed' }
            elseif ($run -eq $Repeat) { 'Passed' }
            else { 'InProgress' }
        $growthVerification = [pscustomobject]@{ Status = $growthStatus; Comparisons = [Math]::Max(0, $run - 1); RequiredRuns = 2 }
        Save-Json (Join-Path $session 'summary.json') ([pscustomobject]@{ Slot = $Slot; SourceCommit = $SourceCommit; ProductVersion = $version; Repeat = $Repeat; RegistryObservationDelaySeconds = 2; InitialTrayRecords = $initial.Count; TrayGrowthVerification = $growthVerification; Runs = $runs })
        Write-Host "Run $run/$Repeat : exit=$exitCode PASS=$passes PNG=$pngCount slotRecords=$($slotKeys.Count) addedSinceFirst=$($growthAfterFirst.Count)"
        if ($failure -or $exitCode -ne 0 -or $passes -eq 0 -or $pngCount -eq 0) { throw "Smoke failed in $directory; remaining runs were not started. $failure" }
        if ($slotKeys.Count -eq 0) { throw 'No attributable slot tray record was found; no-growth verification is indeterminate.' }
        if ($growthAfterFirst.Count) { throw 'Tray records grew after the first run; inspect the retained snapshots.' }
    }
    if ($Repeat -eq 1) {
        Write-Host 'Smoke passed; tray-history no-growth verification was not evaluated (one run; at least two runs are required).'
    } else {
        Write-Host 'Smoke passed; no additional slot tray keys were observed after the first run.'
    }
} finally {
    if ($lock) { $lock.Dispose() }
}
