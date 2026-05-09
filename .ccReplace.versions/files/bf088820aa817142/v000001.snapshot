# CC-DESC: Loads shared and CC-REPLACE modules from the canonical replace folder.

if ([string]::IsNullOrWhiteSpace($script:CcToolRoot)) {
    $candidateRoot = if ($PSScriptRoot -ne "") { $PSScriptRoot } else { (Get-Location).Path }
    if ((Split-Path -Leaf $candidateRoot) -ieq "replace") {
        $script:CcToolRoot = Split-Path -Parent (Split-Path -Parent $candidateRoot)
    }
    elseif ((Split-Path -Leaf $candidateRoot) -ieq "lib") {
        $script:CcToolRoot = Split-Path -Parent $candidateRoot
    }
    else {
        $script:CcToolRoot = $candidateRoot
    }
}

$script:CcLibRoot = Join-Path $script:CcToolRoot "lib"
$script:CcReplaceLibRoot = Join-Path $script:CcLibRoot "replace"

. (Join-Path $script:CcLibRoot "shared/Cc.Bootstrap.ps1")
. (Join-Path $script:CcReplaceLibRoot "Cc.Replace.Settings.ps1")
. (Join-Path $script:CcReplaceLibRoot "Cc.Replace.Parse.ps1")
. (Join-Path $script:CcReplaceLibRoot "Cc.Replace.Target.ps1")
. (Join-Path $script:CcReplaceLibRoot "Cc.Replace.Ranges.ps1")
. (Join-Path $script:CcReplaceLibRoot "Cc.Replace.Hash.ps1")
. (Join-Path $script:CcReplaceLibRoot "Cc.Replace.Plan.ps1")
. (Join-Path $script:CcReplaceLibRoot "Cc.Replace.Versioning.ps1")
. (Join-Path $script:CcReplaceLibRoot "Cc.Replace.Ui.ps1")
. (Join-Path $script:CcReplaceLibRoot "Cc.Replace.Apply.ps1")
. (Join-Path $script:CcReplaceLibRoot "Cc.Replace.Pipeline.ps1")
. (Join-Path $script:CcReplaceLibRoot "Cc.Replace.Agent.ps1")
. (Join-Path $script:CcReplaceLibRoot "Cc.Replace.Menu.ps1")
