#requires -Version 7.0
[CmdletBinding()]
param(
    [ValidateSet('All', 'BUILD-01', 'BUILD-05')][string]$Case = 'All',
    [string]$EvidenceDirectory
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $IsWindows) { throw 'These Windows script regressions require PowerShell 7 on Windows.' }
$repository = Split-Path -Parent $PSScriptRoot
if (-not $EvidenceDirectory) { $EvidenceDirectory = Join-Path $repository 'artifacts/review-windows/build' }
[IO.Directory]::CreateDirectory($EvidenceDirectory) | Out-Null
$fixture = Join-Path $EvidenceDirectory ('fixture-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($fixture) | Out-Null
$script:checks = 0
function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw "FAIL: $Message" }
    $script:checks++
    Write-Output "PASS: $Message"
}

function Invoke-PublishFixture([string]$Name, [bool]$SelfTestSucceeds, [bool]$LockDestination = $false) {
    $directory = Join-Path $fixture $Name
    [IO.Directory]::CreateDirectory((Join-Path $directory 'dist/win-x64')) | Out-Null
    Copy-Item -LiteralPath (Join-Path $repository 'build.ps1') -Destination (Join-Path $directory 'build.ps1')
    $destination = Join-Path $directory 'dist/win-x64/AI Burger Clock.exe'
    [IO.File]::WriteAllText($destination, 'previous verified executable (synthetic)')
    $previousHash = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash
    $state = [pscustomobject]@{ Published = $null; Tested = $null; PublishArguments = @(); Failure = $null }
    $handle = if ($LockDestination) { [IO.File]::Open($destination, 'Open', 'Read', 'Read') } else { $null }
    try {
        & {
            function dotnet {
                $global:LASTEXITCODE = 0
                if ($args[0] -eq 'publish') {
                    $state.PublishArguments = @($args)
                    $override = @($args | Where-Object { $_ -like '-p:PublishDir=*' })
                    $publishDirectory = if ($override.Count) { $override[0].Substring('-p:PublishDir='.Length) } else { Join-Path $directory 'dist/win-x64' }
                    [IO.Directory]::CreateDirectory($publishDirectory) | Out-Null
                    $state.Published = Join-Path $publishDirectory 'AI Burger Clock.exe'
                    [IO.File]::WriteAllText($state.Published, 'candidate executable (synthetic)')
                }
                Write-Output 'Synthetic dotnet succeeded; no compiler or updater was executed.'
            }
            function Start-Process {
                param($FilePath, $ArgumentList, [switch]$Wait, [switch]$PassThru, $WindowStyle, $RedirectStandardOutput, $RedirectStandardError)
                $state.Tested = $FilePath
                [IO.File]::WriteAllText($RedirectStandardOutput, 'Synthetic self-test fixture')
                [IO.File]::WriteAllText($RedirectStandardError, '')
                [pscustomobject]@{ ExitCode = $(if ($SelfTestSucceeds) { 0 } else { 23 }) }
            }
            try { & (Join-Path $directory 'build.ps1') -Publish -ReportGroup synthetic | Out-Host }
            catch { $state.Failure = $_.Exception.Message }
        }
    } finally { if ($handle) { $handle.Dispose() } }
    [pscustomobject]@{ Directory = $directory; Destination = $destination; PreviousHash = $previousHash; CurrentHash = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash; State = $state }
}

function Replace-FunctionBody([string]$Source, [string]$Name, [string]$Body) {
    $tokens = $null; $parseErrors = $null
    $ast = [Management.Automation.Language.Parser]::ParseInput($Source, [ref]$tokens, [ref]$parseErrors)
    if ($parseErrors.Count) { throw 'Fixture source failed to parse.' }
    $function = $ast.Find({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $Name }, $true)
    if (-not $function) { throw "Fixture boundary function not found: $Name" }
    $start = $function.Body.Extent.StartOffset
    $length = $function.Body.Extent.EndOffset - $start
    $Source.Remove($start, $length).Insert($start, "{`n$Body`n}")
}

function Invoke-RunnerFixture([int]$Repetitions) {
    $directory = Join-Path $fixture "runner-$Repetitions"
    [IO.Directory]::CreateDirectory($directory) | Out-Null
    $source = Get-Content -LiteralPath (Join-Path $repository 'test-windows.ps1') -Raw
    # Only OS boundaries are replaced in this isolated script copy. The actual loop,
    # reporting and verdict logic under test remain the repository's current source.
    $source = Replace-FunctionBody $source 'Assert-AppStopped' ''
    $source = Replace-FunctionBody $source 'Read-TrayRecords' @'
[pscustomobject]@{ Key = 'synthetic-stable-tray-key'; ExecutablePath = $slotExe; UID = 1; IsPromoted = 0 }
'@
    $source = $source.Replace("`$fixedRoot = Join-Path ([Environment]::GetFolderPath('UserProfile')) '.ai-burger-clock-tests'", "`$fixedRoot = Join-Path `$PSScriptRoot 'synthetic-slots'")
    $source = $source.Replace("`$version = (Get-Item -LiteralPath `$sourceExe).VersionInfo.ProductVersion", "`$version = 'synthetic+' + `$SourceCommit")
    $runner = Join-Path $directory 'test-windows.ps1'
    [IO.File]::WriteAllText($runner, $source)
    $payload = Join-Path $directory 'synthetic-source'
    [IO.Directory]::CreateDirectory($payload) | Out-Null
    [IO.File]::WriteAllText((Join-Path $payload 'AI Burger Clock.exe'), 'not an executable; no UI or registry is used')
    $output = & {
        function Start-Sleep { param($Seconds) }
        function Start-Process {
            param($FilePath, $ArgumentList, $WorkingDirectory, $WindowStyle, [switch]$PassThru, $RedirectStandardOutput, $RedirectStandardError)
            [IO.File]::WriteAllText($RedirectStandardOutput, 'PASS: synthetic smoke')
            [IO.File]::WriteAllText($RedirectStandardError, '')
            $images = $ArgumentList[2].Trim('"')
            [IO.File]::WriteAllText((Join-Path $images 'synthetic.png'), 'not an image; counted fixture only')
            $process = [pscustomobject]@{ ExitCode = 0 }
            $process | Add-Member ScriptMethod WaitForExit { param($Milliseconds) $true }
            $process | Add-Member ScriptMethod Dispose { }
            $process
        }
        & $runner -Slot after -SourceDirectory $payload -SourceCommit ('a' * 40) -ReportGroup synthetic -Repeat $Repetitions 6>&1 | ForEach-Object { $_.ToString() }
    }
    $summaryPath = @(Get-ChildItem -LiteralPath (Join-Path $directory 'artifacts') -Filter summary.json -File -Recurse)
    if ($summaryPath.Count -ne 1) { throw 'Expected one isolated runner summary.' }
    $summary = Get-Content -LiteralPath $summaryPath[0].FullName -Raw | ConvertFrom-Json
    $output | Write-Output
    [pscustomobject]@{ Output = @($output); Summary = $summary }
}

if ($Case -in @('All', 'BUILD-01')) {
    $failed = Invoke-PublishFixture 'failed-self-test' $false
    Assert-True ($failed.State.Failure -like '*Self-test failed: 23*') 'BUILD-01 rejects a candidate whose self-test fails'
    Assert-True ($failed.CurrentHash -eq $failed.PreviousHash) 'BUILD-01 failed candidate preserves the previous dist executable'
    Assert-True ($failed.State.Published -eq $failed.State.Tested -and $failed.State.Tested -ne $failed.Destination) 'BUILD-01 validates the staged publish executable, not the build output or existing dist'
    Assert-True (@($failed.State.PublishArguments | Where-Object { $_ -like '-p:PublishDir=*' }).Count -eq 1 -and @($failed.State.PublishArguments | Where-Object { $_ -like '-p:PublishUrl=*' }).Count -eq 1) 'BUILD-01 overrides both publish-profile paths for the candidate'
    $passed = Invoke-PublishFixture 'successful-self-test' $true
    Assert-True (-not $passed.State.Failure -and $passed.CurrentHash -ne $passed.PreviousHash) 'BUILD-01 replaces dist only after successful candidate self-test'
    $locked = Invoke-PublishFixture 'locked-destination' $true $true
    Assert-True ([bool]$locked.State.Failure) 'BUILD-01 reports an actual locked-destination replacement failure'
    Assert-True ($locked.CurrentHash -eq $locked.PreviousHash) 'BUILD-01 replacement failure preserves the previous dist executable'
    # Evaluate the real Portable profile with MSBuild, without restore/build/publish.
    # This separately verifies the global-property precedence assumed by the mock.
    $profile = [Security.SecurityElement]::Escape((Join-Path $repository 'Properties/PublishProfiles/Portable.pubxml'))
    $propertyProject = Join-Path $fixture 'PublishProperties.proj'
    [IO.File]::WriteAllText($propertyProject, "<Project><Import Project=`"$profile`" /></Project>")
    $candidateDirectory = Join-Path $fixture 'msbuild-candidate'
    $properties = & dotnet msbuild $propertyProject '-getProperty:PublishDir,PublishUrl' "-p:PublishDir=$candidateDirectory\" "-p:PublishUrl=$candidateDirectory\" --nologo | ConvertFrom-Json
    Assert-True ($LASTEXITCODE -eq 0) 'BUILD-01 actual MSBuild profile evaluation succeeds without compiling or publishing'
    Assert-True ($properties.Properties.PublishDir -eq "$candidateDirectory\" -and $properties.Properties.PublishUrl -eq "$candidateDirectory\") 'BUILD-01 MSBuild global paths override the real Portable profile dist defaults'
}
if ($Case -in @('All', 'BUILD-05')) {
    $single = @(Invoke-RunnerFixture 1)
    $result = $single[-1]
    $result.Output | Write-Output
    Assert-True (-not ($result.Output -match '^Smoke passed; no additional slot tray keys')) 'BUILD-05 a single run does not claim repeated no-growth verification'
    Assert-True ($result.Summary.TrayGrowthVerification.Status -eq 'NotEvaluated' -and $result.Summary.TrayGrowthVerification.Comparisons -eq 0) 'BUILD-05 one-run JSON explicitly reports no-growth not evaluated'
    $repeated = @(Invoke-RunnerFixture 2)
    $result = $repeated[-1]
    Assert-True ($result.Summary.TrayGrowthVerification.Status -eq 'Passed' -and $result.Summary.TrayGrowthVerification.Comparisons -eq 1) 'BUILD-05 two-run JSON reports one actual no-growth comparison'
    Assert-True ([bool]($result.Output -match '^Smoke passed; no additional slot tray keys')) 'BUILD-05 two successful runs retain the verified no-growth conclusion'
}
Write-Output "PASS Windows build-script regressions: $script:checks"
Write-Output "Synthetic fixture evidence: $fixture"
