# CC-DESC: Owns CC-REPLACE application runtime and file-apply orchestration.

function Apply-CcReplacePlanEntry {
    param(
        $Entry,
        $BackedUpMap,
        $Settings,
        [switch]$SkipVersionSnapshot
    )

    if ($Entry.Mode -eq "create_directory") {
        if ($DryRun) {
            Write-Host "Dry run create directory: $($Entry.TargetHeader)"
            return $true
        }

        if (-not (Test-Path -LiteralPath $Entry.TargetPath)) {
            New-Item -ItemType Directory -Path $Entry.TargetPath -Force | Out-Null
            Write-Host "Create directory: $($Entry.TargetHeader)"
        }
        else {
            Write-Host "Directory already exists: $($Entry.TargetHeader)"
        }

        return $true
    }

    switch ($Entry.Mode) {
        "function" { Write-Host "Replace function: $($Entry.TargetHeader) :: $($Entry.Name)" }
        "insert_after_function" { Write-Host "Insert after function: $($Entry.TargetHeader) :: $($Entry.Name)" }
        "insert_before_function" { Write-Host "Insert before function: $($Entry.TargetHeader) :: $($Entry.Name)" }
        "delete_function" { Write-Host "Delete function: $($Entry.TargetHeader) :: $($Entry.Name)" }
        "replace_region" { Write-Host "Replace marker region: $($Entry.TargetHeader) :: $($Entry.Name)" }
        "insert_include" { Write-Host "Insert/include check: $($Entry.TargetHeader) :: $($Entry.PartLabel)" }
        "append_to_file" { Write-Host "Append to file: $($Entry.TargetHeader)" }
        "whole_file" {
            if ($Entry.CreatesFile) {
                Write-Host "Create whole file: $($Entry.TargetHeader)"
            }
            else {
                Write-Host "Replace whole file: $($Entry.TargetHeader)"
            }
        }
    }

    if ($DryRun) {
        Write-Host "Dry run: no write."
        return $true
    }

    if ($Entry.SkipWrite) {
        return $true
    }

    Ensure-ParentDirectory $Entry.TargetPath

    $useVersionCache = ([bool]$Settings.VersionCacheEnabled -and -not $NoBackup)
    if ($useVersionCache -and -not [bool]$Entry.IsDuplicate -and -not [bool]$Entry.CreatesFile) {
        Ensure-CcReplaceVersionBaseline $Settings $Entry.TargetHeader $Entry.TargetPath
    }

    Backup-TargetOnce $Entry.TargetPath $BackedUpMap
    Write-TargetLines $Entry.TargetPath $Entry.NewLines $Entry.Newline

    if ((-not $SkipVersionSnapshot) -and $useVersionCache -and -not [bool]$Entry.IsDuplicate) {
        Save-CcReplaceCurrentFileVersion $Settings $Entry.TargetHeader $Entry.TargetPath ("applied {0}" -f $Entry.Mode)
    }

    return $true
}

function Invoke-CcReplaceText {
    param(
        [string]$Text,
        [switch]$NoExitOnCancel
    )

    Start-CcReplaceEditPhase

    try {
        $blocks = @(Parse-CcReplaceBlocks $Text)

        if ($blocks.Count -eq 0) {
            Write-Host "No CC-REPLACE blocks found."
            Write-Host ""
            Write-Host "Expected raw patch file format:"
            Write-Host "BEGIN CC-REPLACE"
            Write-Host "FILE: shaders/terrain/cube.frag"
            Write-Host "MODE: function"
            Write-Host "NAME: lookupMaterialOverlay"
            Write-Host "---"
            Write-Host "code here"
            Write-Host "END CC-REPLACE"
            return
        }

        $settings = Read-CcReplaceSettings
        $plan = @(Analyze-CcReplaceBlocks $blocks)
        $decision = Get-CcReplacePreflightDecision $plan $settings

        if ($decision -eq "cancel") {
            return
        }

        $selected = @()
        if ($decision -eq "all") {
            $selected = @($plan)
        }
        else {
            $selected = @($plan | Where-Object { $_.IsEffective })
        }

        if ($selected.Count -eq 0) {
            Write-Host ""
            Write-Host "No effective edits selected."
            return
        }

        $backedUp = @{}
        $changedTargets = @{}
        $applied = 0

        foreach ($entry in $selected) {
            $entryToApply = $entry

            if (-not $DryRun -and -not [bool]$entry.IsDirectory) {
                $targetKey = ([System.IO.Path]::GetFullPath($entry.TargetPath)).ToLowerInvariant()

                if ($changedTargets.ContainsKey($targetKey)) {
                    $entryToApply = Analyze-CcReplaceBlock $entry.Block

                    if ((-not [bool]$entry.IsDuplicate) -and [bool]$entryToApply.IsDuplicate) {
                        Write-Host "Skip now-duplicate action: $($entryToApply.TargetHeader) :: $($entryToApply.PartLabel)" -ForegroundColor Yellow
                        continue
                    }
                }
            }

            if (Apply-CcReplacePlanEntry $entryToApply $backedUp $settings) {
                $applied++

                if (-not $DryRun -and -not [bool]$entryToApply.IsDirectory -and -not [bool]$entryToApply.SkipWrite) {
                    $targetKey = ([System.IO.Path]::GetFullPath($entryToApply.TargetPath)).ToLowerInvariant()
                    $changedTargets[$targetKey] = $true
                }
            }
        }

        Write-CcReplaceActionSummary $selected $settings "Actions Done:"

        Write-Host ""
        if ($DryRun) {
            Write-Host "Done. Dry-run CC-REPLACE actions checked: $applied"
        }
        else {
            Write-Host "Done. Applied CC-REPLACE actions: $applied"
        }
    }
    finally {
        Stop-CcReplaceEditPhase
    }
}

function Invoke-CcReplaceFile {
    param(
        [string]$Path,
        [switch]$NoExitOnCancel
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "Input file not found: $Path"
    }

    $text = Read-TextFileAutoEncoding $Path
    return (Invoke-CcReplaceText $text -NoExitOnCancel:$NoExitOnCancel)
}

function Apply-Block {
    param($Block, $BackedUpMap)

    $settings = Read-CcReplaceSettings
    $entry = Analyze-CcReplaceBlock $Block
    return Apply-CcReplacePlanEntry $entry $BackedUpMap $settings
}
