# CC-DESC: Shared text, line, and encoding helpers for Context Control scripts.

function ConvertTo-LineArray {
    param($Value)

    if ($null -eq $Value) {
        return [string[]]@()
    }

    $out = New-Object System.Collections.Generic.List[string]

    foreach ($item in @($Value)) {
        if ($null -eq $item) {
            continue
        }

        $text = [string]$item
        $text = $text -replace "`0", ""

        $text = $text.Replace([char]0x0085, [char]10)
        $text = $text.Replace([char]0x2028, [char]10)
        $text = $text.Replace([char]0x2029, [char]10)
        $text = $text.Replace("`r`n", "`n")
        $text = $text.Replace("`r", "`n")

        $parts = $text.Split([char]10)

        foreach ($part in $parts) {
            $out.Add($part)
        }
    }

    return [string[]]$out.ToArray()
}

function Read-TextFileAutoEncoding {
    param([string]$Path)

    $resolved = (Resolve-Path -LiteralPath $Path).Path
    $bytes = [System.IO.File]::ReadAllBytes($resolved)

    if ($bytes.Length -eq 0) {
        return ""
    }

    $encoding = $null

    if ($bytes.Length -ge 3 -and
        $bytes[0] -eq 0xEF -and
        $bytes[1] -eq 0xBB -and
        $bytes[2] -eq 0xBF) {
        $encoding = [System.Text.Encoding]::UTF8
    }
    elseif ($bytes.Length -ge 2 -and
            $bytes[0] -eq 0xFF -and
            $bytes[1] -eq 0xFE) {
        $encoding = [System.Text.Encoding]::Unicode
    }
    elseif ($bytes.Length -ge 2 -and
            $bytes[0] -eq 0xFE -and
            $bytes[1] -eq 0xFF) {
        $encoding = [System.Text.Encoding]::BigEndianUnicode
    }
    else {
        $sampleCount = [Math]::Min($bytes.Length, 400)
        $evenZero = 0
        $oddZero = 0

        for ($i = 0; $i -lt $sampleCount; $i++) {
            if ($bytes[$i] -eq 0) {
                if (($i % 2) -eq 0) {
                    $evenZero++
                }
                else {
                    $oddZero++
                }
            }
        }

        if ($oddZero -gt 10 -and $oddZero -gt ($evenZero * 2)) {
            $encoding = [System.Text.Encoding]::Unicode
        }
        elseif ($evenZero -gt 10 -and $evenZero -gt ($oddZero * 2)) {
            $encoding = [System.Text.Encoding]::BigEndianUnicode
        }
        else {
            $encoding = New-Object System.Text.UTF8Encoding($false, $false)
        }
    }

    $text = $encoding.GetString($bytes)
    $text = $text -replace "`0", ""
    return $text
}

function Write-TextFileUtf8NoBom {
    param(
        [string]$Path,
        [string]$Text
    )

    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($Path, $Text, $utf8NoBom)
}
