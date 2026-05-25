# CC-DESC: Thin launcher for the modular CC-REPLACE patch applier.

[CmdletBinding()]
param(
    [string]$InputFile = "",
    [switch]$DryRun,
    [switch]$NoBackup,
    [switch]$Help,
    [switch]$AgentMode,
    [switch]$PlanOnly,
    [switch]$Json,
    [string]$Apply = ""
)

$ErrorActionPreference = "Stop"

$script:CcToolRoot = if ($PSScriptRoot -ne "") { $PSScriptRoot } else { (Get-Location).Path }
$script:CcLibRoot = Join-Path $script:CcToolRoot "lib"

. (Join-Path $script:CcLibRoot "Cc.Replace.Bootstrap.ps1")

if ($Help) {
    Show-CcReplaceHelp
    exit 0
}

if ($AgentMode) {
    Invoke-CcReplaceAgentMode
    exit 0
}

if ($InputFile -eq "") {
    Show-CcReplaceMainMenu
    exit 0
}

$resolvedInputFile = Resolve-TargetPath $InputFile

if ($PlanOnly) {
    Invoke-CcReplacePlanFile $resolvedInputFile -Json:$Json
    exit 0
}

Invoke-CcReplaceFile $resolvedInputFile -ApplyDecision $Apply
