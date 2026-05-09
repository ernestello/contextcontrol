# CC-DESC: Finds PowerShell/GDScript/C-style function and marker ranges for CC-REPLACE.

function Get-IndentWidth {
    param([string]$Line)

    $width = 0

    for ($i = 0; $i -lt $Line.Length; $i++) {
        $ch = $Line[$i]

        if ($ch -eq ' ') {
            $width += 1
        }
        elseif ($ch -eq "`t") {
            $width += 4
        }
        else {
            break
        }
    }

    return $width
}

function Find-GdFunctionRange {
    param($Lines, [string]$FunctionName)

    $safeLines = @(ConvertTo-LineArray $Lines)
    $escaped = [regex]::Escape($FunctionName)
    $pattern = '^\s*(static\s+)?func\s+' + $escaped + '\s*\('

    $start = -1

    for ($i = 0; $i -lt $safeLines.Count; $i++) {
        if ($safeLines[$i] -match $pattern) {
            $start = $i
            break
        }
    }

    if ($start -lt 0) {
        throw "Function not found: $FunctionName"
    }

    $startIndent = Get-IndentWidth $safeLines[$start]
    $end = $safeLines.Count

    for ($j = $start + 1; $j -lt $safeLines.Count; $j++) {
        if ($safeLines[$j].Trim() -eq "") {
            continue
        }

        $indent = Get-IndentWidth $safeLines[$j]

        if ($indent -le $startIndent) {
            $end = $j
            break
        }
    }

    return [pscustomobject]@{
        Start = $start
        End = $end
    }
}

function Strip-CodeLineForBraces {
    param(
        [string]$Line,
        [ref]$InBlockComment
    )

    $result = New-Object System.Text.StringBuilder
    $inString = $false
    $inChar = $false
    $escape = $false
    $i = 0

    while ($i -lt $Line.Length) {
        $ch = $Line[$i]
        $next = if ($i + 1 -lt $Line.Length) { $Line[$i + 1] } else { [char]0 }

        if ($InBlockComment.Value) {
            if ($ch -eq '*' -and $next -eq '/') {
                $InBlockComment.Value = $false
                [void]$result.Append('  ')
                $i += 2
                continue
            }

            [void]$result.Append(' ')
            $i++
            continue
        }

        if (-not $inString -and -not $inChar) {
            if ($ch -eq '/' -and $next -eq '/') {
                break
            }

            if ($ch -eq '/' -and $next -eq '*') {
                $InBlockComment.Value = $true
                [void]$result.Append('  ')
                $i += 2
                continue
            }
        }

        if ($escape) {
            [void]$result.Append(' ')
            $escape = $false
            $i++
            continue
        }

        if (($inString -or $inChar) -and $ch -eq '\') {
            [void]$result.Append(' ')
            $escape = $true
            $i++
            continue
        }

        if (-not $inChar -and $ch -eq '"') {
            $inString = -not $inString
            [void]$result.Append(' ')
            $i++
            continue
        }

        if (-not $inString -and $ch -eq "'") {
            $inChar = -not $inChar
            [void]$result.Append(' ')
            $i++
            continue
        }

        if ($inString -or $inChar) {
            [void]$result.Append(' ')
        }
        else {
            [void]$result.Append($ch)
        }

        $i++
    }

    return $result.ToString()
}

function Test-LooksLikeFunctionSignaturePrefix {
    param([string]$Prefix)

    $p = $Prefix.Trim()

    if ($p -eq "") {
        return $true
    }

    # Reject obvious call/expression contexts.
    if ($p -match '(=|,|\[|\]|\.)') {
        return $false
    }

    if ($p -match '\b(return|if|while|for|switch|case|sizeof|new|delete|catch)\b') {
        return $false
    }

    return $true
}

function Get-RawOccurrenceHint {
    param($Lines, [string]$Needle)

    $safeLines = @(ConvertTo-LineArray $Lines)
    $escaped = [regex]::Escape($Needle)
    $hits = New-Object System.Collections.Generic.List[string]

    for ($i = 0; $i -lt $safeLines.Count; $i++) {
        if ($safeLines[$i] -match $escaped) {
            $lineNo = $i + 1
            $text = $safeLines[$i].Trim()

            if ($text.Length -gt 220) {
                $text = $text.Substring(0, 220) + " ..."
            }

            $hits.Add("  line ${lineNo}: $text")

            if ($hits.Count -ge 12) {
                break
            }
        }
    }

    if ($hits.Count -eq 0) {
        return ""
    }

    return "Raw occurrences of '$Needle' found:`n" + ($hits -join "`n")
}

function Find-BraceFunctionRange {
    param($Lines, [string]$FunctionName)

    $safeLines = @(ConvertTo-LineArray $Lines)
    $escaped = [regex]::Escape($FunctionName)

    $psPattern = '^\s*function\s+' + $escaped + '\b'
    $cPattern = '(?<![A-Za-z0-9_~])(?:[A-Za-z_][A-Za-z0-9_]*::)*' + $escaped + '\s*\('

    $searchBlockComment = $false

    for ($i = 0; $i -lt $safeLines.Count; $i++) {
        $rawLine = [string]$safeLines[$i]
        $trimmed = $rawLine.Trim()

        if ($trimmed -eq "") {
            continue
        }

        if (($trimmed.StartsWith("//") -or $trimmed.StartsWith("#")) -and
            ($trimmed -notmatch $escaped)) {
            continue
        }

        $cleanLine = Strip-CodeLineForBraces $rawLine ([ref]$searchBlockComment)

        $isPowerShellFunction = $cleanLine -match $psPattern
        $cMatch = [regex]::Match($cleanLine, $cPattern)
        $isCStyleFunction = $cMatch.Success

        if (-not $isPowerShellFunction -and -not $isCStyleFunction) {
            continue
        }

        if ($isCStyleFunction -and -not $isPowerShellFunction) {
            $prefix = $cleanLine.Substring(0, $cMatch.Index)

            if (-not (Test-LooksLikeFunctionSignaturePrefix $prefix)) {
                continue
            }
        }

        $start = $i

        while ($start -gt 0) {
            $prev = $safeLines[$start - 1].Trim()

            if ($prev -eq "") {
                break
            }

            if ($prev.StartsWith("template") -or
                $prev.StartsWith("[[") -or
                $prev.StartsWith("__attribute__") -or
                $prev.StartsWith("VKAPI_ATTR") -or
                $prev.StartsWith("static_assert")) {
                $start--
                continue
            }

            break
        }

        $openLine = -1
        $sawSemicolonBeforeBrace = $false
        $scanBlockComment = $false

        for ($j = $i; $j -lt $safeLines.Count; $j++) {
            $clean = Strip-CodeLineForBraces ([string]$safeLines[$j]) ([ref]$scanBlockComment)

            $braceIndex = $clean.IndexOf("{")
            $semiIndex = $clean.IndexOf(";")

            if ($semiIndex -ge 0 -and ($braceIndex -lt 0 -or $semiIndex -lt $braceIndex)) {
                $sawSemicolonBeforeBrace = $true
                break
            }

            if ($braceIndex -ge 0) {
                $openLine = $j
                break
            }
        }

        if ($openLine -lt 0 -or $sawSemicolonBeforeBrace) {
            continue
        }

        $depth = 0
        $started = $false
        $bodyBlockComment = $false

        for ($j = $openLine; $j -lt $safeLines.Count; $j++) {
            $clean = Strip-CodeLineForBraces ([string]$safeLines[$j]) ([ref]$bodyBlockComment)

            for ($k = 0; $k -lt $clean.Length; $k++) {
                if ($clean[$k] -eq '{') {
                    $depth++
                    $started = $true
                }
                elseif ($clean[$k] -eq '}') {
                    $depth--
                }

                if ($started -and $depth -eq 0) {
                    return [pscustomobject]@{
                        Start = $start
                        End = $j + 1
                    }
                }
            }
        }

        throw "Function body did not close: $FunctionName"
    }

    $hint = Get-RawOccurrenceHint $safeLines $FunctionName
    if ($hint -ne "") {
        throw "Function not found: $FunctionName`n$hint"
    }

    throw "Function not found: $FunctionName"
}

function Find-FunctionRange {
    param([string]$Path, $Lines, [string]$FunctionName)

    $safeLines = @(ConvertTo-LineArray $Lines)
    $ext = [System.IO.Path]::GetExtension($Path).ToLowerInvariant()

    if ($ext -eq ".gd") {
        return Find-GdFunctionRange $safeLines $FunctionName
    }

    return Find-BraceFunctionRange $safeLines $FunctionName
}

function Find-MarkerRange {
    param($Lines, [string]$MarkerName)

    $safeLines = @(ConvertTo-LineArray $Lines)
    $escaped = [regex]::Escape($MarkerName)

    $beginPattern = '^\s*(#|//|/\*)?\s*CC-REPLACE-BEGIN\s*:\s*' + $escaped + '\b.*$'
    $endPattern   = '^\s*(#|//|/\*)?\s*CC-REPLACE-END\s*:\s*'   + $escaped + '\b.*$'

    $begin = -1
    $end = -1

    for ($i = 0; $i -lt $safeLines.Count; $i++) {
        if ($begin -lt 0 -and $safeLines[$i] -match $beginPattern) {
            $begin = $i
            continue
        }

        if ($begin -ge 0 -and $safeLines[$i] -match $endPattern) {
            $end = $i
            break
        }
    }

    if ($begin -lt 0) {
        throw "Marker begin not found: CC-REPLACE-BEGIN: $MarkerName"
    }

    if ($end -lt 0) {
        throw "Marker end not found after begin line $($begin + 1): CC-REPLACE-END: $MarkerName"
    }

    return [pscustomobject]@{
        Start = $begin + 1
        End = $end
    }
}
