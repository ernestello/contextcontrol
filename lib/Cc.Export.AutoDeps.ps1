# CC-DESC: Auto dependency discovery for Context Control source exports.

function Add-UniquePathToList {
    param(
        [System.Collections.Generic.List[string]]$List,
        $Seen,
        [string]$Path,
        [string]$Reason
    )

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return
    }

    if (-not (Test-Path -LiteralPath $Path)) {
        return
    }

    if (Is-ExcludedPath $Path) {
        return
    }

    $key = Get-PathKey $Path
    if ($Seen.ContainsKey($key)) {
        return
    }

    $Seen[$key] = $true
    $List.Add((Get-RelativeDisplayPath $Path))

    if ($Reason -ne "") {
        Write-Host "Auto-added ${Reason}: $(Get-RelativeDisplayPath $Path)"
    }
}

function Find-MatchingHeadersForSource {
    param([string]$SourcePath)

    $result = New-Object System.Collections.Generic.List[string]

    if (-not (Test-Path -LiteralPath $SourcePath)) {
        return @()
    }

    $resolved = (Resolve-Path -LiteralPath $SourcePath).Path
    $rel = Get-RelativeDisplayPath $resolved
    $dir = Split-Path -Parent $rel
    $base = [System.IO.Path]::GetFileNameWithoutExtension($rel)
    $exts = @(".h", ".hpp", ".hh", ".hxx", ".inl")

    $candidateRelDirs = New-Object System.Collections.Generic.List[string]

    if ($dir -ne "") {
        $candidateRelDirs.Add($dir)

        if ($dir -match '^src/(.+)$') {
            $candidateRelDirs.Add("include/$($Matches[1])")
        }
        elseif ($dir -eq "src") {
            $candidateRelDirs.Add("include")
        }
    }

    $candidateRelDirs.Add("include")
    $candidateRelDirs.Add("src")

    $seen = @{}

    foreach ($candDir in $candidateRelDirs) {
        foreach ($e in $exts) {
            $candidate = Join-Path (Get-Location).Path (($candDir.TrimEnd('/') + "/" + $base + $e) -replace '/', [System.IO.Path]::DirectorySeparatorChar)
            if (Test-Path -LiteralPath $candidate) {
                $key = Get-PathKey $candidate
                if (-not $seen.ContainsKey($key)) {
                    $seen[$key] = $true
                    $result.Add($candidate)
                }
            }
        }
    }

    return @($result.ToArray())
}

function Resolve-ShaderIncludePath {
    param(
        [string]$ShaderPath,
        [string]$IncludeText
    )

    if ([string]::IsNullOrWhiteSpace($IncludeText)) {
        return ""
    }

    $includeClean = $IncludeText.Trim() -replace '\\', '/'
    $shaderResolved = (Resolve-Path -LiteralPath $ShaderPath).Path
    $shaderDir = Split-Path -Parent $shaderResolved
    $root = (Get-Location).Path

    $candidates = @(
        (Join-Path $shaderDir ($includeClean -replace '/', [System.IO.Path]::DirectorySeparatorChar)),
        (Join-Path $root ($includeClean -replace '/', [System.IO.Path]::DirectorySeparatorChar)),
        (Join-Path (Join-Path $root "shaders") ($includeClean -replace '/', [System.IO.Path]::DirectorySeparatorChar))
    )

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }

    if (Test-Path -LiteralPath "shaders") {
        $matches = Get-ChildItem -LiteralPath "shaders" -Recurse -File -ErrorAction SilentlyContinue |
            Where-Object { ((Get-RelativeDisplayPath $_.FullName) -replace '\\', '/').EndsWith($includeClean, [System.StringComparison]::OrdinalIgnoreCase) } |
            Select-Object -First 1

        if ($null -ne $matches) {
            return $matches.FullName
        }
    }

    return ""
}

function Find-ShaderIncludesForFile {
    param([string]$ShaderPath)

    $result = New-Object System.Collections.Generic.List[string]

    if (-not (Test-Path -LiteralPath $ShaderPath)) {
        return @()
    }

    $content = Read-TextFileAutoEncoding $ShaderPath
    $lines = @(ConvertTo-LineArray $content)
    $max = [Math]::Min(100, $lines.Count)
    $seen = @{}

    for ($i = 0; $i -lt $max; $i++) {
        $line = [string]$lines[$i]
        if ($line -match '^\s*#\s*include\s*[<"]([^>"]+)[>"]') {
            $inc = $Matches[1]
            $resolved = Resolve-ShaderIncludePath $ShaderPath $inc
            if ($resolved -ne "") {
                $key = Get-PathKey $resolved
                if (-not $seen.ContainsKey($key)) {
                    $seen[$key] = $true
                    $result.Add($resolved)
                }
            }
        }
    }

    return @($result.ToArray())
}

function Expand-InputPathsWithAutoDependencies {
    param($Paths)

    $expanded = New-Object System.Collections.Generic.List[string]
    $seen = @{}

    foreach ($path in @($Paths)) {
        $cleanPath = $path -replace '/', [System.IO.Path]::DirectorySeparatorChar

        if (Test-Path -LiteralPath $cleanPath) {
            Add-UniquePathToList $expanded $seen $cleanPath ""

            $item = Get-Item -LiteralPath $cleanPath
            if (-not $item.PSIsContainer) {
                $ext = [System.IO.Path]::GetExtension($item.Name).ToLowerInvariant()

                if (@(".cpp", ".cc", ".cxx", ".c").Contains($ext)) {
                    foreach ($header in @(Find-MatchingHeadersForSource $item.FullName)) {
                        Add-UniquePathToList $expanded $seen $header "matching header"
                    }
                }
                elseif (@(".frag", ".vert", ".comp", ".geom", ".tesc", ".tese", ".mesh", ".task", ".glsl").Contains($ext)) {
                    foreach ($includePath in @(Find-ShaderIncludesForFile $item.FullName)) {
                        Add-UniquePathToList $expanded $seen $includePath "shader include"
                    }
                }
            }
        }
        else {
            $key = ($path -replace '\\', '/').ToLowerInvariant()
            if (-not $seen.ContainsKey($key)) {
                $seen[$key] = $true
                $expanded.Add($path)
            }
        }
    }

    return @($expanded.ToArray())
}
