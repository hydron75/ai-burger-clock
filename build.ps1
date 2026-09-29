[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$Publish
)

$ErrorActionPreference = 'Stop'
$projectDirectory = $PSScriptRoot
$projectPath = Join-Path $projectDirectory 'AiBurgerClock.csproj'

& dotnet build $projectPath -c $Configuration --nologo -warnaserror
if ($LASTEXITCODE -ne 0) { throw "Build failed: $LASTEXITCODE" }

$outputPath = Join-Path $projectDirectory "bin\$Configuration\net10.0-windows\win-x64\AI Burger Clock.exe"
if ($Publish) {
    & dotnet publish $projectPath -c $Configuration -p:PublishProfile=Portable --nologo -warnaserror
    if ($LASTEXITCODE -ne 0) { throw "Publish failed: $LASTEXITCODE" }
    $outputPath = Join-Path $projectDirectory 'dist\win-x64\AI Burger Clock.exe'
}

$reportDirectory = Join-Path $projectDirectory 'artifacts'
New-Item -ItemType Directory -Force -Path $reportDirectory | Out-Null
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
