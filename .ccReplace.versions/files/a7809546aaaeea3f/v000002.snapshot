# CC-DESC: Compatibility shim for the canonical shared Context Control bootstrap.

if ([string]::IsNullOrWhiteSpace($script:CcToolRoot)) {
    $candidateRoot = if ($PSScriptRoot -ne "") { Split-Path -Parent $PSScriptRoot } else { (Get-Location).Path }
    $script:CcToolRoot = $candidateRoot
}

$script:CcLibRoot = Join-Path $script:CcToolRoot "lib"
. (Join-Path $script:CcLibRoot "shared/Cc.Bootstrap.ps1")
