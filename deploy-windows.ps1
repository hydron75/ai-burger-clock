#requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory)][string]$ReceiptPath)

# Run only in the user's 64-bit PowerShell outside Codex, after tray Exit.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'tools/WindowsDeployment.ps1')
$result = Invoke-WindowsDeployment -Repository $PSScriptRoot -ReceiptPath $ReceiptPath
$result | ConvertTo-Json -Depth 8
if (-not $result.Succeeded) { exit 1 }
