# CC-DESC: Owns CC-REPLACE version cache, snapshots, rollback, and version badges.

function Get-CcReplaceVersionRoot {
    param($Settings)

    $root = ".ccReplace.versions"
    if ($null -ne $Settings -and ($Settings.PSObject.Properties.Name -contains "VersionCacheRoot") -and $Settings.VersionCacheRoot -ne "") {
        $root = [string]$Settings.VersionCacheRoot
    }

    # Keep relative version-cache paths with the Context Control output folder.
    # Absolute paths still work for users who want the cache somewhere else.
    return Resolve-CcOutputPath $root $Settings
}

function Get-CcReplaceVersionIndexPath {
    param($Settings)

    return Join-Path (Get-CcReplaceVersionRoot $Settings) "index.json"
}

function New-CcReplaceVersionIndex {
    return [pscustomobject]@{
        Files = @()
    }
}

function Normalize-CcReplaceVersionIndex {
    param($Index)

    if ($null -eq $Index) {
        return New-CcReplaceVersionIndex
    }

    if (-not ($Index.PSObject.Properties.Name -contains "Files") -or $null -eq $Index.Files) {
        $Index | Add-Member -NotePropertyName Files -NotePropertyValue @() -Force
    }

    $Index.Files = @($Index.Files)

    foreach ($file in @($Index.Files)) {
        if (-not ($file.PSObject.Properties.Name -contains "Versions") -or $null -eq $file.Versions) {
            $file | Add-Member -NotePropertyName Versions -NotePropertyValue @() -Force
        }
        $file.Versions = @($file.Versions)

        if (-not ($file.PSObject.Properties.Name -contains "CurrentVersion")) {
            $max = 0
            foreach ($version in @($file.Versions)) {
                if ([int]$version.Version -gt $max) { $max = [int]$version.Version }
            }
            $file | Add-Member -NotePropertyName CurrentVersion -NotePropertyValue $max -Force
        }
    }

    return $Index
}

function Read-CcReplaceVersionIndex {
    param($Settings)

    $path = Get-CcReplaceVersionIndexPath $Settings
    $cacheKey = ([System.IO.Path]::GetFullPath($path)).ToLowerInvariant()

    if (($script:CcReplaceVersionIndexCacheKey -eq $cacheKey) -and
        ($null -ne $script:CcReplaceVersionIndexCache)) {
        return $script:CcReplaceVersionIndexCache
    }

    if (-not (Test-Path -LiteralPath $path)) {
        $empty = New-CcReplaceVersionIndex
        $script:CcReplaceVersionIndexCacheKey = $cacheKey
        $script:CcReplaceVersionIndexCache = $empty
        return $empty
    }

    try {
        $json = Read-TextFileAutoEncoding $path
        if ($json.Trim() -eq "") {
            $empty = New-CcReplaceVersionIndex
            $script:CcReplaceVersionIndexCacheKey = $cacheKey
            $script:CcReplaceVersionIndexCache = $empty
            return $empty
        }

        $index = Normalize-CcReplaceVersionIndex ($json | ConvertFrom-Json)
        $script:CcReplaceVersionIndexCacheKey = $cacheKey
        $script:CcReplaceVersionIndexCache = $index
        return $index
    }
    catch {
        Write-Host "Version index is invalid, starting with empty index: $path" -ForegroundColor Yellow
        $empty = New-CcReplaceVersionIndex
        $script:CcReplaceVersionIndexCacheKey = $cacheKey
        $script:CcReplaceVersionIndexCache = $empty
        return $empty
    }
}

function Save-CcReplaceVersionIndex {
    param($Settings, $Index)

    $root = Get-CcReplaceVersionRoot $Settings
    if (-not (Test-Path -LiteralPath $root)) {
        New-Item -ItemType Directory -Path $root -Force | Out-Null
    }

    $path = Get-CcReplaceVersionIndexPath $Settings
    $indexToSave = Normalize-CcReplaceVersionIndex $Index
    $json = $indexToSave | ConvertTo-Json -Depth 32
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($path, $json + [Environment]::NewLine, $utf8NoBom)

    $script:CcReplaceVersionIndexCacheKey = ([System.IO.Path]::GetFullPath($path)).ToLowerInvariant()
    $script:CcReplaceVersionIndexCache = $indexToSave
}

function Get-CcReplaceSha1Short {
    param([string]$Text, [int]$Length = 16)

    if ($null -eq $Text) { $Text = "" }
    if ($Length -lt 8) { $Length = 8 }

    $sha = [System.Security.Cryptography.SHA1]::Create()
    try {
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($Text)
        $hashBytes = $sha.ComputeHash($bytes)
        $hex = -join ($hashBytes | ForEach-Object { $_.ToString("x2") })
        if ($Length -gt $hex.Length) { $Length = $hex.Length }
        return $hex.Substring(0, $Length)
    }
    finally {
        $sha.Dispose()
    }
}

function Get-CcReplaceVersionKey {
    param([string]$TargetHeader, [string]$TargetPath)

    $basis = $TargetHeader
    if ($basis -eq "" -or $null -eq $basis) {
        $basis = $TargetPath
    }

    return Get-CcReplaceSha1Short $basis 16
}

function Get-CcReplaceVersionFileRecord {
    param(
        $Index,
        [string]$TargetHeader,
        [string]$TargetPath,
        [bool]$CreateIfMissing
    )

    $Index = Normalize-CcReplaceVersionIndex $Index
    $files = @($Index.Files)

    foreach ($file in $files) {
        if ([string]$file.Path -eq $TargetHeader) {
            return $file
        }
    }

    if (-not $CreateIfMissing) {
        return $null
    }

    $record = [pscustomobject]@{
        Path = $TargetHeader
        FullPath = $TargetPath
        Key = (Get-CcReplaceVersionKey $TargetHeader $TargetPath)
        CurrentVersion = 0
        Versions = @()
    }

    $files += $record
    $Index.Files = @($files)
    return $record
}

function Get-CcReplaceNextVersionNumber {
    param($Record)

    $max = 0
    foreach ($version in @($Record.Versions)) {
        if ([int]$version.Version -gt $max) {
            $max = [int]$version.Version
        }
    }

    return ($max + 1)
}

function Get-CcReplaceVersionTimestamp {
    return (Get-Date).ToUniversalTime().ToString("o")
}

function Format-CcReplaceVersionTimestamp {
    param([string]$Timestamp)

    if ($Timestamp -eq "" -or $null -eq $Timestamp) {
        return "unknown time"
    }

    $normalizedTimestamp = $Timestamp.Trim()

    try {
        return ([DateTime]::Parse($normalizedTimestamp)).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
    }
    catch {
        if ($normalizedTimestamp -match '^\d{4}-\d{2}-\d{2}$') {
            return "$normalizedTimestamp 00:00:00"
        }

        return $normalizedTimestamp
    }
}

function Get-CcReplaceTargetTextForVersionSnapshot {
    param([string]$TargetPath)

    if (-not (Test-Path -LiteralPath $TargetPath)) {
        return ""
    }

    # Use the same read cache as patch analysis/apply. This avoids a second disk
    # read when version cache is enabled and the file was already loaded.
    $read = Read-TargetLines $TargetPath
    $lines = @($read.Lines)

    if ($lines.Count -eq 0) {
        return ""
    }

    return (($lines -join $read.Newline) + $read.Newline)
}

function Save-CcReplaceSnapshotText {
    param(
        $Settings,
        $Index,
        [string]$TargetHeader,
        [string]$TargetPath,
        [string]$Text,
        [string]$Reason
    )

    if (-not [bool]$Settings.VersionCacheEnabled) {
        return $Index
    }

    if ($NoBackup) {
        return $Index
    }

    $Index = Normalize-CcReplaceVersionIndex $Index
    $record = Get-CcReplaceVersionFileRecord $Index $TargetHeader $TargetPath $true
    $versionNumber = Get-CcReplaceNextVersionNumber $record
    $timestamp = Get-CcReplaceVersionTimestamp

    $root = Get-CcReplaceVersionRoot $Settings
    $fileDir = Join-Path (Join-Path $root "files") $record.Key
    if (-not (Test-Path -LiteralPath $fileDir)) {
        New-Item -ItemType Directory -Path $fileDir -Force | Out-Null
    }

    $snapshotName = ("v{0:D6}.snapshot" -f $versionNumber)
    $snapshotPath = Join-Path $fileDir $snapshotName
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($snapshotPath, $Text, $utf8NoBom)

    $relativeSnapshot = Join-Path (Join-Path "files" $record.Key) $snapshotName
    $versions = @($record.Versions)
    $versions += [pscustomobject]@{
        Version = $versionNumber
        Timestamp = $timestamp
        Snapshot = $relativeSnapshot
        Reason = $Reason
    }

    $record.Versions = @($versions)
    $record.CurrentVersion = $versionNumber
    $record.FullPath = $TargetPath

    return $Index
}

function Save-CcReplaceCurrentFileVersion {
    param(
        $Settings,
        [string]$TargetHeader,
        [string]$TargetPath,
        [string]$Reason
    )

    if (-not [bool]$Settings.VersionCacheEnabled) { return }
    if ($NoBackup) { return }
    if (-not (Test-Path -LiteralPath $TargetPath)) { return }

    $index = Read-CcReplaceVersionIndex $Settings
    $text = Get-CcReplaceTargetTextForVersionSnapshot $TargetPath
    $index = Save-CcReplaceSnapshotText $Settings $index $TargetHeader $TargetPath $text $Reason
    Save-CcReplaceVersionIndex $Settings $index
}

function Ensure-CcReplaceVersionBaseline {
    param(
        $Settings,
        [string]$TargetHeader,
        [string]$TargetPath
    )

    if (-not [bool]$Settings.VersionCacheEnabled) { return }
    if ($NoBackup) { return }
    if (-not (Test-Path -LiteralPath $TargetPath)) { return }

    $index = Read-CcReplaceVersionIndex $Settings
    $record = Get-CcReplaceVersionFileRecord $index $TargetHeader $TargetPath $false

    if ($null -ne $record -and @($record.Versions).Count -gt 0) {
        return
    }

    $text = Get-CcReplaceTargetTextForVersionSnapshot $TargetPath
    $index = Save-CcReplaceSnapshotText $Settings $index $TargetHeader $TargetPath $text "baseline before first CC-REPLACE edit"
    Save-CcReplaceVersionIndex $Settings $index
}

function Get-CcReplaceSnapshotFullPath {
    param($Settings, $VersionRecord)

    return Join-Path (Get-CcReplaceVersionRoot $Settings) ([string]$VersionRecord.Snapshot)
}

function Invoke-CcReplaceVersionRollback {
    param($Settings, $FileRecord, $VersionRecord)

    $snapshotPath = Get-CcReplaceSnapshotFullPath $Settings $VersionRecord
    if (-not (Test-Path -LiteralPath $snapshotPath)) {
        Write-Host "Snapshot file is missing: $snapshotPath" -ForegroundColor Red
        return
    }

    $targetPath = [string]$FileRecord.FullPath
    if ($targetPath -eq "" -or $null -eq $targetPath) {
        $targetPath = Resolve-TargetPath ([string]$FileRecord.Path)
    }

    Ensure-ParentDirectory $targetPath

    $text = Read-TextFileAutoEncoding $snapshotPath
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($targetPath, $text, $utf8NoBom)

    $index = Read-CcReplaceVersionIndex $Settings
    $index = Save-CcReplaceSnapshotText $Settings $index ([string]$FileRecord.Path) $targetPath $text ("rollback to version {0}" -f [int]$VersionRecord.Version)
    Save-CcReplaceVersionIndex $Settings $index

    Write-Host "Rolled back to version $($VersionRecord.Version). New current version was created." -ForegroundColor Green
}

function Remove-CcReplaceCachedVersion {
    param($Settings, $FileRecord, $VersionRecord)

    $index = Read-CcReplaceVersionIndex $Settings
    $record = Get-CcReplaceVersionFileRecord $index ([string]$FileRecord.Path) ([string]$FileRecord.FullPath) $false
    if ($null -eq $record) { return }

    $snapshotPath = Get-CcReplaceSnapshotFullPath $Settings $VersionRecord
    if (Test-Path -LiteralPath $snapshotPath) {
        Remove-Item -LiteralPath $snapshotPath -Force
    }

    $remaining = @($record.Versions | Where-Object { [int]$_.Version -ne [int]$VersionRecord.Version })
    $record.Versions = @($remaining)

    $max = 0
    foreach ($version in $remaining) {
        if ([int]$version.Version -gt $max) { $max = [int]$version.Version }
    }
    $record.CurrentVersion = $max

    if ($remaining.Count -eq 0) {
        $index.Files = @($index.Files | Where-Object { [string]$_.Path -ne [string]$record.Path })
    }

    Save-CcReplaceVersionIndex $Settings $index
    Write-Host "Removed cached version $($VersionRecord.Version)." -ForegroundColor Yellow
}

function Show-CcReplaceVersionLogMenu {
    $settings = Read-CcReplaceSettings
    $index = Read-CcReplaceVersionIndex $settings

    while ($true) {
        $index = Normalize-CcReplaceVersionIndex $index
        $files = @($index.Files | Sort-Object Path)

        Write-Host ""
        Write-CcReplaceSectionTitle "Version cache"

        if ($files.Count -eq 0) {
            Write-CcReplaceMutedText "No cached file versions yet."
            Write-Host "Press Enter to go back." -ForegroundColor DarkGray
            [void][Console]::ReadLine()
            return
        }

        for ($i = 0; $i -lt $files.Count; $i++) {
            $file = $files[$i]
            $current = [int]$file.CurrentVersion
            $currentRecord = @($file.Versions | Where-Object { [int]$_.Version -eq $current }) | Select-Object -First 1
            $time = if ($null -ne $currentRecord) { Format-CcReplaceVersionTimestamp ([string]$currentRecord.Timestamp) } else { "unknown time" }

            Write-Host ("{0}. " -f ($i + 1)) -NoNewline
            Write-Host ([string]$file.Path) -ForegroundColor White -NoNewline
            Write-CcReplaceMutedText ("  current v{0}  {1}" -f $current, $time)
        }

        Write-Host "0. Back"
        Write-Host "Pick file: " -NoNewline
        $choice = [Console]::ReadLine()
        if ($null -eq $choice) { return }

        $choice = $choice.Trim()
        if ($choice -eq "0") { return }
        if ($choice -notmatch '^\d+$') { continue }

        $fileIndex = [int]$choice - 1
        if ($fileIndex -lt 0 -or $fileIndex -ge $files.Count) { continue }

        $selectedFile = $files[$fileIndex]
        Show-CcReplaceVersionFileMenu $settings $selectedFile
        $index = Read-CcReplaceVersionIndex $settings
    }
}

function Show-CcReplaceVersionFileMenu {
    param($Settings, $FileRecord)

    while ($true) {
        $index = Read-CcReplaceVersionIndex $Settings
        $record = Get-CcReplaceVersionFileRecord $index ([string]$FileRecord.Path) ([string]$FileRecord.FullPath) $false
        if ($null -eq $record) {
            Write-Host "Version record no longer exists." -ForegroundColor Yellow
            return
        }

        $versions = @($record.Versions | Sort-Object Version)
        Write-Host ""
        Write-Host ([string]$record.Path) -ForegroundColor White
        Write-CcReplaceMutedText ("Current Version: {0}" -f [int]$record.CurrentVersion)

        foreach ($version in $versions) {
            $time = Format-CcReplaceVersionTimestamp ([string]$version.Timestamp)
            $reason = [string]$version.Reason
            if ($reason -ne "") {
                Write-Host ("v{0}: {1}  - {2}" -f [int]$version.Version, $time, $reason)
            }
            else {
                Write-Host ("v{0}: {1}" -f [int]$version.Version, $time)
            }
        }

        Write-Host ""
        Write-Host "Pick version number, or 0 to go back: " -NoNewline
        $choice = [Console]::ReadLine()
        if ($null -eq $choice) { return }
        $choice = $choice.Trim()
        if ($choice -eq "0") { return }
        if ($choice -notmatch '^\d+$') { continue }

        $selectedVersion = @($versions | Where-Object { [int]$_.Version -eq [int]$choice }) | Select-Object -First 1
        if ($null -eq $selectedVersion) { continue }

        Write-Host ""
        Write-Host ("Selected version {0}" -f [int]$selectedVersion.Version) -ForegroundColor White
        Write-Host "1. Roll back"
        Write-Host "2. Remove"
        Write-Host "0. Back"
        Write-Host "Pick action: " -NoNewline
        $action = [Console]::ReadLine()
        if ($null -eq $action) { return }
        $action = $action.Trim()

        switch ($action) {
            "1" { Invoke-CcReplaceVersionRollback $Settings $record $selectedVersion }
            "2" { Remove-CcReplaceCachedVersion $Settings $record $selectedVersion }
            "0" { }
            default { Write-Host "Unknown action." -ForegroundColor Yellow }
        }
    }
}

function Get-CcReplacePlannedVersionAfter {
    param(
        $Settings,
        [int]$CurrentVersion,
        $EntriesForFile
    )

    $effective = @($EntriesForFile | Where-Object { $_.IsEffective })
    if ($effective.Count -eq 0) {
        return $CurrentVersion
    }

    if (-not [bool]$Settings.VersionCacheEnabled -or $NoBackup) {
        return $CurrentVersion
    }

    $createsFile = @($effective | Where-Object { $_.CreatesFile }).Count -gt 0
    if ($CurrentVersion -le 0 -and -not $createsFile) {
        return 2
    }

    return ($CurrentVersion + 1)
}

function Write-CcReplaceVersionPlan {
    param(
        [int]$CurrentVersion,
        [int]$NextVersion,
        [switch]$NoNewline
    )

    $text = ("v{0}->v{1}" -f $CurrentVersion, $NextVersion)
    Write-CcReplaceVersionAccent $text -NoNewline:$NoNewline
}

function Get-CcReplacePlanVersionBefore {
    param(
        $Entry,
        $Settings,
        [string]$TargetHeader,
        [string]$TargetPath
    )

    if ($null -ne $Entry -and
        ($Entry.PSObject.Properties.Name -contains "VersionBefore")) {
        return [int]$Entry.VersionBefore
    }

    return Get-CcReplaceFileVersionNumber $Settings $TargetHeader $TargetPath
}

function Get-CcReplacePlanVersionAfter {
    param(
        $Entry,
        $Settings,
        [int]$VersionBefore,
        $EntriesForFile
    )

    if ($null -ne $Entry -and
        ($Entry.PSObject.Properties.Name -contains "VersionAfter")) {
        return [int]$Entry.VersionAfter
    }

    return Get-CcReplacePlannedVersionAfter $Settings $VersionBefore $EntriesForFile
}

function Set-CcReplacePlanVersionHints {
    param($Plan, $Settings)

    $items = @($Plan)
    $fileEntries = @($items | Where-Object { -not $_.IsDirectory })
    $groups = @($fileEntries | Group-Object TargetHeader)

    foreach ($group in $groups) {
        $entriesForFile = @($group.Group)
        $firstEntry = $entriesForFile | Select-Object -First 1
        if ($null -eq $firstEntry) {
            continue
        }

        $versionBefore = Get-CcReplaceFileVersionNumber $Settings $group.Name ([string]$firstEntry.TargetPath)
        $versionAfter = Get-CcReplacePlannedVersionAfter $Settings $versionBefore $entriesForFile

        foreach ($entry in $entriesForFile) {
            $entry | Add-Member -NotePropertyName VersionBefore -NotePropertyValue $versionBefore -Force
            $entry | Add-Member -NotePropertyName VersionAfter -NotePropertyValue $versionAfter -Force
        }
    }

    return $items
}

function Write-CcReplaceVersionAccent {
    param(
        [string]$Text,
        [switch]$NoNewline
    )

    $esc = [char]27
    $value = "$esc[38;2;105;185;255m$Text$esc[0m"

    if ($NoNewline) {
        Write-Host $value -NoNewline
    }
    else {
        Write-Host $value
    }
}

function Get-CcReplaceFileVersionNumber {
    param(
        $Settings,
        [string]$TargetHeader,
        [string]$TargetPath
    )

    try {
        $index = Read-CcReplaceVersionIndex $Settings
        $record = Get-CcReplaceVersionFileRecord $index $TargetHeader $TargetPath $false

        if ($null -eq $record) {
            return 0
        }

        return [int]$record.CurrentVersion
    }
    catch {
        return 0
    }
}

function Write-CcReplaceFileVersionBadge {
    param([int]$Version)

    Write-CcReplaceVersionAccent ("v{0}" -f $Version) -NoNewline
}
