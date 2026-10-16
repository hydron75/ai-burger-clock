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

$outputPath = Join-Path $projectDirectory "bin\$Configuration\net10.0-windows\win-x64\AI Burger Clock.exe"
if ($Publish) {
    if ($ReportGroup) {
        & dotnet publish $projectPath -c $Configuration -p:PublishProfile=Portable --nologo -warnaserror 2>&1 | Tee-Object -FilePath (Join-Path $reportDirectory 'publish.txt')
    } else {
        & dotnet publish $projectPath -c $Configuration -p:PublishProfile=Portable --nologo -warnaserror
    }
    if ($LASTEXITCODE -ne 0) { throw "Publish failed: $LASTEXITCODE" }
    $outputPath = Join-Path $projectDirectory 'dist\win-x64\AI Burger Clock.exe'
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

Write-Host "Build and self-test passed: $outputPath"
if ($ReportGroup) { Write-Host "Reports: $reportDirectory" }
