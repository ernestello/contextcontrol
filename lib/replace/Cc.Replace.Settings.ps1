# CC-DESC: Owns CC-REPLACE settings, settings persistence, and settings menu.

function New-CcReplaceDefaultSettings {
    return [pscustomobject]@{
        # ProjectRoot is resolved relative to this ccReplace.ps1 file when it is
        # not absolute. "auto" keeps the normal layout zero-config:
        #   <project-root>/contextcontrol/ccReplace.ps1
        # In that layout, auto resolves to the parent project folder.
        # Explicit paths are respected exactly; use "." to edit Context Control itself.
        ProjectRoot = "auto"

        # OutputRoot is where Context Control working files live by default.
        # Default "." means the contextcontrol/ folder, keeping the project root clean.
        OutputRoot = "."

        DefaultPatchFile = "patch.txt"
        ShowPreflightStatistics = $true
        ShowFileDetails = $true
        ShowCreatedRemovedLists = $true
        ShowDirectoryPrefixes = $true
        ConfirmationStage = $true
        RepeatInvalidInput = $false

        # Enabled by default: first successful edits capture a baseline and then
        # a post-apply version snapshot unless -NoBackup is used or the user
        # explicitly disables this in Settings.
        VersionCacheEnabled = $true

        # Relative paths are resolved from OutputRoot, so the default cache lives
        # in contextcontrol/.ccReplace.versions instead of cluttering project root.
        VersionCacheRoot = ".ccReplace.versions"
    }
}

function Get-CcReplaceScriptDirectory {
    if (-not [string]::IsNullOrWhiteSpace($script:CcToolRoot)) {
        return $script:CcToolRoot
    }

    $candidateRoot = if ($PSScriptRoot -ne "") { $PSScriptRoot } else { (Get-Location).Path }

    if ((Split-Path -Leaf $candidateRoot) -ieq "lib") {
        return (Split-Path -Parent $candidateRoot)
    }

    return $candidateRoot
}

function Get-CcReplaceSettingsPath {
    # Keep Context Control settings with the tool, not loose in the project root.
    return (Join-Path (Get-CcReplaceScriptDirectory) ".ccReplace.settings.json")
}


function Normalize-CcReplaceSettingPathText {
    param(
        [string]$PathText,
        [string]$Fallback = ""
    )

    if ([string]::IsNullOrWhiteSpace($PathText)) {
        return $Fallback
    }

    $clean = [string]$PathText
    $clean = $clean -replace "`0", ""

    # If a whole console line or multi-line paste was accidentally saved, prefer
    # the last non-empty line. This catches pasted menu output without making
    # normal absolute/relative paths slower or more magical.
    $parts = @($clean -split "`r?`n" | ForEach-Object { $_.Trim() } | Where-Object { $_ -ne "" })
    if ($parts.Count -gt 0) {
        $clean = $parts[$parts.Count - 1]
    }

    $clean = $clean.Trim().Trim('"')

    for ($i = 0; $i -lt 4; $i++) {
        $next = $clean

        if ($next -match '^\s*(?:Resolved\s+)?(?:project\s+root|output\s+folder|version\s+cache|version\s+cache\s+directory|patch\s+file)\s*:\s*(.+)$') {
            $next = $Matches[1].Trim()
        }
        elseif ($next -match '^\s*(?:project\s+root|output\s+folder|version\s+cache|version\s+cache\s+directory|patch\s+file)\s*=\s*(.+)$') {
            $next = $Matches[1].Trim()
        }

        $next = $next.Trim().Trim('"')
        if ($next -eq $clean) {
            break
        }

        $clean = $next
    }

    if ([string]::IsNullOrWhiteSpace($clean)) {
        return $Fallback
    }

    # A label without a usable value is not a path. Fall back instead of feeding
    # garbage into System.IO.Path.GetFullPath and killing the settings menu.
    if ($clean -match '^\s*(?:Resolved\s+)?(?:project\s+root|output\s+folder|version\s+cache|version\s+cache\s+directory|patch\s+file)\s*:?\s*$') {
        return $Fallback
    }

    return $clean
}

function Repair-CcReplaceSettingsInPlace {
    param($Settings)

    if ($null -eq $Settings) {
        return $false
    }

    $changed = $false

    $projectRoot = Normalize-CcReplaceSettingPathText ([string]$Settings.ProjectRoot) "auto"
    if ([string]::IsNullOrWhiteSpace($projectRoot)) { $projectRoot = "auto" }
    if ([string]$Settings.ProjectRoot -ne $projectRoot) {
        $Settings.ProjectRoot = $projectRoot
        $changed = $true
    }

    $outputRoot = Normalize-CcReplaceSettingPathText ([string]$Settings.OutputRoot) "."
    if ([string]::IsNullOrWhiteSpace($outputRoot)) { $outputRoot = "." }
    if ([string]$Settings.OutputRoot -ne $outputRoot) {
        $Settings.OutputRoot = $outputRoot
        $changed = $true
    }

    $patchFile = Normalize-CcReplaceSettingPathText ([string]$Settings.DefaultPatchFile) "patch.txt"
    if ([string]::IsNullOrWhiteSpace($patchFile)) { $patchFile = "patch.txt" }
    if ([string]$Settings.DefaultPatchFile -ne $patchFile) {
        $Settings.DefaultPatchFile = $patchFile
        $changed = $true
    }

    $versionRoot = Normalize-CcReplaceSettingPathText ([string]$Settings.VersionCacheRoot) ".ccReplace.versions"
    if ([string]::IsNullOrWhiteSpace($versionRoot)) { $versionRoot = ".ccReplace.versions" }
    if ([string]$Settings.VersionCacheRoot -ne $versionRoot) {
        $Settings.VersionCacheRoot = $versionRoot
        $changed = $true
    }

    return $changed
}

function Resolve-CcPathRelativeToScript {
    param([string]$PathText)

    $clean = Normalize-CcReplaceSettingPathText $PathText ""
    if ([string]::IsNullOrWhiteSpace($clean)) {
        return (Get-Location).Path
    }

    $clean = $clean -replace '/', [System.IO.Path]::DirectorySeparatorChar

    if ([System.IO.Path]::IsPathRooted($clean)) {
        return [System.IO.Path]::GetFullPath($clean)
    }

    return [System.IO.Path]::GetFullPath((Join-Path (Get-CcReplaceScriptDirectory) $clean))
}

function Resolve-CcProjectRoot {
    param($Settings)

    if (-not [string]::IsNullOrWhiteSpace($env:CC_WORKBENCH_PROJECT_ROOT)) {
        return [System.IO.Path]::GetFullPath($env:CC_WORKBENCH_PROJECT_ROOT)
    }

    $root = "auto"
    if ($null -ne $Settings -and
        ($Settings.PSObject.Properties.Name -contains "ProjectRoot") -and
        -not [string]::IsNullOrWhiteSpace([string]$Settings.ProjectRoot)) {
        $root = [string]$Settings.ProjectRoot
    }

    $clean = Normalize-CcReplaceSettingPathText $root "auto"

    if ([string]::IsNullOrWhiteSpace($clean) -or $clean -ieq "auto") {
        $toolRoot = Get-CcReplaceScriptDirectory
        $leaf = Split-Path -Leaf $toolRoot

        if ($leaf -ieq "contextcontrol") {
            $parent = Split-Path -Parent $toolRoot
            if (-not [string]::IsNullOrWhiteSpace($parent)) {
                return [System.IO.Path]::GetFullPath($parent)
            }
        }

        return [System.IO.Path]::GetFullPath($toolRoot)
    }

    # Explicit user paths must win. In particular, do not auto-promote
    # D:\...\contextcontrol or "." to the parent repo; those are valid when
    # editing Context Control itself.
    return Resolve-CcPathRelativeToScript $clean
}

function Resolve-CcOutputRoot {
    param($Settings)

    $root = "."
    if ($null -ne $Settings -and
        ($Settings.PSObject.Properties.Name -contains "OutputRoot") -and
        -not [string]::IsNullOrWhiteSpace([string]$Settings.OutputRoot)) {
        $root = [string]$Settings.OutputRoot
    }

    return Resolve-CcPathRelativeToScript $root
}

function Resolve-CcOutputPath {
    param(
        [string]$PathText,
        $Settings = $null
    )

    if ([string]::IsNullOrWhiteSpace($PathText)) {
        throw "Missing output path."
    }

    $clean = Normalize-CcReplaceSettingPathText $PathText ""
    $clean = $clean -replace '/', [System.IO.Path]::DirectorySeparatorChar

    if ([System.IO.Path]::IsPathRooted($clean)) {
        return [System.IO.Path]::GetFullPath($clean)
    }

    if ($null -eq $Settings) {
        $Settings = Read-CcReplaceSettings
    }

    return [System.IO.Path]::GetFullPath((Join-Path (Resolve-CcOutputRoot $Settings) $clean))
}

function Get-CcProjectRoot {
    $settings = Read-CcReplaceSettings
    return Resolve-CcProjectRoot $settings
}

function Merge-CcReplaceSettings {
    param($Loaded)

    $settings = New-CcReplaceDefaultSettings

    if ($null -eq $Loaded) {
        return $settings
    }

    foreach ($prop in $settings.PSObject.Properties.Name) {
        if ($Loaded.PSObject.Properties.Name -contains $prop) {
            $settings.$prop = $Loaded.$prop
        }
    }

    return $settings
}

function Read-CcReplaceSettings {
    $path = Get-CcReplaceSettingsPath

    if (-not (Test-Path -LiteralPath $path)) {
        $settings = New-CcReplaceDefaultSettings
        Save-CcReplaceSettings $settings
        return $settings
    }

    try {
        $json = Read-TextFileAutoEncoding $path
        if ($json.Trim() -eq "") {
            $settings = New-CcReplaceDefaultSettings
            Save-CcReplaceSettings $settings
            return $settings
        }

        $loaded = $json | ConvertFrom-Json
        $settings = Merge-CcReplaceSettings $loaded
        $wasRepaired = Repair-CcReplaceSettingsInPlace $settings

        # Persist newly introduced settings on first run after an update. Do not
        # override existing user choices; only normalize missing keys into the
        # settings file so Windows/macOS/Linux all start from the same durable state.
        $shouldSave = [bool]$wasRepaired
        foreach ($prop in $settings.PSObject.Properties.Name) {
            if (-not ($loaded.PSObject.Properties.Name -contains $prop)) {
                $shouldSave = $true
                break
            }
        }

        if ($shouldSave) {
            Save-CcReplaceSettings $settings
        }

        return $settings
    }
    catch {
        Write-Host "Settings file is invalid, rewriting defaults: $path" -ForegroundColor Yellow
        $settings = New-CcReplaceDefaultSettings
        Save-CcReplaceSettings $settings
        return $settings
    }
}

function Save-CcReplaceSettings {
    param($Settings)

    [void](Repair-CcReplaceSettingsInPlace $Settings)

    $path = Get-CcReplaceSettingsPath
    $json = $Settings | ConvertTo-Json -Depth 8
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($path, $json + [Environment]::NewLine, $utf8NoBom)
}

function Write-CcReplaceBoolLine {
    param([int]$Index, [string]$Name, [bool]$Value)

    $state = if ($Value) { "ON" } else { "OFF" }
    $color = if ($Value) { "Green" } else { "DarkGray" }
    Write-Host ("{0}. {1}: " -f $Index, $Name) -NoNewline
    Write-Host $state -ForegroundColor $color
}

function Show-CcReplaceSettingsMenu {
    $settings = Read-CcReplaceSettings

    while ($true) {
        Write-Host ""
        Write-Host "CC-REPLACE Settings"
        Write-CcReplaceBoolLine 1 "Preflight statistics" ([bool]$settings.ShowPreflightStatistics)
        Write-CcReplaceBoolLine 2 "File detail statistics" ([bool]$settings.ShowFileDetails)
        Write-CcReplaceBoolLine 3 "Created/removed lists" ([bool]$settings.ShowCreatedRemovedLists)
        Write-CcReplaceBoolLine 4 "Directory prefixes" ([bool]$settings.ShowDirectoryPrefixes)
        Write-CcReplaceBoolLine 5 "Confirmation stage" ([bool]$settings.ConfirmationStage)
        Write-CcReplaceBoolLine 6 "Repeat after incorrect input" ([bool]$settings.RepeatInvalidInput)
        Write-Host "7. Reconfigure default patch file: $($settings.DefaultPatchFile)"
        Write-CcReplaceBoolLine 8 "Version cache" ([bool]$settings.VersionCacheEnabled)
        Write-Host "9. Version log / rollback / remove cached versions"
        $resolvedProjectRoot = Resolve-CcProjectRoot $settings
        $resolvedOutputRoot = Resolve-CcOutputRoot $settings
        $resolvedVersionRoot = Get-CcReplaceVersionRoot $settings
        Write-Host "10. Reconfigure version cache directory: $($settings.VersionCacheRoot)"
        Write-Host "    Resolved version cache: $resolvedVersionRoot" -ForegroundColor DarkGray
        Write-Host "11. Reconfigure project root: $($settings.ProjectRoot)"
        Write-Host "    Resolved project root: $resolvedProjectRoot" -ForegroundColor DarkGray
        Write-Host "12. Reconfigure Context Control output folder: $($settings.OutputRoot)"
        Write-Host "    Resolved output folder: $resolvedOutputRoot" -ForegroundColor DarkGray
        Write-Host "0. Back"
        Write-Host ""
        Write-Host "Pick setting: " -NoNewline

        $choice = [Console]::ReadLine()
        if ($null -eq $choice) { return $settings }
        $choice = $choice.Trim()

        switch ($choice) {
            "1" { $settings.ShowPreflightStatistics = -not [bool]$settings.ShowPreflightStatistics }
            "2" { $settings.ShowFileDetails = -not [bool]$settings.ShowFileDetails }
            "3" { $settings.ShowCreatedRemovedLists = -not [bool]$settings.ShowCreatedRemovedLists }
            "4" { $settings.ShowDirectoryPrefixes = -not [bool]$settings.ShowDirectoryPrefixes }
            "5" { $settings.ConfirmationStage = -not [bool]$settings.ConfirmationStage }
            "6" { $settings.RepeatInvalidInput = -not [bool]$settings.RepeatInvalidInput }
            "7" {
                Write-Host "Enter new default patch file path. Relative paths are resolved from the Context Control output folder." -ForegroundColor DarkGray
                Write-Host "Default: patch.txt" -ForegroundColor DarkGray
                Write-Host "Patch file: " -NoNewline
                $path = [Console]::ReadLine()
                if ($null -ne $path -and $path.Trim() -ne "") {
                    $settings.DefaultPatchFile = Normalize-CcReplaceSettingPathText $path "patch.txt"
                }
            }
            "8" { $settings.VersionCacheEnabled = -not [bool]$settings.VersionCacheEnabled }
            "9" { Show-CcReplaceVersionLogMenu }
            "10" {
                Write-Host "Enter new version cache directory. Relative paths are resolved from the Context Control output folder." -ForegroundColor DarkGray
                Write-Host "Default: .ccReplace.versions" -ForegroundColor DarkGray
                Write-Host "Version cache directory: " -NoNewline
                $path = [Console]::ReadLine()
                if ($null -ne $path -and $path.Trim() -ne "") {
                    $settings.VersionCacheRoot = Normalize-CcReplaceSettingPathText $path ".ccReplace.versions"
                }
            }
            "11" {
                Write-Host "Enter project root path. Relative paths are resolved from the Context Control folder." -ForegroundColor DarkGray
                Write-Host "Default: auto  (when this tool lives in <project>/contextcontrol, use the parent project)" -ForegroundColor DarkGray
                Write-Host "Use '.' for the Context Control tool folder itself." -ForegroundColor DarkGray
                Write-Host "Use '..' for the parent project explicitly, or paste any absolute project path." -ForegroundColor DarkGray
                Write-Host "Project root: " -NoNewline
                $path = [Console]::ReadLine()
                if ($null -ne $path -and $path.Trim() -ne "") {
                    $settings.ProjectRoot = Normalize-CcReplaceSettingPathText $path "auto"
                }
            }
            "12" {
                Write-Host "Enter output folder path. Relative paths are resolved from the Context Control folder." -ForegroundColor DarkGray
                Write-Host "Default: .  (the contextcontrol folder)" -ForegroundColor DarkGray
                Write-Host "Output folder: " -NoNewline
                $path = [Console]::ReadLine()
                if ($null -ne $path -and $path.Trim() -ne "") {
                    $settings.OutputRoot = Normalize-CcReplaceSettingPathText $path "."
                }
            }
            "0" {
                Save-CcReplaceSettings $settings
                return $settings
            }
            default {
                Write-Host "Unknown setting." -ForegroundColor Yellow
            }
        }

        Save-CcReplaceSettings $settings
    }
}
