[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$RuntimeIdentifier = 'win-x64',
    [string]$Version = '',
    [switch]$SkipTests,
    [switch]$SkipInstallerExe,
    [switch]$NoZip
)

$ErrorActionPreference = 'Stop'

function Resolve-RepoRoot {
    $scriptDir = Split-Path -Parent $PSCommandPath
    return (Resolve-Path (Join-Path $scriptDir '..')).Path
}

function Remove-DirectorySafely {
    param(
        [Parameter(Mandatory)]
        [string]$Root,
        [Parameter(Mandatory)]
        [string]$Path
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        return
    }

    $rootFull = [System.IO.Path]::GetFullPath($Root).TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    $pathFull = [System.IO.Path]::GetFullPath($Path)
    if (-not $pathFull.StartsWith($rootFull, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove path outside ${rootFull}: $pathFull"
    }

    Remove-Item -LiteralPath $pathFull -Recurse -Force
}

function Wait-FileReady {
    param(
        [Parameter(Mandatory)]
        [string]$Path,
        [int]$TimeoutSeconds = 180
    )

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        if (Test-Path -LiteralPath $Path) {
            try {
                $firstLength = (Get-Item -LiteralPath $Path).Length
                Start-Sleep -Milliseconds 500
                $secondLength = (Get-Item -LiteralPath $Path).Length
                if ($firstLength -ne $secondLength) {
                    continue
                }

                $stream = [System.IO.File]::Open(
                    $Path,
                    [System.IO.FileMode]::Open,
                    [System.IO.FileAccess]::Read,
                    [System.IO.FileShare]::Read)
                $stream.Dispose()
                return
            }
            catch {
                Start-Sleep -Milliseconds 500
            }
        }
        else {
            Start-Sleep -Milliseconds 500
        }
    }

    throw "Timed out waiting for file to be ready: $Path"
}

function New-IExpressInstaller {
    param(
        [Parameter(Mandatory)]
        [string]$StageDir,
        [Parameter(Mandatory)]
        [string]$OutputPath,
        [Parameter(Mandatory)]
        [string]$Version
    )

    $iexpress = Get-Command iexpress.exe -ErrorAction SilentlyContinue
    if ($null -eq $iexpress) {
        throw 'iexpress.exe was not found. Build on Windows to create the installer EXE.'
    }

    $sedPath = Join-Path (Split-Path -Parent $OutputPath) 'ContextControl-win-x64-setup.sed'
    if (Test-Path -LiteralPath $OutputPath) {
        Remove-Item -LiteralPath $OutputPath -Force
    }

    $files = Get-ChildItem -LiteralPath $StageDir -File -Force | Sort-Object Name
    $stringLines = New-Object System.Collections.Generic.List[string]
    $fileLines = New-Object System.Collections.Generic.List[string]
    for ($i = 0; $i -lt $files.Count; $i++) {
        $key = "FILE$i"
        $stringLines.Add("$key=`"$($files[$i].Name)`"")
        $fileLines.Add("%$key%=")
    }

    $stageRoot = [System.IO.Path]::GetFullPath($StageDir).TrimEnd('\') + '\'
    $outputFull = [System.IO.Path]::GetFullPath($OutputPath)
    $sed = @"
[Version]
Class=IEXPRESS
SEDVersion=3
[Options]
PackagePurpose=InstallApp
ShowInstallProgramWindow=1
HideExtractAnimation=1
UseLongFileName=1
InsideCompressed=1
CAB_FixedSize=0
CAB_ResvCodeSigning=0
RebootMode=N
InstallPrompt=
DisplayLicense=
FinishMessage=ContextControl $Version installed.
TargetName=$outputFull
FriendlyName=ContextControl $Version
AppLaunched=powershell.exe -NoProfile -ExecutionPolicy Bypass -File Install-ContextControl.ps1
PostInstallCmd=<None>
AdminQuietInstCmd=powershell.exe -NoProfile -ExecutionPolicy Bypass -File Install-ContextControl.ps1 -NoLaunch
UserQuietInstCmd=powershell.exe -NoProfile -ExecutionPolicy Bypass -File Install-ContextControl.ps1 -NoLaunch
SourceFiles=SourceFiles
[Strings]
$($stringLines -join "`r`n")
[SourceFiles]
SourceFiles0=$stageRoot
[SourceFiles0]
$($fileLines -join "`r`n")
"@

    Set-Content -LiteralPath $sedPath -Value $sed -Encoding ascii
    & $iexpress.Source /N /Q $sedPath
    Wait-FileReady -Path $OutputPath -TimeoutSeconds 180

    Remove-Item -LiteralPath $sedPath -Force -ErrorAction SilentlyContinue
    Get-ChildItem -LiteralPath (Split-Path -Parent $OutputPath) -Force -File |
        Where-Object { $_.Name -like "~$([System.IO.Path]::GetFileNameWithoutExtension($OutputPath)).*" } |
        Remove-Item -Force -ErrorAction SilentlyContinue
}

$repoRoot = Resolve-RepoRoot
$project = Join-Path $repoRoot 'ide\ContextControl.Workbench\ContextControl.Workbench.csproj'
$testProject = Join-Path $repoRoot 'ide\ContextControl.Workbench.Tests\ContextControl.Workbench.Tests.csproj'
$releaseRoot = Join-Path $repoRoot '.tmp\release'
$publishDir = Join-Path $releaseRoot "publish\$RuntimeIdentifier"
$packageName = "ContextControl-$RuntimeIdentifier"
$stageDir = Join-Path $releaseRoot $packageName
$zipPath = Join-Path $releaseRoot "$packageName.zip"
$shaPath = "$zipPath.sha256.txt"
$installerPath = Join-Path $releaseRoot "$packageName-Setup.exe"
$installerShaPath = "$installerPath.sha256.txt"

if ([string]::IsNullOrWhiteSpace($Version)) {
    $versionFile = Join-Path $repoRoot 'packaging\VERSION'
    $fileVersion = if (Test-Path -LiteralPath $versionFile) {
        (Get-Content -LiteralPath $versionFile -TotalCount 1).Trim()
    }
    else {
        ''
    }

    $Version = if ([string]::IsNullOrWhiteSpace($fileVersion)) { '0.0.0-dev' } else { $fileVersion }
}

$displayVersion = $Version.Trim()
$packageVersion = $displayVersion -replace '^v(?=\d+\.\d+\.\d+)', ''
$numericVersionMatch = [regex]::Match($packageVersion, '(\d+)\.(\d+)\.(\d+)')
$assemblyVersion = if ($numericVersionMatch.Success) {
    "$($numericVersionMatch.Groups[1].Value).$($numericVersionMatch.Groups[2].Value).$($numericVersionMatch.Groups[3].Value).0"
}
else {
    '0.0.0.0'
}

New-Item -ItemType Directory -Path $releaseRoot -Force | Out-Null
Remove-DirectorySafely -Root $releaseRoot -Path $publishDir
Remove-DirectorySafely -Root $releaseRoot -Path $stageDir
if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}
if (Test-Path -LiteralPath $shaPath) {
    Remove-Item -LiteralPath $shaPath -Force
}
if (Test-Path -LiteralPath $installerPath) {
    Remove-Item -LiteralPath $installerPath -Force
}
if (Test-Path -LiteralPath $installerShaPath) {
    Remove-Item -LiteralPath $installerShaPath -Force
}

if (-not $SkipTests) {
    dotnet run --project $testProject --configuration $Configuration
}

dotnet publish $project `
    --configuration $Configuration `
    --runtime $RuntimeIdentifier `
    --self-contained true `
    --output $publishDir `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none `
    -p:DebugSymbols=false `
    -p:Version=$packageVersion `
    -p:AssemblyVersion=$assemblyVersion `
    -p:FileVersion=$assemblyVersion `
    -p:InformationalVersion=$displayVersion

New-Item -ItemType Directory -Path $stageDir -Force | Out-Null
Get-ChildItem -LiteralPath $publishDir -Force | Where-Object {
    $_.Extension -notin @('.pdb', '.xml')
} | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination $stageDir -Recurse -Force
}

$packageDir = Join-Path $repoRoot 'packaging\package'
Copy-Item -LiteralPath (Join-Path $packageDir 'INSTALL.md') -Destination $stageDir -Force
Copy-Item -LiteralPath (Join-Path $packageDir 'Install-ContextControl.ps1') -Destination $stageDir -Force
Copy-Item -LiteralPath (Join-Path $packageDir 'Start-ContextControl.cmd') -Destination $stageDir -Force
Copy-Item -LiteralPath (Join-Path $repoRoot 'packaging\release-settings.template.json') -Destination (Join-Path $stageDir '.ccWorkbench.settings.json') -Force

$manifest = [ordered]@{
    Name = 'ContextControl'
    RuntimeIdentifier = $RuntimeIdentifier
    Configuration = $Configuration
    Version = $packageVersion
    DisplayVersion = $displayVersion
    BuiltUtc = (Get-Date).ToUniversalTime().ToString('o')
    EntryPoint = 'ContextControl.Workbench.exe'
    DotNetRuntime = 'self-contained'
    Notes = @(
        'No .NET runtime install required.',
        'No LLM weights are bundled.',
        'Use the app Dependencies and Local LLM pages to install runtimes and download models.'
    )
}
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $stageDir 'release-manifest.json') -Encoding utf8

if (-not $NoZip) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::CreateFromDirectory(
        $stageDir,
        $zipPath,
        [System.IO.Compression.CompressionLevel]::Optimal,
        $false)
    $hash = Get-FileHash -Algorithm SHA256 -LiteralPath $zipPath
    "$($hash.Hash)  $([System.IO.Path]::GetFileName($zipPath))" | Set-Content -LiteralPath $shaPath -Encoding ascii
}

if (-not $SkipInstallerExe) {
    New-IExpressInstaller -StageDir $stageDir -OutputPath $installerPath -Version $displayVersion
    $installerHash = Get-FileHash -Algorithm SHA256 -LiteralPath $installerPath
    "$($installerHash.Hash)  $([System.IO.Path]::GetFileName($installerPath))" | Set-Content -LiteralPath $installerShaPath -Encoding ascii
}

$exePath = Join-Path $stageDir 'ContextControl.Workbench.exe'
$sizeMb = if (Test-Path -LiteralPath $exePath) {
    [math]::Round((Get-Item -LiteralPath $exePath).Length / 1MB, 1)
}
else {
    0
}

Write-Host "Release folder: $stageDir"
if (-not $NoZip) {
    Write-Host "Release zip: $zipPath"
    Write-Host "SHA256: $shaPath"
}
if (-not $SkipInstallerExe) {
    Write-Host "Installer EXE: $installerPath"
    Write-Host "Installer SHA256: $installerShaPath"
}
Write-Host "Executable size: $sizeMb MB"
