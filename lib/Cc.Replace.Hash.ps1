# CC-DESC: Provides CC-REPLACE hash validation and duplicate-detection helpers.

function Get-Sha1Short {
    param([string]$Text)

    if ($null -eq $Text) {
        $Text = ""
    }

    $sha = [System.Security.Cryptography.SHA1]::Create()
    try {
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($Text)
        $hashBytes = $sha.ComputeHash($bytes)
        $hex = -join ($hashBytes | ForEach-Object { $_.ToString("x2") })
        return $hex.Substring(0, 8)
    }
    finally {
        $sha.Dispose()
    }
}

function Get-RegionHashFromLines {
    param(
        $Lines,
        [int]$Start,
        [int]$End
    )

    $safeLines = @(ConvertTo-LineArray $Lines)

    if ($Start -lt 0 -or $End -lt $Start -or $End -gt $safeLines.Count) {
        throw "Invalid hash range: Start=$Start End=$End Lines=$($safeLines.Count)"
    }

    if ($Start -eq $End) {
        return Get-Sha1Short ""
    }

    return Get-Sha1Short (($safeLines[$Start..($End - 1)]) -join "`n")
}

function Assert-OptionalHashMatches {
    param(
        $Headers,
        $Lines,
        [int]$Start,
        [int]$End,
        [string]$TargetDescription
    )

    $expected = Get-HeaderValue $Headers "HASH"
    if ($expected -eq "") {
        return
    }

    if ($expected -notmatch '^[0-9A-Fa-f]{8}$') {
        throw "Invalid HASH header for ${TargetDescription}: '$expected'. Expected 8 hex characters."
    }

    $actual = Get-RegionHashFromLines $Lines $Start $End

    if ($actual.ToLowerInvariant() -ne $expected.ToLowerInvariant()) {
        throw "HASH mismatch for ${TargetDescription}. Expected $expected, actual $actual. The target region changed since export; re-run cc.ps1 and regenerate the patch."
    }
}

function Get-TrimmedCodeLineArray {
    param([string]$Code)

    $trimmed = Trim-CodeBlankEdges $Code

    if ($trimmed -eq "") {
        return [string[]]@()
    }

    return [string[]]@(ConvertTo-LineArray $trimmed)
}

function Test-LineArraysEqual {
    param($A, $B)

    $aa = @(ConvertTo-LineArray $A)
    $bb = @(ConvertTo-LineArray $B)

    if ($aa.Count -ne $bb.Count) {
        return $false
    }

    for ($i = 0; $i -lt $aa.Count; $i++) {
        if ($aa[$i] -ne $bb[$i]) {
            return $false
        }
    }

    return $true
}

function Test-CodeRegionMatches {
    param(
        $Lines,
        [int]$Start,
        [int]$End,
        [string]$Code
    )

    $safeLines = @(ConvertTo-LineArray $Lines)
    $codeLines = @(Get-TrimmedCodeLineArray $Code)

    if ($Start -lt 0 -or $End -lt $Start -or $End -gt $safeLines.Count) {
        throw "Invalid duplicate-check range: Start=$Start End=$End Lines=$($safeLines.Count)"
    }

    $existingCount = $End - $Start

    if ($existingCount -ne $codeLines.Count) {
        return $false
    }

    for ($i = 0; $i -lt $existingCount; $i++) {
        if ($safeLines[$Start + $i] -ne $codeLines[$i]) {
            return $false
        }
    }

    return $true
}

function Test-CodeBlockAlreadyInLines {
    param(
        $Lines,
        [string]$Code
    )

    $safeLines = @(ConvertTo-LineArray $Lines)
    $codeLines = @(Get-TrimmedCodeLineArray $Code)

    if ($codeLines.Count -eq 0) {
        return $false
    }

    if ($codeLines.Count -gt $safeLines.Count) {
        return $false
    }

    $lastStart = $safeLines.Count - $codeLines.Count

    for ($start = 0; $start -le $lastStart; $start++) {
        $matches = $true

        for ($j = 0; $j -lt $codeLines.Count; $j++) {
            if ($safeLines[$start + $j] -ne $codeLines[$j]) {
                $matches = $false
                break
            }
        }

        if ($matches) {
            return $true
        }
    }

    return $false
}
