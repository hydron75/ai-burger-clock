#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$CandidatePath,
    [Parameter(Mandatory)][string]$PreviousIdentityPath,
    [Parameter(Mandatory)][string]$ReceiptPath
)

# Preparation only: never opens dist, a user DB, registry, or an app process.
# PreviousIdentityPath is the last deployment's candidate.json or receipt.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'WindowsDeployment.ps1')
$repo = Get-WDFullPath (Split-Path -Parent $PSScriptRoot)
$output = Assert-WDChildPath $ReceiptPath (Join-Path $repo 'artifacts')
$candidate = Assert-WDChildPath $CandidatePath (Split-Path -Parent $output)
$oldPath = Assert-WDChildPath $PreviousIdentityPath (Join-Path $repo 'artifacts')
if (Test-Path -LiteralPath $output) { Stop-WD 'ReceiptAlreadyExists' }
$oldReceipt = [IO.File]::ReadAllText((Get-WDActualPath $oldPath)) | ConvertFrom-Json
$old = if ($oldReceipt.PSObject.Properties.Name -contains 'Candidate') { $oldReceipt.Candidate } else { $oldReceipt }
Assert-WDIdentityShape $old
$version = Get-WDExeVersion $candidate
if ($version.ProductVersion -notmatch '\+([0-9a-f]{40})$') { Stop-WD 'SourceCommitMissing' }
$new = [ordered]@{
    Path = $candidate; FileVersion = $version.FileVersion; ProductVersion = $version.ProductVersion
    SourceCommit = $Matches[1]; SHA256 = Get-WDHash $candidate; Bytes = (Get-Item -LiteralPath $candidate).Length
}
Assert-WDIdentityShape ([pscustomobject]$new)
if ([IO.Path]::GetFileName($candidate) -ne 'AI Burger Clock.exe' -or
    [version]$new.FileVersion -le [version]$old.FileVersion -or $new.SHA256 -eq $old.SHA256) { Stop-WD 'InvalidCandidate' }
$receipt = [ordered]@{
    Schema = 'aiburgerclock.windows-deployment'; SchemaVersion = 1; Repository = $repo
    Candidate = $new
    Previous = [ordered]@{ FileVersion = $old.FileVersion; ProductVersion = $old.ProductVersion; SourceCommit = $old.SourceCommit; SHA256 = $old.SHA256 }
}
# CreateNew avoids overwriting another preparation receipt. File contents contain no secrets.
$stream = [IO.File]::Open($output, 'CreateNew', 'Write', 'None')
try {
    $bytes = [Text.Encoding]::UTF8.GetBytes(($receipt | ConvertTo-Json -Depth 5))
    $stream.Write($bytes, 0, $bytes.Length)
    $stream.Flush($true)
} finally { $stream.Dispose() }
[pscustomobject]@{ ReceiptPath = $output; FileVersion = $new.FileVersion; SHA256 = $new.SHA256; AppStarted = $false }
