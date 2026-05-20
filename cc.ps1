# CC-DESC: Thin launcher for the modular Context Control source/function exporter.

[CmdletBinding()]
param(
    [string]$OutputFile = "cc_code_export.md",
    [int]$MaxFileKB = 512,
    [switch]$ForceLargeFiles,
    [switch]$NoClipboard,
    [switch]$HashHints
)

$ErrorActionPreference = "Stop"

$script:CcToolRoot = if ($PSScriptRoot -ne "") { $PSScriptRoot } else { (Get-Location).Path }
$script:CcLibRoot = Join-Path $script:CcToolRoot "lib"

. (Join-Path $script:CcLibRoot "Cc.Bootstrap.ps1")
. (Join-Path $script:CcLibRoot "Cc.Export.Source.ps1")

Invoke-CcSourceExport `
    -OutputFile $OutputFile `
    -MaxFileKB $MaxFileKB `
    -ForceLargeFiles:$ForceLargeFiles `
    -NoClipboard:$NoClipboard `
    -HashHints:$HashHints
