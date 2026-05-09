# CC-DESC: Shared Context Control settings and path resolution helpers.

function New-CcSharedDefaultSettings {
    return [pscustomobject]@{
        ProjectRoot = ".."
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

    if ([string]::IsNullOrWhiteSpace($PathText)) {
        return (Get-CcSharedScriptDirectory)
    }

    $clean = $PathText.Trim()
    $clean = $clean -replace '/', [System.IO.Path]::DirectorySeparatorChar

    if ([System.IO.Path]::IsPathRooted($clean)) {
        return [System.IO.Path]::GetFullPath($clean)
    }

    return [System.IO.Path]::GetFullPath((Join-Path (Get-CcSharedScriptDirectory) $clean))
}

function Resolve-CcSharedProjectRoot {
    param($Settings)

    $root = ".."
    if ($null -ne $Settings -and
        ($Settings.PSObject.Properties.Name -contains "ProjectRoot") -and
        -not [string]::IsNullOrWhiteSpace([string]$Settings.ProjectRoot)) {
        $root = [string]$Settings.ProjectRoot
    }

    $resolvedRoot = Resolve-CcSharedPathRelativeToScript $root

    # Context Control is designed to live at:
    #   <project-root>/contextcontrol/
    # If an old settings file says ProjectRoot="." or the tool is launched from
    # inside contextcontrol/, do not treat the tool folder as the codebase. Promote
    # source exports/searches to the parent engine folder automatically.
    $leaf = Split-Path -Leaf $resolvedRoot
    if ($leaf -ieq "contextcontrol") {
        $hasContextControlScripts =
            (Test-Path -LiteralPath (Join-Path $resolvedRoot "ccDir.ps1")) -or
            (Test-Path -LiteralPath (Join-Path $resolvedRoot "cc.ps1")) -or
            (Test-Path -LiteralPath (Join-Path $resolvedRoot "ccReplace.ps1"))

        if ($hasContextControlScripts) {
            $parent = Split-Path -Parent $resolvedRoot
            if (-not [string]::IsNullOrWhiteSpace($parent)) {
                return [System.IO.Path]::GetFullPath($parent)
            }
        }
    }

    return $resolvedRoot
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

    $clean = $PathText.Trim()
    $clean = $clean -replace '/', [System.IO.Path]::DirectorySeparatorChar

    if ([System.IO.Path]::IsPathRooted($clean)) {
        return [System.IO.Path]::GetFullPath($clean)
    }

    if ($null -eq $Settings) {
        $Settings = Read-CcSharedSettings
    }

    return [System.IO.Path]::GetFullPath((Join-Path (Resolve-CcSharedOutputRoot $Settings) $clean))
}
