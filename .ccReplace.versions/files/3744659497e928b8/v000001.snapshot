# CC-DESC: Owns CC-REPLACE DIR/CC/GO pipeline helper commands.

function Get-CcPipelineScriptPath {
    param([string]$ScriptName)

    $candidates = @()

    if (-not [string]::IsNullOrWhiteSpace($script:CcToolRoot)) {
        $candidates += (Join-Path $script:CcToolRoot $ScriptName)
    }

    if ($PSScriptRoot -ne "") {
        $root = $PSScriptRoot
        if ((Split-Path -Leaf $root) -ieq "lib") {
            $root = Split-Path -Parent $root
        }
        $candidates += (Join-Path $root $ScriptName)
    }

    $candidates += (Join-Path (Get-Location).Path $ScriptName)

    foreach ($candidate in @($candidates | Select-Object -Unique)) {
        if (Test-Path -LiteralPath $candidate) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }

    return ""
}

function Test-CcGoPatchLooksLikePatch {
    param([string]$Text)

    if ([string]::IsNullOrWhiteSpace($Text)) {
        return $false
    }

    try {
        $normalized = [regex]::Replace($Text, "\r\n|\r|\n|\x85|\u2028|\u2029", "`n")
        return (($normalized -match '(?m)^\s*BEGIN\s+CC-REPLACE\s*$') -and
                ($normalized -match '(?m)^\s*END\s+CC-REPLACE\s*$'))
    }
    catch {
        return $false
    }
}

function Read-CcGoClipboardText {
    try {
        $cmd = Get-Command Get-Clipboard -ErrorAction SilentlyContinue
        if ($null -eq $cmd) {
            return ""
        }

        try {
            $raw = Get-Clipboard -Raw -ErrorAction Stop
            if ($null -ne $raw) {
                return [string]$raw
            }
        }
        catch {
            $items = Get-Clipboard -ErrorAction Stop
            if ($null -ne $items) {
                return ($items -join [Environment]::NewLine)
            }
        }
    }
    catch {
        return ""
    }

    return ""
}

function Test-CcGoConsoleHasQueuedInput {
    try {
        return [Console]::KeyAvailable
    }
    catch {
        return $false
    }
}

function Read-CcGoManualPatchText {
    Write-Host ""
    Write-Host "Paste raw BEGIN CC-REPLACE blocks." -ForegroundColor Cyan
    Write-Host "GO accepts normal patch syntax. ENDCC is optional as an emergency/manual terminator." -ForegroundColor DarkGray
    Write-Host ""

    $lines = New-Object System.Collections.Generic.List[string]
    $sawPatchBlock = $false
    $openPatchBlocks = 0

    while ($true) {
        $line = [Console]::ReadLine()

        if ($null -eq $line) {
            break
        }

        if ($line -eq "ENDCC") {
            break
        }

        $lines.Add($line)

        $sentinel = Normalize-CcReplaceLine $line
        if ($sentinel -match '^BEGIN\s+CC-REPLACE$') {
            $sawPatchBlock = $true
            $openPatchBlocks++
        }
        elseif ($sentinel -match '^END\s+CC-REPLACE$') {
            if ($openPatchBlocks -gt 0) {
                $openPatchBlocks--
            }

            if ($sawPatchBlock -and $openPatchBlocks -eq 0) {
                # A whole paste normally arrives in the console input buffer at once.
                # Give the terminal a brief chance to expose remaining queued lines so
                # multi-block patches keep being consumed, then auto-finish without
                # requiring a separate ENDCC line.
                $hasMoreInput = $false
                for ($attempt = 0; $attempt -lt 15; $attempt++) {
                    Start-Sleep -Milliseconds 20
                    if (Test-CcGoConsoleHasQueuedInput) {
                        $hasMoreInput = $true
                        break
                    }
                }

                if (-not $hasMoreInput) {
                    break
                }
            }
        }
    }

    return ($lines -join [Environment]::NewLine)
}

function Read-CcGoPatchText {
    $clipboardText = Read-CcGoClipboardText
    if (Test-CcGoPatchLooksLikePatch $clipboardText) {
        Write-Host "Loaded CC-REPLACE blocks from clipboard. No Ctrl+V/ENDCC needed." -ForegroundColor Green
        return $clipboardText
    }

    Write-Host "Clipboard does not contain raw CC-REPLACE blocks; falling back to manual paste." -ForegroundColor DarkGray
    return Read-CcGoManualPatchText
}

function Invoke-CcPipelineCommand {
    param([string]$Command)

    $clean = ""
    if ($null -ne $Command) {
        $clean = $Command.Trim()
    }

    if ($clean -eq "") {
        return $false
    }

    $settings = Read-CcReplaceSettings
    $projectRoot = Resolve-CcProjectRoot $settings
    $outputRoot = Resolve-CcOutputRoot $settings

    if ($clean -ieq "DIR") {
        $scriptPath = Get-CcPipelineScriptPath "ccDir.ps1"
        if ($scriptPath -eq "") {
            Write-Host "ccDir.ps1 was not found next to ccReplace.ps1 or in the current directory." -ForegroundColor Yellow
            return $true
        }

        $outputFile = Join-Path $outputRoot "cc_project_dir.md"

        Write-Host ""
        Write-Host "Running Context Control directory export..." -ForegroundColor Cyan
        Write-Host "Project root: $projectRoot" -ForegroundColor DarkGray
        Write-Host "Output file:  $outputFile" -ForegroundColor DarkGray
        Push-Location $projectRoot
        try {
            & $scriptPath -OutputFile $outputFile
        }
        finally {
            Pop-Location
        }

        if (Test-Path -LiteralPath $outputFile) {
            Write-Host "Agent export verified: $outputFile" -ForegroundColor Green
        }
        else {
            Write-Host "WARNING: ccDir.ps1 returned, but expected output was not found: $outputFile" -ForegroundColor Yellow
        }

        Write-Host "Returned to Context Control." -ForegroundColor DarkGray
        return $true
    }

    if ($clean -ieq "CC") {
        $scriptPath = Get-CcPipelineScriptPath "cc.ps1"
        if ($scriptPath -eq "") {
            Write-Host "cc.ps1 was not found next to ccReplace.ps1 or in the current directory." -ForegroundColor Yellow
            return $true
        }

        $outputFile = Join-Path $outputRoot "cc_code_export.md"

        Write-Host ""
        Write-Host "Running Context Control source export..." -ForegroundColor Cyan
        Write-Host "Project root: $projectRoot" -ForegroundColor DarkGray
        Write-Host "Output file:  $outputFile" -ForegroundColor DarkGray
        Push-Location $projectRoot
        try {
            & $scriptPath -OutputFile $outputFile
        }
        finally {
            Pop-Location
        }

        if (Test-Path -LiteralPath $outputFile) {
            Write-Host "Agent export verified: $outputFile" -ForegroundColor Green
        }
        else {
            Write-Host "WARNING: cc.ps1 returned, but expected output was not found: $outputFile" -ForegroundColor Yellow
        }

        Write-Host "Returned to Context Control." -ForegroundColor DarkGray
        return $true
    }

    if ($clean -ieq "GO") {
        Write-Host ""
        Write-Host "GO patch mode." -ForegroundColor Cyan
        Write-Host "Paste raw BEGIN CC-REPLACE blocks into this terminal." -ForegroundColor DarkGray
        Write-Host "This uses the same preflight and confirmation stage as patch.txt." -ForegroundColor DarkGray
        Write-Host "Clipboard auto-load is disabled; GO will not read or apply clipboard text by itself." -ForegroundColor DarkGray

        if (Get-Command Read-CcGoManualPatchText -ErrorAction SilentlyContinue) {
            $patchText = Read-CcGoManualPatchText
        }
        else {
            $patchText = Read-PasteInputText
        }

        if ([string]::IsNullOrWhiteSpace($patchText)) {
            Write-Host "No GO patch text." -ForegroundColor Yellow
            return $true
        }

        try {
            [void](Invoke-CcReplaceText $patchText -NoExitOnCancel)
        }
        catch {
            Write-Host "GO patch failed: $($_.Exception.Message)" -ForegroundColor Red
        }

        Write-Host "Returned to Context Control." -ForegroundColor DarkGray
        return $true
    }

    return $false
}
