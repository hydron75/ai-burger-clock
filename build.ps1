[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$Publish,
    [ValidatePattern('^[a-zA-Z0-9][a-zA-Z0-9._-]{0,79}$')]
    [string]$ReportGroup
)

$ErrorActionPreference = 'Stop'
$projectDirectory = $PSScriptRoot
$projectPath = Join-Path $projectDirectory 'AiBurgerClock.csproj'
$reportDirectory = Join-Path $projectDirectory 'artifacts'
if ($ReportGroup) {
    $kst = [TimeZoneInfo]::ConvertTimeBySystemTimeZoneId([DateTime]::UtcNow, 'Korea Standard Time')
    $reportDirectory = Join-Path $reportDirectory "$ReportGroup\build\$($kst.ToString('yyyyMMdd-HHmmss-fff'))"
}
New-Item -ItemType Directory -Force -Path $reportDirectory | Out-Null

if ($ReportGroup) {
    & dotnet build $projectPath -c $Configuration --nologo -warnaserror 2>&1 | Tee-Object -FilePath (Join-Path $reportDirectory 'build.txt')
} else {
    & dotnet build $projectPath -c $Configuration --nologo -warnaserror
}
if ($LASTEXITCODE -ne 0) { throw "Build failed: $LASTEXITCODE" }

$outputPath = Join-Path $projectDirectory "bin\$Configuration\net10.0-windows10.0.19041.0\win-x64\AI Burger Clock.exe"
$publishDirectory = $null
$preservePublishDirectory = $false
try {
    if ($Publish) {
        # Command-line global properties override the Portable profile's fixed dist
        # paths. The final single EXE is tested here before dist is changed at all.
        $publishDirectory = Join-Path $reportDirectory ('.publish-' + [Guid]::NewGuid().ToString('N'))
        [IO.Directory]::CreateDirectory($publishDirectory) | Out-Null
        $publishArguments = @('publish', $projectPath, '-c', $Configuration, '-p:PublishProfile=Portable', "-p:PublishDir=$publishDirectory\", "-p:PublishUrl=$publishDirectory\", '--nologo', '-warnaserror')
        if ($ReportGroup) {
            & dotnet @publishArguments 2>&1 | Tee-Object -FilePath (Join-Path $reportDirectory 'publish.txt')
        } else {
            & dotnet @publishArguments
        }
        if ($LASTEXITCODE -ne 0) { throw "Publish failed: $LASTEXITCODE" }
        $outputPath = Join-Path $publishDirectory 'AI Burger Clock.exe'
    }

    $report = Join-Path $reportDirectory 'self-test.txt'
    $errorReport = Join-Path $reportDirectory 'self-test-errors.txt'
    # WinExe does not reliably update PowerShell's LASTEXITCODE. Wait for this exact process.
    $test = Start-Process -FilePath $outputPath -ArgumentList '--self-test' -Wait -PassThru -WindowStyle Hidden -RedirectStandardOutput $report -RedirectStandardError $errorReport
    Get-Content -LiteralPath $report
    if ($test.ExitCode -ne 0) {
        Get-Content -LiteralPath $errorReport
        throw "Self-test failed: $($test.ExitCode)"
    }

    if ($Publish) {
        $destination = Join-Path $projectDirectory 'dist\win-x64\AI Burger Clock.exe'
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination)) | Out-Null
        if ([IO.File]::Exists($destination)) {
            $previous = Join-Path $publishDirectory 'previous.exe'
            try { [IO.File]::Replace($outputPath, $destination, $previous) }
            catch {
                # A locked destination normally remains unchanged. If Windows got
                # as far as creating the backup before failing, restore it first;
                # an unsuccessful restore must never delete the recoverable copy.
                if ([IO.File]::Exists($previous)) {
                    $preservePublishDirectory = $true
                    try {
                        [IO.File]::Copy($previous, $destination, $true)
                        $preservePublishDirectory = $false
                    } catch {
                        throw "Publish replacement and restore failed. Previous executable retained at $previous. $($_.Exception.Message)"
                    }
                }
                throw
            }
        } else {
            [IO.File]::Move($outputPath, $destination)
        }
        $outputPath = $destination
    }
    Write-Host "Build and self-test passed: $outputPath"
    if ($ReportGroup) { Write-Host "Reports: $reportDirectory" }
} finally {
    if ($publishDirectory -and -not $preservePublishDirectory -and [IO.Directory]::Exists($publishDirectory)) {
        # Only the unique directory created by this invocation, never dist or an
        # existing publish/report directory, is eligible for recursive cleanup.
        $cleanupPath = [IO.Path]::GetFullPath($publishDirectory)
        $cleanupRoot = [IO.Path]::GetFullPath($reportDirectory).TrimEnd('\') + '\'
        if (-not $cleanupPath.StartsWith($cleanupRoot, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Publish staging cleanup escaped its report directory; nothing was removed.'
        }
        Remove-Item -LiteralPath $publishDirectory -Recurse -Force
    }
}
