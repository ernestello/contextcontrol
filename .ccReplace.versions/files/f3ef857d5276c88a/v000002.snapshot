# CC-DESC: Owns CC-REPLACE agent mode patch watcher.

function Test-CcAgentConsoleKeyAvailable {
    try {
        if ([Console]::IsInputRedirected) {
            return $false
        }

        return [Console]::KeyAvailable
    }
    catch {
        # macOS/Linux shells, IDE terminals, and redirected runs can throw here.
        # Patch watching must continue even when interactive DIR/CC shortcuts are
        # unavailable, so treat this as "no key pressed" instead of spamming errors.
        return $false
    }
}

function Invoke-CcReplaceAgentMode {
    function Initialize-CcAgentWatchFile {
        param([string]$WatchPath)

        $parent = Split-Path -Parent $WatchPath
        if (-not [string]::IsNullOrWhiteSpace($parent) -and -not (Test-Path -LiteralPath $parent)) {
            New-Item -ItemType Directory -Path $parent -Force | Out-Null
        }

        if (-not (Test-Path -LiteralPath $WatchPath)) {
            Write-TextFileUtf8NoBom $WatchPath ""
        }
    }

    $settings = Read-CcReplaceSettings
    $path = Resolve-CcOutputPath $settings.DefaultPatchFile $settings
    $lastWrite = $null

    Initialize-CcAgentWatchFile $path

    Write-Host ""
    Write-Host "Agent Mode active. Watching: $path"
    Write-Host "Update the patch file to trigger a run. Press Ctrl+C to stop."
    Write-Host "Type DIR + Enter to run ccDir.ps1, CC + Enter to run cc.ps1, GO + Enter to paste/apply a patch, or SS + Enter to open settings." -ForegroundColor DarkCyan

    while ($true) {
        try {
            if (Test-CcAgentConsoleKeyAvailable) {
                Write-Host ""
                Write-Host "Agent command: " -ForegroundColor Cyan -NoNewline
                $agentCommand = [Console]::ReadLine()
                if ($null -ne $agentCommand) {
                    $normalizedCommand = $agentCommand.Trim()

                    if ($normalizedCommand -ieq "SS") {
                        $settings = Show-CcReplaceSettingsMenu
                        $path = Resolve-CcOutputPath $settings.DefaultPatchFile $settings
                        Initialize-CcAgentWatchFile $path
                        $lastWrite = $null
                        Write-Host "Agent settings updated. Watching: $path" -ForegroundColor DarkCyan
                    }
                    elseif (-not (Invoke-CcPipelineCommand $agentCommand)) {
                        Write-Host "Unknown agent command: $agentCommand" -ForegroundColor Yellow
                    }
                }
                Write-Host "Agent Mode waiting for next patch update..." -ForegroundColor DarkGray
                Start-Sleep -Milliseconds 150
                continue
            }

            if (-not (Test-Path -LiteralPath $path)) {
                Start-Sleep -Milliseconds 250
                continue
            }

            $write = (Get-Item -LiteralPath $path).LastWriteTimeUtc
            if ($null -eq $lastWrite) {
                $lastWrite = $write
                Start-Sleep -Milliseconds 250
                continue
            }

            if ($write -ne $lastWrite) {
                $lastWrite = $write
                Start-Sleep -Milliseconds 150
                try {
                    $success = Invoke-CcReplaceFile $path -NoExitOnCancel
                    if ($success) {
                        Write-Host "Agent Mode waiting for next patch update..." -ForegroundColor DarkGray
                    }
                }
                catch {
                    Write-Host "Agent Mode patch failed: $($_.Exception.Message)" -ForegroundColor Red
                    Write-Host "Agent Mode waiting for next patch update..." -ForegroundColor DarkGray
                }
            }

            Start-Sleep -Milliseconds 250
        }
        catch {
            Write-Host "Agent Mode error: $($_.Exception.Message)" -ForegroundColor Red
            Start-Sleep -Milliseconds 500
        }
    }
}
