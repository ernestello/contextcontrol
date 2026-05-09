# CC-DESC: Resolves CC-REPLACE targets and performs line-level file mutations.

function Resolve-TargetPath {
    param([string]$PathText)

    if ($PathText -eq "") {
        throw "Missing FILE/DIR/PATH header."
    }

    $clean = $PathText.Trim()
    $clean = $clean -replace '/', [System.IO.Path]::DirectorySeparatorChar

    if ([System.IO.Path]::IsPathRooted($clean)) {
        return $clean
    }

    return Join-Path (Get-CcProjectRoot) $clean
}

function Get-HeaderValue {
    param($Headers, [string]$Name)

    $key = $Name.ToUpperInvariant()

    if ($Headers.ContainsKey($key)) {
        return $Headers[$key]
    }

    return ""
}

function Get-TargetHeader {
    param($Headers)

    $file = Get-HeaderValue $Headers "FILE"
    if ($file -ne "") { return $file }

    $dir = Get-HeaderValue $Headers "DIR"
    if ($dir -ne "") { return $dir }

    $path = Get-HeaderValue $Headers "PATH"
    if ($path -ne "") { return $path }

    return ""
}

function Read-TargetLines {
    param([string]$Path)

    if ($null -eq $script:CcReplaceReadCache) {
        $script:CcReplaceReadCache = @{}
    }

    # Fast in-process cache. ccReplace is a single-run mechanical applier:
    # files are only changed by Write-TargetLines, which updates this cache.
    # Avoid Get-Item timestamp checks on every function lookup; those were
    # becoming visible when many blocks targeted the same file.
    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $cacheKey = $fullPath.ToLowerInvariant()

    if ($script:CcReplaceReadCache.ContainsKey($cacheKey)) {
        $cached = $script:CcReplaceReadCache[$cacheKey]
        if ($null -ne $cached) {
            return [pscustomobject]@{
                Lines = [string[]]$cached.Lines
                Newline = [string]$cached.Newline
            }
        }
    }

    $raw = Read-TextFileAutoEncoding $fullPath

    $newline = "`n"
    if ($raw.Contains("`r`n")) {
        $newline = "`r`n"
    }
    elseif ($raw.Contains("`r")) {
        $newline = "`r"
    }

    $lines = @(ConvertTo-LineArray $raw)

    if ($lines.Count -gt 0 -and $lines[$lines.Count - 1] -eq "") {
        if ($lines.Count -eq 1) {
            $lines = @()
        }
        else {
            $lines = $lines[0..($lines.Count - 2)]
        }
    }

    $result = [pscustomobject]@{
        Lines = [string[]]$lines
        Newline = $newline
    }

    $script:CcReplaceReadCache[$cacheKey] = $result
    return $result
}

function Write-TargetLines {
    param(
        [string]$Path,
        $Lines,
        [string]$Newline
    )

    if ($null -eq $Lines) {
        $safeLines = [string[]]@()
    }
    elseif ($Lines -is [System.Array]) {
        $safeLines = [string[]]@($Lines)
    }
    else {
        $safeLines = [string[]]@(ConvertTo-LineArray $Lines)
    }

    $out = ($safeLines -join $Newline) + $Newline
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    $fullPath = [System.IO.Path]::GetFullPath($Path)

    [System.IO.File]::WriteAllText($fullPath, $out, $utf8NoBom)

    if ($null -eq $script:CcReplaceReadCache) {
        $script:CcReplaceReadCache = @{}
    }

    $cacheKey = $fullPath.ToLowerInvariant()
    $script:CcReplaceReadCache[$cacheKey] = [pscustomobject]@{
        Lines = [string[]]$safeLines
        Newline = $Newline
    }
}

function Replace-LineRange {
    param($Lines, [int]$Start, [int]$End, [string]$ReplacementCode)

    $safeLines = @(ConvertTo-LineArray $Lines)
    $trimmed = Trim-CodeBlankEdges $ReplacementCode
    $replacement = @()

    if ($trimmed -ne "") {
        $replacement = @(ConvertTo-LineArray $trimmed)
    }

    if ($Start -lt 0 -or $End -lt $Start -or $End -gt $safeLines.Count) {
        throw "Invalid replace range: Start=$Start End=$End Lines=$($safeLines.Count)"
    }

    $newLines = New-Object System.Collections.Generic.List[string]

    for ($i = 0; $i -lt $Start; $i++) {
        $newLines.Add($safeLines[$i])
    }

    foreach ($line in $replacement) {
        $newLines.Add($line)
    }

    for ($i = $End; $i -lt $safeLines.Count; $i++) {
        $newLines.Add($safeLines[$i])
    }

    return [string[]]$newLines.ToArray()
}

function Insert-LinesAt {
    param($Lines, [int]$Index, [string]$Code)

    $safeLines = @(ConvertTo-LineArray $Lines)

    if ($Index -lt 0 -or $Index -gt $safeLines.Count) {
        throw "Invalid insert index: Index=$Index Lines=$($safeLines.Count)"
    }

    $trimmed = Trim-CodeBlankEdges $Code
    $insert = @()

    if ($trimmed -ne "") {
        $insert = @(ConvertTo-LineArray $trimmed)
    }

    $newLines = New-Object System.Collections.Generic.List[string]

    for ($i = 0; $i -lt $Index; $i++) {
        $newLines.Add($safeLines[$i])
    }

    if ($insert.Count -gt 0) {
        if ($Index -gt 0 -and $newLines.Count -gt 0 -and $newLines[$newLines.Count - 1].Trim() -ne "") {
            $newLines.Add("")
        }

        foreach ($line in $insert) {
            $newLines.Add($line)
        }

        if ($Index -lt $safeLines.Count -and $insert[$insert.Count - 1].Trim() -ne "") {
            $newLines.Add("")
        }
    }

    if ($Index -lt $safeLines.Count) {
        for ($i = $Index; $i -lt $safeLines.Count; $i++) {
            $newLines.Add($safeLines[$i])
        }
    }

    return [string[]]$newLines.ToArray()
}

function Backup-TargetOnce {
    param([string]$Path, $BackedUpMap)

    if ($NoBackup) {
        return
    }

    if (-not (Test-Path -LiteralPath $Path)) {
        return
    }

    if ($BackedUpMap.ContainsKey($Path)) {
        return
    }


}

function Ensure-ParentDirectory {
    param([string]$Path)

    $parent = Split-Path -Parent $Path

    if ($parent -eq "" -or $null -eq $parent) {
        return
    }

    if (-not (Test-Path -LiteralPath $parent)) {
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
        Write-Host "Create parent directory: $parent"
    }
}

function Get-IncludeHeaderValue {
    param($Headers)

    $header = Get-HeaderValue $Headers "HEADER"
    if ($header -eq "") {
        $header = Get-HeaderValue $Headers "INCLUDE"
    }

    return $header.Trim()
}

function Insert-IncludeLine {
    param(
        $Lines,
        [string]$HeaderText
    )

    $safeLines = @(ConvertTo-LineArray $Lines)
    $header = $HeaderText.Trim()

    if ($header -eq "") {
        throw 'MODE:insert_include requires HEADER: <...> or HEADER: "...".'
    }

    if ($header -notmatch '^(<[^>]+>|"[^"]+")$') {
        throw ('Invalid HEADER for insert_include: {0}. Use <system> or "local" include style.' -f $header)
    }

    $includeLine = "#include $header"
    $escaped = [regex]::Escape($header)

    foreach ($line in $safeLines) {
        if ($line -match ('^\s*#\s*include\s+' + $escaped + '\s*(//.*)?$')) {
            return [pscustomobject]@{
                Lines = [string[]]$safeLines
                Changed = $false
            }
        }
    }

    $insertAt = 0
    for ($i = 0; $i -lt $safeLines.Count; $i++) {
        if ($safeLines[$i] -match '^\s*#\s*include\b') {
            $insertAt = $i + 1
        }
    }

    $newLines = New-Object System.Collections.Generic.List[string]

    for ($i = 0; $i -lt $insertAt; $i++) {
        $newLines.Add($safeLines[$i])
    }

    $newLines.Add($includeLine)

    for ($i = $insertAt; $i -lt $safeLines.Count; $i++) {
        $newLines.Add($safeLines[$i])
    }

    return [pscustomobject]@{
        Lines = [string[]]$newLines.ToArray()
        Changed = $true
    }
}
