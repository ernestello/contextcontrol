# CC-DESC: Loads the shared Context Control PowerShell foundation modules from the canonical shared folder.

if ([string]::IsNullOrWhiteSpace($script:CcToolRoot)) {
    $candidateRoot = if ($PSScriptRoot -ne "") { $PSScriptRoot } else { (Get-Location).Path }
    if ((Split-Path -Leaf $candidateRoot) -ieq "shared") {
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
$script:CcSharedLibRoot = Join-Path $script:CcLibRoot "shared"

. (Join-Path $script:CcSharedLibRoot "Cc.Text.ps1")
. (Join-Path $script:CcSharedLibRoot "Cc.Settings.ps1")
. (Join-Path $script:CcSharedLibRoot "Cc.Output.ps1")
. (Join-Path $script:CcSharedLibRoot "Cc.Clipboard.ps1")
