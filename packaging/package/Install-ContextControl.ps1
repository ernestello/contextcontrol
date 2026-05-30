[CmdletBinding()]
param(
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA 'Programs\ContextControl'),
    [bool]$StartMenuShortcut = $true,
    [switch]$DesktopShortcut,
    [switch]$InstallWebView2Runtime,
    [switch]$NoLaunch
)

$ErrorActionPreference = 'Stop'

function Test-WebView2Runtime {
    $roots = @(
        'HKLM:\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients',
        'HKLM:\SOFTWARE\Microsoft\EdgeUpdate\Clients',
        'HKCU:\Software\Microsoft\EdgeUpdate\Clients'
    )

    foreach ($root in $roots) {
        if (-not (Test-Path -LiteralPath $root)) {
            continue
        }

        foreach ($client in Get-ChildItem -LiteralPath $root -ErrorAction SilentlyContinue) {
            try {
                $props = Get-ItemProperty -LiteralPath $client.PSPath -ErrorAction Stop
                if (($props.name -as [string]) -match 'WebView2') {
                    return $true
                }
            }
            catch {
                continue
            }
        }
    }

    return $false
}

function New-ContextControlShortcut {
    param(
        [Parameter(Mandatory)]
        [string]$Path,
        [Parameter(Mandatory)]
        [string]$TargetPath,
        [Parameter(Mandatory)]
        [string]$WorkingDirectory
    )

    $parent = Split-Path -Parent $Path
    if (-not [string]::IsNullOrWhiteSpace($parent)) {
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
    }

    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($Path)
    $shortcut.TargetPath = $TargetPath
    $shortcut.WorkingDirectory = $WorkingDirectory
    $shortcut.IconLocation = $TargetPath
    $shortcut.Description = 'ContextControl Workbench'
    $shortcut.Save()
}

$sourceDir = $PSScriptRoot
$sourceExe = Join-Path $sourceDir 'ContextControl.Workbench.exe'
if (-not (Test-Path -LiteralPath $sourceExe)) {
    throw "ContextControl.Workbench.exe was not found beside this installer. Extract the release zip before running it."
}

if ($InstallWebView2Runtime -and -not (Test-WebView2Runtime)) {
    $winget = Get-Command winget -ErrorAction SilentlyContinue
    if ($null -eq $winget) {
        Write-Warning 'winget was not found; skipping WebView2 Runtime installation.'
    }
    else {
        Write-Host 'Installing Microsoft Edge WebView2 Runtime with winget...'
        & winget install --id Microsoft.EdgeWebView2Runtime --exact --silent --accept-package-agreements --accept-source-agreements
        if ($LASTEXITCODE -ne 0) {
            Write-Warning "winget exited with code $LASTEXITCODE while installing WebView2 Runtime."
        }
    }
}
elseif (-not (Test-WebView2Runtime)) {
    Write-Warning 'Microsoft Edge WebView2 Runtime was not detected. The Browser workspace may need it; rerun with -InstallWebView2Runtime to install it with winget.'
}

New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
$resolvedInstallDir = (Resolve-Path -LiteralPath $InstallDir).Path

Get-ChildItem -LiteralPath $sourceDir -Force | Where-Object {
    $_.Name -notin @('.ccWorkbench.settings.json')
} | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination $resolvedInstallDir -Recurse -Force
}

$sourceSettings = Join-Path $sourceDir '.ccWorkbench.settings.json'
$targetSettings = Join-Path $resolvedInstallDir '.ccWorkbench.settings.json'
if ((Test-Path -LiteralPath $sourceSettings) -and -not (Test-Path -LiteralPath $targetSettings)) {
    Copy-Item -LiteralPath $sourceSettings -Destination $targetSettings -Force
}

$targetExe = Join-Path $resolvedInstallDir 'ContextControl.Workbench.exe'
if ($StartMenuShortcut) {
    $startMenu = [Environment]::GetFolderPath('StartMenu')
    New-ContextControlShortcut `
        -Path (Join-Path $startMenu 'Programs\ContextControl.lnk') `
        -TargetPath $targetExe `
        -WorkingDirectory $resolvedInstallDir
}

if ($DesktopShortcut) {
    New-ContextControlShortcut `
        -Path (Join-Path ([Environment]::GetFolderPath('Desktop')) 'ContextControl.lnk') `
        -TargetPath $targetExe `
        -WorkingDirectory $resolvedInstallDir
}

Write-Host "ContextControl installed to $resolvedInstallDir"
if (-not $NoLaunch) {
    Start-Process -FilePath $targetExe -WorkingDirectory $resolvedInstallDir
}
