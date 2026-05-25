# CC-DESC: Owns CC-REPLACE help and main interactive menu.

function Show-CcReplaceHelp {
    Write-Host ""
    Write-Host "ccReplace.ps1"
    Write-Host "Applies raw BEGIN CC-REPLACE blocks from patch files or pasted text."
    Write-Host ""
    Write-Host "Usage:"
    Write-Host "  .\ccReplace.ps1 -InputFile .\patch.txt"
    Write-Host "  .\ccReplace.ps1 -InputFile .\patch.txt -DryRun"
    Write-Host "  .\ccReplace.ps1 -InputFile .\patch.txt -PlanOnly -Json"
    Write-Host "  .\ccReplace.ps1 -InputFile .\patch.txt -Apply effective"
    Write-Host "  .\ccReplace.ps1 -InputFile .\patch.txt -NoBackup"
    Write-Host "  .\ccReplace.ps1"
    Write-Host "  .\ccReplace.ps1 -AgentMode"
    Write-Host ""
    Write-Host "Project root:"
    Write-Host "  Default ProjectRoot is auto."
    Write-Host "  In the normal <project>/contextcontrol layout, auto resolves patch/export targets to the parent project."
    Write-Host "  Explicit settings win: use '.' or an absolute contextcontrol path to work on Context Control itself."
    Write-Host "  Change this in contextcontrol/.ccReplace.settings.json or Settings option 11."
    Write-Host "  .\ccStart.ps1"
    Write-Host "  .\ccReplace.ps1 -Help"
    Write-Host ""
    Write-Host "No-parameter menu:"
    Write-Host "  1. Run using existing default patch file"
    Write-Host "  2. Provide your own patch file path"
    Write-Host "  3. Paste CC-REPLACE text manually"
    Write-Host "  4. Settings / project root / output folder"
    Write-Host "  5. Agent Mode: watch the default patch file and apply when it changes"
    Write-Host "  6. Run ccDir.ps1 directory export"
    Write-Host "  7. Run cc.ps1 source/function export"
    Write-Host "  8. GO: paste CC-REPLACE blocks and apply with the normal preflight confirmation"
    Write-Host ""
    Write-Host "Agent / confirmation commands:"
    Write-Host "  DIR  Run ccDir.ps1 directory export"
    Write-Host "  CC   Run cc.ps1 source/function export"
    Write-Host "  GO   Paste CC-REPLACE blocks and apply them immediately after preflight confirmation"
    Write-Host "  SS   Open Settings from Agent Mode"
    Write-Host ""
    Write-Host "Settings file: $(Get-CcReplaceSettingsPath)"
    Write-Host "Settings include project root, output folder, UI blocks, directory prefixes, default patch file, confirmation stage, repeat-invalid-input, and version cache."
    Write-Host ""
    Write-Host "Version cache:"
    Write-Host "  Enabled by default unless disabled in Settings."
    Write-Host "  Stored under VersionCacheRoot, default .ccReplace.versions."
    Write-Host "  Existing files get a baseline version before their first CC-REPLACE edit."
    Write-Host "  Every successful non-duplicate file write creates the next file version."
    Write-Host "  Rollback writes the selected snapshot to disk and creates a new latest version."
    Write-Host "  -NoBackup disables version-cache writes for that run."
    Write-Host ""
    Write-Host "Duplicate handling:"
    Write-Host "  Duplicates only: type exactly 'still proceed' to apply duplicate actions."
    Write-Host "  Mixed effective edits + duplicates: type Y/yes/etc to ignore duplicates and apply effective edits, or 'still proceed' to apply all."
    Write-Host "  Any other input cancels unless repeat-invalid-input is enabled."
    Write-Host ""
    Write-Host "Supported modes:"
    Write-Host "  function, insert_after_function, insert_before_function, delete_function, replace_region, append_to_file, whole_file, create_directory, insert_include"
    Write-Host ""
}

function Show-CcReplaceMainMenu {
    $settings = Read-CcReplaceSettings

    while ($true) {
        Write-Host ""
        Write-Host "CC-REPLACE Menu"
        Write-Host "1. Run using existing $($settings.DefaultPatchFile)"
        Write-Host "2. Provide your own filepath to another patch file"
        Write-Host "3. Paste your own text"
        Write-Host "4. Settings / project root / output folder"
        Write-Host "5. Agent Mode - watch $($settings.DefaultPatchFile) and apply when it changes"
        Write-Host "6. Run ccDir.ps1 directory export"
        Write-Host "7. Run cc.ps1 source/function export"
        Write-Host "8. GO - paste CC-REPLACE blocks and apply with preflight confirmation"
        Write-Host "0. Exit"
        Write-Host ""
        Write-Host "Pick option: " -NoNewline

        $choice = [Console]::ReadLine()
        if ($null -eq $choice) { exit 1 }
        $choice = $choice.Trim()

        switch ($choice) {
            "1" { Invoke-CcReplaceFile (Resolve-CcOutputPath $settings.DefaultPatchFile $settings); return }
            "2" {
                Write-Host "Patch file path: " -NoNewline
                $path = [Console]::ReadLine()
                if ($null -ne $path -and $path.Trim() -ne "") {
                    Invoke-CcReplaceFile $path.Trim()
                    return
                }
            }
            "3" { Invoke-CcReplaceText (Read-PasteInputText); return }
            "4" { $settings = Show-CcReplaceSettingsMenu }
            "5" { Invoke-CcReplaceAgentMode; return }
            "6" { [void](Invoke-CcPipelineCommand "DIR") }
            "7" { [void](Invoke-CcPipelineCommand "CC") }
            "8" { [void](Invoke-CcPipelineCommand "GO") }
            "0" { return }
            default { Write-Host "Unknown menu option." -ForegroundColor Yellow }
        }
    }
}
