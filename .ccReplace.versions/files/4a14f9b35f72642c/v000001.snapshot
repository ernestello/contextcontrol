# CC-DESC: Shared markdown output buffering and durable write helpers for Context Control exports.

$script:OutputLines = New-Object System.Collections.Generic.List[string]

function Add-Line {
    param([AllowNull()][string]$Text)

    if ($null -eq $Text) {
        $Text = ""
    }

    [void]$script:OutputLines.Add($Text)
}

function Get-OutputText {
    $text = [string]::Join([Environment]::NewLine, [string[]]$script:OutputLines.ToArray())

    if (-not $text.EndsWith([Environment]::NewLine)) {
        $text += [Environment]::NewLine
    }

    return $text
}

function Save-OutputFile {
    param([string]$Path)

    $fullPath = Resolve-CcSharedOutputPath $Path $script:CcSharedSettings
    $dir = [System.IO.Path]::GetDirectoryName($fullPath)

    if ([string]::IsNullOrWhiteSpace($dir)) {
        $dir = Resolve-CcSharedOutputRoot $script:CcSharedSettings
    }

    if (-not (Test-Path -LiteralPath $dir)) {
        New-Item -ItemType Directory -Path $dir | Out-Null
    }

    $leaf = [System.IO.Path]::GetFileName($fullPath)
    $tmpPath = Join-Path $dir (".$leaf.$PID.$([System.Guid]::NewGuid().ToString('N')).tmp")
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)

    [System.IO.File]::WriteAllText($tmpPath, (Get-OutputText), $utf8NoBom)

    $lastError = $null

    for ($attempt = 1; $attempt -le 30; $attempt++) {
        try {
            if ([System.IO.File]::Exists($fullPath)) {
                try {
                    [System.IO.File]::Replace($tmpPath, $fullPath, $null, $true)
                }
                catch {
                    Copy-Item -LiteralPath $tmpPath -Destination $fullPath -Force -ErrorAction Stop
                    Remove-Item -LiteralPath $tmpPath -Force -ErrorAction SilentlyContinue
                }
            }
            else {
                Move-Item -LiteralPath $tmpPath -Destination $fullPath -Force -ErrorAction Stop
            }

            if (-not [System.IO.File]::Exists($fullPath)) {
                throw "Export write reported success, but final file does not exist: $fullPath"
            }

            return $fullPath
        }
        catch {
            $lastError = $_.Exception.Message
            Start-Sleep -Milliseconds ([Math]::Min(1000, 50 * $attempt))
        }
    }

    throw "Failed to write '$Path' after retries. Close any editor/preview/indexer using it and try again. Temp output was left at: $tmpPath. Last error: $lastError"
}
