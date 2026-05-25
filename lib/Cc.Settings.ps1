# CC-DESC: Shared Context Control settings and path resolution helpers.

function New-CcSharedDefaultSettings {
    return [pscustomobject]@{
        # "auto" detects the project root from the Context Control tool location.
        # Default layout: <project-root>/contextcontrol/, so auto selects the parent
        # project. Explicit paths are respected exactly; use "." to make the
        # Context Control tool folder itself the project root.
        ProjectRoot = "auto"
        OutputRoot = "."
    }
}

function Get-CcSharedScriptDirectory {
    if (-not [string]::IsNullOrWhiteSpace($script:CcToolRoot)) {
        return $script:CcToolRoot
    }

    $candidateRoot = if ($PSScriptRoot -ne "") { $PSScriptRoot } else { (Get-Location).Path }

    if ((Split-Path -Leaf $candidateRoot) -ieq "lib") {
        return (Split-Path -Parent $candidateRoot)
    }

    return $candidateRoot
}

function Get-CcSharedSettingsPath {
    return (Join-Path (Get-CcSharedScriptDirectory) ".ccReplace.settings.json")
}


function Normalize-CcSharedSettingPathText {
    param(
        [string]$PathText,
        [string]$Fallback = ""
    )

    if ([string]::IsNullOrWhiteSpace($PathText)) {
        return $Fallback
    }

    $clean = [string]$PathText
    $clean = $clean -replace "`0", ""

    $parts = @($clean -split "`r?`n" | ForEach-Object { $_.Trim() } | Where-Object { $_ -ne "" })
    if ($parts.Count -gt 0) {
        $clean = $parts[$parts.Count - 1]
    }

    $clean = $clean.Trim().Trim('"')

    for ($i = 0; $i -lt 4; $i++) {
        $next = $clean

        if ($next -match '^\s*(?:Resolved\s+)?(?:project\s+root|output\s+folder)\s*:\s*(.+)$') {
            $next = $Matches[1].Trim()
        }
        elseif ($next -match '^\s*(?:project\s+root|output\s+folder)\s*=\s*(.+)$') {
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

    if ($clean -match '^\s*(?:Resolved\s+)?(?:project\s+root|output\s+folder)\s*:?\s*$') {
        return $Fallback
    }

    return $clean
}

function Repair-CcSharedSettingsInPlace {
    param($Settings)

    if ($null -eq $Settings) {
        return $false
    }

    $changed = $false

    $projectRoot = Normalize-CcSharedSettingPathText ([string]$Settings.ProjectRoot) "auto"
    if ([string]::IsNullOrWhiteSpace($projectRoot)) { $projectRoot = "auto" }
    if ([string]$Settings.ProjectRoot -ne $projectRoot) {
        $Settings.ProjectRoot = $projectRoot
        $changed = $true
    }

    $outputRoot = Normalize-CcSharedSettingPathText ([string]$Settings.OutputRoot) "."
    if ([string]::IsNullOrWhiteSpace($outputRoot)) { $outputRoot = "." }
    if ([string]$Settings.OutputRoot -ne $outputRoot) {
        $Settings.OutputRoot = $outputRoot
        $changed = $true
    }

    return $changed
}

function Merge-CcSharedSettings {
    param($Loaded)

    $settings = New-CcSharedDefaultSettings

    if ($null -eq $Loaded) {
        return $settings
    }

    foreach ($prop in $settings.PSObject.Properties.Name) {
        if ($Loaded.PSObject.Properties.Name -contains $prop) {
            $settings.$prop = $Loaded.$prop
        }
    }

    [void](Repair-CcSharedSettingsInPlace $settings)
    return $settings
}

function Read-CcSharedSettings {
    $path = Get-CcSharedSettingsPath

    if (-not (Test-Path -LiteralPath $path)) {
        return New-CcSharedDefaultSettings
    }

    try {
        $json = Read-TextFileAutoEncoding $path
        if ([string]::IsNullOrWhiteSpace($json)) {
            return New-CcSharedDefaultSettings
        }

        return Merge-CcSharedSettings ($json | ConvertFrom-Json)
    }
    catch {
        Write-Host "Context Control settings could not be read, using defaults: $($_.Exception.Message)" -ForegroundColor Yellow
        return New-CcSharedDefaultSettings
    }
}

function Resolve-CcSharedPathRelativeToScript {
    param([string]$PathText)

    $clean = Normalize-CcSharedSettingPathText $PathText ""
    if ([string]::IsNullOrWhiteSpace($clean)) {
        return (Get-CcSharedScriptDirectory)
    }

    $clean = $clean -replace '/', [System.IO.Path]::DirectorySeparatorChar

    if ([System.IO.Path]::IsPathRooted($clean)) {
        return [System.IO.Path]::GetFullPath($clean)
    }

    return [System.IO.Path]::GetFullPath((Join-Path (Get-CcSharedScriptDirectory) $clean))
}

function Resolve-CcSharedProjectRoot {
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

    $clean = Normalize-CcSharedSettingPathText $root "auto"

    if ([string]::IsNullOrWhiteSpace($clean) -or $clean -ieq "auto") {
        $toolRoot = Get-CcSharedScriptDirectory
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
    return Resolve-CcSharedPathRelativeToScript $clean
}

function Resolve-CcSharedOutputRoot {
    param($Settings)

    $root = "."
    if ($null -ne $Settings -and
        ($Settings.PSObject.Properties.Name -contains "OutputRoot") -and
        -not [string]::IsNullOrWhiteSpace([string]$Settings.OutputRoot)) {
        $root = [string]$Settings.OutputRoot
    }

    return Resolve-CcSharedPathRelativeToScript $root
}

function Resolve-CcSharedOutputPath {
    param(
        [string]$PathText,
        $Settings = $null
    )

    if ([string]::IsNullOrWhiteSpace($PathText)) {
        throw "Missing output path."
    }

    $clean = Normalize-CcSharedSettingPathText $PathText ""
    $clean = $clean -replace '/', [System.IO.Path]::DirectorySeparatorChar

    if ([System.IO.Path]::IsPathRooted($clean)) {
        return [System.IO.Path]::GetFullPath($clean)
    }

    if ($null -eq $Settings) {
        $Settings = Read-CcSharedSettings
    }

    return [System.IO.Path]::GetFullPath((Join-Path (Resolve-CcSharedOutputRoot $Settings) $clean))
}
