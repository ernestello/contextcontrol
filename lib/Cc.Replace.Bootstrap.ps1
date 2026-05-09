# CC-DESC: Compatibility shim for the canonical CC-REPLACE bootstrap.

if ([string]::IsNullOrWhiteSpace($script:CcToolRoot)) {
    $candidateRoot = if ($PSScriptRoot -ne "") { Split-Path -Parent $PSScriptRoot } else { (Get-Location).Path }
    $script:CcToolRoot = $candidateRoot
}

$script:CcLibRoot = Join-Path $script:CcToolRoot "lib"
. (Join-Path $script:CcLibRoot "replace/Cc.Replace.Bootstrap.ps1")
