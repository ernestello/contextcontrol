# CC-DESC: Owns CC-REPLACE terminal UI, preflight rendering, duplicate warnings, and confirmation decisions.

function Confirm-AlreadyImplementedProceed {
    param(
        [string]$Action,
        [string]$TargetDescription
    )

    Write-CcReplaceDuplicateWarning @([pscustomobject]@{
        Action = $Action
        TargetDescription = $TargetDescription
        TargetHeader = $TargetDescription
        Name = ""
        Mode = ""
    }) $false

    $answer = Read-CcReplaceDecision "To proceed without ignorance, type:" @("still proceed")

    if ($answer -ieq "still proceed") {
        return $true
    }

    Write-Host "cancelled"
    exit 1
}

function Confirm-AlreadyImplementedProceedOrSkip {
    param(
        [string]$Action,
        [string]$TargetDescription
    )

    $proceed = Confirm-AlreadyImplementedProceed $Action $TargetDescription

    if (-not $proceed) {
        return $false
    }

    if ($DryRun) {
        Write-Host "Dry run: proceed accepted, but no write will happen."
    }

    return $true
}

function Get-CcReplaceDisplayPath {
    param([string]$PathText, $Settings)

    if ([bool]$Settings.ShowDirectoryPrefixes) {
        return $PathText
    }

    $clean = $PathText.Trim()
    $leaf = Split-Path -Leaf $clean
    if ($leaf -eq "" -or $null -eq $leaf) {
        return $clean
    }

    return $leaf
}

function Write-CcReplaceRule {
    param(
        [string]$Character = "=",
        [int]$Count = 60,
        [string]$Color = "DarkGray"
    )

    if ($Count -lt 1) {
        $Count = 1
    }

    Write-Host ($Character * $Count) -ForegroundColor $Color
}

function Write-CcReplaceEditBorder {
    param(
        [int]$EditNumber,
        [string]$Kind
    )

    $safeNumber = [Math]::Max(1, $EditNumber)
    $safeKind = "START"
    if ($Kind -ne "") {
        $safeKind = $Kind.ToUpperInvariant()
    }

    $esc = [char]27
    $reset = "$esc[0m"

    # Heavy phase wrapper. The label is centered into the same dark-yellow
    # block line so one full ccReplace invocation reads as one edit phase.
    $barColor = "184;134;11"
    $barAnsi = "$esc[38;2;$barColor" + "m"
    $labelAnsi = "$esc[1;30;48;2;$barColor" + "m"

    $labelText = "[EDIT $safeNumber - $safeKind]"
    $totalWidth = 76
    $remaining = [Math]::Max(4, $totalWidth - $labelText.Length)
    $leftCount = [Math]::Max(2, [int][Math]::Floor($remaining / 2.0))
    $rightCount = [Math]::Max(2, $totalWidth - $labelText.Length - $leftCount)

    $line =
        $barAnsi + ("█" * $leftCount) +
        $labelAnsi + $labelText + $reset +
        $barAnsi + ("█" * $rightCount) +
        $reset

    Write-Host $line
}

function Write-CcReplaceSectionTitle {
    param(
        [string]$Title,
        [string]$Color = "Magenta",
        [string]$BorderColor = "DarkMagenta"
    )

    $width = 76
    $safeTitle = ""
    if ($null -ne $Title) {
        $safeTitle = [string]$Title
    }

    $visibleLength = $safeTitle.Length
    $leftPad = [Math]::Max(0, [int][Math]::Floor(($width - $visibleLength) / 2.0))
    $rightPad = [Math]::Max(0, $width - $visibleLength - $leftPad)
    $titleLine = (" " * $leftPad) + $safeTitle + (" " * $rightPad)

    Write-CcReplaceRule "=" $width $BorderColor

    $esc = [char]27
    $reset = "$esc[0m"

    # Near-black indigo gives the title row a subtle "screen panel" texture
    # without crushing readability of the bright status colors around it.
    $fg = "220;120;255"
    $bg = "11;7;18"

    if ($Color -eq "Red") {
        $fg = "255;80;80"
        $bg = "24;6;8"
    }
    elseif ($Color -eq "Yellow") {
        $fg = "255;220;80"
        $bg = "24;18;6"
    }
    elseif ($Color -eq "Cyan") {
        $fg = "80;220;255"
        $bg = "5;18;24"
    }
    elseif ($Color -eq "White") {
        $fg = "240;245;250"
        $bg = "10;12;16"
    }

    $ansi = "$esc[1;38;2;$fg;48;2;$bg" + "m"
    Write-Host ($ansi + $titleLine + $reset)
    Write-CcReplaceRule "=" $width $BorderColor
}

function Write-CcReplaceLabel {
    param(
        [string]$Text,
        [string]$Color = "Magenta"
    )

    Write-Host $Text -ForegroundColor $Color
}

function Write-CcReplaceMutedText {
    param(
        [string]$Text,
        [switch]$NoNewline
    )

    $esc = [char]27
    $value = "$esc[38;2;190;196;202m$Text$esc[0m"

    if ($NoNewline) {
        Write-Host $value -NoNewline
    }
    else {
        Write-Host $value
    }
}

function Write-CcReplaceSummaryText {
    param(
        [string]$Text,
        [switch]$NoNewline
    )

    $esc = [char]27
    $value = "$esc[38;2;232;236;240m$Text$esc[0m"

    if ($NoNewline) {
        Write-Host $value -NoNewline
    }
    else {
        Write-Host $value
    }
}

function Write-CcReplaceInfoAccent {
    param(
        [string]$Text,
        [switch]$NoNewline
    )

    # Important numeric/UI accent: warm orange, used for LOC and mode summaries.
    $esc = [char]27
    $value = "$esc[38;2;255;176;80m$Text$esc[0m"

    if ($NoNewline) {
        Write-Host $value -NoNewline
    }
    else {
        Write-Host $value
    }
}

function Write-CcReplaceBucketToken {
    param(
        [string]$Label,
        [int]$Count,
        [string]$Color,
        [switch]$NoLeadingGap
    )

    if (-not $NoLeadingGap) {
        Write-CcReplaceSummaryText "    " -NoNewline
    }

    Write-Host ("{0}: " -f $Label) -ForegroundColor $Color -NoNewline
    Write-Host $Count -ForegroundColor $Color -NoNewline
}

function Get-CcReplaceBucketCounts {
    param($Entries)

    $items = @($Entries)
    $created = 0
    $changed = 0
    $removed = 0
    $duplicates = 0

    foreach ($entry in $items) {
        $bucket = Get-CcReplaceActionBucket $entry
        switch ($bucket) {
            "created" { $created++ }
            "changed" { $changed++ }
            "removed" { $removed++ }
            "duplicate" { $duplicates++ }
        }
    }

    return [pscustomobject]@{
        Created = $created
        Changed = $changed
        Removed = $removed
        Duplicate = $duplicates
    }
}

function Write-CcReplaceFileBucketStats {
    param($Entries)

    $counts = Get-CcReplaceBucketCounts $Entries

    Write-CcReplaceMutedText "    " -NoNewline
    Write-Host "CR: " -ForegroundColor DarkGreen -NoNewline
    Write-Host $counts.Created -ForegroundColor Green -NoNewline
    Write-CcReplaceMutedText "  " -NoNewline
    Write-Host "CH: " -ForegroundColor DarkCyan -NoNewline
    Write-Host $counts.Changed -ForegroundColor Cyan -NoNewline
    Write-CcReplaceMutedText "  " -NoNewline
    Write-Host "RM: " -ForegroundColor DarkRed -NoNewline
    Write-Host $counts.Removed -ForegroundColor Red -NoNewline
    Write-CcReplaceMutedText "  " -NoNewline
    Write-Host "DUP: " -ForegroundColor DarkRed -NoNewline
    Write-Host $counts.Duplicate -ForegroundColor Red -NoNewline
}

function Write-CcReplaceActionPromptSection {
    Write-Host ""

    $width = 76
    $title = "  >>> ACTIONS"
    $titleLine = $title + (" " * [Math]::Max(0, $width - $title.Length))

    Write-CcReplaceRule "=" $width "DarkMagenta"

    $esc = [char]27
    $reset = "$esc[0m"

    # Keep the action phase in the same framed UI language as the other
    # sections, but make it left-aligned and white so it reads as the actual
    # decision area instead of another centered information title.
    $fg = "245;245;245"
    $bg = "11;7;18"
    $ansi = "$esc[1;38;2;$fg;48;2;$bg" + "m"

    Write-Host ($ansi + $titleLine + $reset)
    Write-CcReplaceRule "=" $width "DarkMagenta"
}

function Start-CcReplaceEditPhase {
    if ($null -eq $script:CcReplaceEditPhaseNumber) {
        $script:CcReplaceEditPhaseNumber = 0
    }

    $script:CcReplaceEditPhaseNumber++

    Write-Host ""
    Write-CcReplaceEditBorder $script:CcReplaceEditPhaseNumber "START"
}

function Stop-CcReplaceEditPhase {
    $editNumber = 1
    if ($null -ne $script:CcReplaceEditPhaseNumber) {
        $editNumber = [int]$script:CcReplaceEditPhaseNumber
    }

    Write-CcReplaceEditBorder $editNumber "END"
}

function Write-CcReplaceLoc {
    param([int]$Loc)

    Write-Host "$Loc LOC" -ForegroundColor White -NoNewline
}

function Write-CcReplaceDelta {
    param([int]$Added, [int]$Removed)

    Write-Host "+$Added" -ForegroundColor Green -NoNewline
    Write-Host " " -NoNewline
    Write-Host "-$Removed" -ForegroundColor Red -NoNewline
}

function Write-CcReplaceActionSummary {
    param($Entries, $Settings, [string]$Title)

    $items = @($Entries)
    if ($items.Count -eq 0) {
        return
    }

    Write-CcReplaceCompactPlan $items @() $Settings $Title
}

function Write-CcReplaceEffectiveIntro {
    param($EffectiveEntries, $DuplicateEntries, $Settings)

    $effective = @($EffectiveEntries)
    $duplicates = @($DuplicateEntries)

    Write-Host ""
    Write-Host "Effective Edits:`t $($effective.Count)    Duplicates (if present): $($duplicates.Count)"

    if ($effective.Count -eq 0) {
        return
    }

    Write-Host ""
    Write-Host "You are now going to effectively edit:"
    foreach ($entry in $effective | Where-Object { -not $_.IsDirectory }) {
        $display = Get-CcReplaceDisplayPath $entry.TargetHeader $Settings
        Write-Host "$display " -NoNewline
        Write-CcReplaceDelta ([int]$entry.Added) ([int]$entry.Removed)
        Write-Host ""
    }

    $groups = @($effective | Where-Object { -not $_.IsDirectory } | Group-Object TargetHeader)
    foreach ($group in $groups) {
        $display = Get-CcReplaceDisplayPath $group.Name $Settings
        Write-Host ""
        Write-Host $display
        Write-Host "changing:"
        foreach ($entry in $group.Group | Where-Object { $_.Removed -gt 0 -and $_.Added -gt 0 -and -not $_.CreatesFile }) {
            Write-Host "  $($entry.PartLabel) (" -NoNewline
            Write-CcReplaceDelta ([int]$entry.Added) ([int]$entry.Removed)
            Write-Host ")"
        }
        Write-Host "creating:"
        foreach ($entry in $group.Group | Where-Object { $_.Added -gt 0 -and ($_.Removed -eq 0 -or $_.CreatesFile) }) {
            Write-Host "  $($entry.PartLabel) (" -NoNewline
            Write-CcReplaceDelta ([int]$entry.Added) 0
            Write-Host ")"
        }
        Write-Host "removing:"
        foreach ($entry in $group.Group | Where-Object { $_.Removed -gt 0 -and $_.Added -eq 0 }) {
            Write-Host "  $($entry.PartLabel) (" -NoNewline
            Write-CcReplaceDelta 0 ([int]$entry.Removed)
            Write-Host ")"
        }
    }
}

function Write-CcReplaceModeCountsInline {
    param($Entries, [string]$Label, [string]$Color)

    $items = @($Entries)
    if ($items.Count -eq 0) {
        return
    }

    $modes = @("function", "insert_after_function", "insert_before_function", "delete_function", "replace_region", "append_to_file", "whole_file", "create_directory", "insert_include")
    $parts = New-Object System.Collections.Generic.List[string]

    foreach ($mode in $modes) {
        $count = @($items | Where-Object { $_.Mode -eq $mode }).Count
        if ($count -gt 0) {
            $parts.Add(("{0}={1}" -f $mode, $count))
        }
    }

    Write-CcReplaceSummaryText "$Label`: " -NoNewline
    Write-CcReplaceInfoAccent ($parts -join ", ")
}

function Get-CcReplaceActionKind {
    param($Entry)

    if ([bool]$Entry.IsDuplicate) {
        return "DUP"
    }

    if ([bool]$Entry.IsDirectory) {
        if ([bool]$Entry.CreatesDirectory) {
            return "CREATE DIR"
        }
        return "CHANGE DIR"
    }

    if ([bool]$Entry.CreatesFile) {
        return "CREATE FILE"
    }

    switch ($Entry.Mode) {
        "delete_function" { return "REMOVE" }
        "insert_after_function" { return "CREATE" }
        "insert_before_function" { return "CREATE" }
        "append_to_file" { return "CREATE" }
        "insert_include" { return "CREATE" }
        default { return "CHANGE" }
    }
}

function Get-CcReplaceActionBucket {
    param($Entry)

    $kind = Get-CcReplaceActionKind $Entry

    if ($kind -eq "DUP") { return "duplicate" }
    if ($kind.StartsWith("CREATE")) { return "created" }
    if ($kind.StartsWith("REMOVE")) { return "removed" }
    return "changed"
}

function Get-CcReplaceKindColor {
    param([string]$Kind)

    if ($Kind.StartsWith("DUP")) { return "Red" }
    if ($Kind.StartsWith("CREATE")) { return "Green" }
    if ($Kind.StartsWith("REMOVE")) { return "Red" }
    return "Cyan"
}

function Write-CcReplaceKindTag {
    param([string]$Kind)

    $color = Get-CcReplaceKindColor $Kind
    Write-Host "[" -NoNewline
    Write-Host $Kind -ForegroundColor $color -NoNewline
    Write-Host "]" -NoNewline
}

function Write-CcReplaceBucketSummary {
    param($Entries)

    $counts = Get-CcReplaceBucketCounts $Entries

    Write-CcReplaceBucketToken "created" $counts.Created "Green" -NoLeadingGap
    Write-CcReplaceBucketToken "changed" $counts.Changed "Cyan"
    Write-CcReplaceBucketToken "removed" $counts.Removed "Red"
    Write-CcReplaceBucketToken "duplicate" $counts.Duplicate "Red"
    Write-Host ""
}

function Write-CcReplaceAggregateBadge {
    param(
        [string]$Kind,
        [int]$Count,
        [int]$Added,
        [int]$Removed,
        [bool]$WouldRepeat = $false
    )

    if ($Count -le 0) {
        return
    }

    Write-Host "  " -NoNewline
    Write-CcReplaceKindTag ("{0} {1}" -f $Kind, $Count)
    if ($WouldRepeat) {
        Write-Host " would repeat " -ForegroundColor Yellow -NoNewline
    }
    else {
        Write-Host " " -NoNewline
    }
    Write-CcReplaceDelta $Added $Removed
}

function Write-CcReplacePlanRow {
    param($Entry, $Settings)

    $kind = Get-CcReplaceActionKind $Entry
    $label = $Entry.PartLabel
    if ($null -eq $label -or $label -eq "") {
        $label = $Entry.TargetHeader
    }

    Write-CcReplaceMutedText "    " -NoNewline
    Write-CcReplaceKindTag $kind
    Write-CcReplaceMutedText " $label (" -NoNewline
    Write-CcReplaceDelta ([int]$Entry.Added) ([int]$Entry.Removed)
    Write-CcReplaceMutedText ")"
}

function Write-CcReplaceCompactPlan {
    param(
        $EffectiveEntries,
        $DuplicateEntries,
        $Settings,
        [string]$Title = "Preflight patch plan"
    )

    $effective = @($EffectiveEntries)
    $duplicates = @($DuplicateEntries)
    $allEntries = @($effective + $duplicates)

    $fileEntries = @($allEntries | Where-Object { -not $_.IsDirectory })
    $effectiveFileEntries = @($effective | Where-Object { -not $_.IsDirectory })
    $fileGroups = @($fileEntries | Group-Object TargetHeader)

    $filesAffected = $fileGroups.Count

    $addedAffected = 0
    $removedAffected = 0
    foreach ($entry in $effectiveFileEntries) {
        $addedAffected += [int]$entry.Added
        $removedAffected += [int]$entry.Removed
    }
    $locAffected = $addedAffected + $removedAffected

    Write-CcReplaceSectionTitle $Title

    Write-CcReplaceSummaryText "Total LOC stats: " -NoNewline
    Write-CcReplaceDelta $addedAffected $removedAffected
    Write-CcReplaceMutedText " | " -NoNewline
    Write-CcReplaceInfoAccent ("{0} total affected LOC" -f $locAffected)
    Write-CcReplaceSummaryText "Total Files Affected: " -NoNewline
    Write-CcReplaceInfoAccent ([string]$filesAffected)

    $fileIndex = 1
    foreach ($group in $fileGroups) {
        $entriesForFile = @($group.Group)
        $effectiveForFile = @($effective | Where-Object { -not $_.IsDirectory -and $_.TargetHeader -eq $group.Name })

        $fileAdded = 0
        $fileRemoved = 0
        $locAfter = 0
        foreach ($entry in $effectiveForFile) {
            $fileAdded += [int]$entry.Added
            $fileRemoved += [int]$entry.Removed
        }
        foreach ($entry in $entriesForFile) {
            if ([int]$entry.TotalLocAfter -gt 0) {
                $locAfter = [int]$entry.TotalLocAfter
            }
        }

        $display = Get-CcReplaceDisplayPath $group.Name $Settings
        $firstEntry = $entriesForFile | Select-Object -First 1
        $versionNumber = 0
        if ($null -ne $firstEntry) {
            $versionNumber = Get-CcReplaceFileVersionNumber $Settings $group.Name ([string]$firstEntry.TargetPath)
        }
        $nextVersion = Get-CcReplacePlannedVersionAfter $Settings $versionNumber $entriesForFile

        Write-Host ("{0}." -f $fileIndex) -ForegroundColor Yellow -NoNewline
        Write-CcReplaceMutedText " " -NoNewline
        Write-Host $display -ForegroundColor White -NoNewline
        Write-CcReplaceMutedText " " -NoNewline
        Write-CcReplaceVersionPlan $versionNumber $nextVersion -NoNewline
        Write-CcReplaceMutedText " " -NoNewline
        Write-CcReplaceDelta $fileAdded $fileRemoved
        Write-CcReplaceMutedText " | " -NoNewline
        Write-CcReplaceLoc $locAfter
        Write-Host ""

        $fileIndex++
    }

    Write-Host ""

    Write-CcReplaceSummaryText "Effective actions: " -NoNewline
    Write-Host $effective.Count -ForegroundColor Green -NoNewline
    Write-CcReplaceSummaryText "    Duplicate actions: " -NoNewline
    Write-Host $duplicates.Count -ForegroundColor Red

    Write-CcReplaceBucketSummary $allEntries

    Write-Host ""
    Write-CcReplaceModeCountsInline $effective "effective modes" "DarkGray"
    Write-CcReplaceModeCountsInline $duplicates "duplicates" "DarkGray"

    if ($fileEntries.Count -gt 0) {
        Write-CcReplaceSectionTitle "Implementation Plan"

        $fileIndex = 1
        $firstFile = $true
        foreach ($group in $fileGroups) {
            if (-not $firstFile) {
                Write-Host ""
            }
            $firstFile = $false

            $entriesForFile = @($group.Group)
            $effectiveForFile = @($effective | Where-Object { -not $_.IsDirectory -and $_.TargetHeader -eq $group.Name })

            $fileAdded = 0
            $fileRemoved = 0
            $locAfter = 0
            foreach ($entry in $effectiveForFile) {
                $fileAdded += [int]$entry.Added
                $fileRemoved += [int]$entry.Removed
            }
            foreach ($entry in $entriesForFile) {
                if ([int]$entry.TotalLocAfter -gt 0) {
                    $locAfter = [int]$entry.TotalLocAfter
                }
            }

            $display = Get-CcReplaceDisplayPath $group.Name $Settings
            $firstEntry = $entriesForFile | Select-Object -First 1
            $versionNumber = 0
            if ($null -ne $firstEntry) {
                $versionNumber = Get-CcReplaceFileVersionNumber $Settings $group.Name ([string]$firstEntry.TargetPath)
            }
            $nextVersion = Get-CcReplacePlannedVersionAfter $Settings $versionNumber $entriesForFile

            Write-Host ("{0}." -f $fileIndex) -ForegroundColor Yellow -NoNewline
            Write-CcReplaceMutedText " " -NoNewline
            Write-Host $display -ForegroundColor White -NoNewline
            Write-CcReplaceMutedText " " -NoNewline
            Write-CcReplaceVersionPlan $versionNumber $nextVersion -NoNewline
            Write-CcReplaceMutedText " " -NoNewline
            Write-CcReplaceDelta $fileAdded $fileRemoved
            Write-CcReplaceMutedText " | " -NoNewline
            Write-CcReplaceLoc $locAfter
            Write-Host ""

            $orderedRows = @(
                $entriesForFile | Sort-Object `
                    @{ Expression = {
                        $bucket = Get-CcReplaceActionBucket $_
                        switch ($bucket) {
                            "created" { 0 }
                            "changed" { 1 }
                            "removed" { 2 }
                            "duplicate" { 3 }
                            default { 9 }
                        }
                    } },
                    @{ Expression = { $_.PartLabel } }
            )

            foreach ($entry in $orderedRows) {
                Write-CcReplacePlanRow $entry $Settings
            }

            $fileIndex++
        }
    }

    $directoryEntries = @($allEntries | Where-Object { $_.IsDirectory })
    if ($directoryEntries.Count -gt 0) {
        Write-CcReplaceSectionTitle "Directories"

        foreach ($entry in $directoryEntries) {
            $kind = Get-CcReplaceActionKind $entry
            $display = Get-CcReplaceDisplayPath $entry.TargetHeader $Settings
            Write-CcReplaceMutedText "  " -NoNewline
            Write-CcReplaceKindTag $kind
            Write-CcReplaceMutedText " " -NoNewline
            Write-Host $display -ForegroundColor White
        }
    }
}

function Write-CcReplaceDuplicateWarning {
    param($DuplicateEntries, [bool]$MixedWithEffective)

    $duplicates = @($DuplicateEntries)
    if ($duplicates.Count -eq 0) {
        return
    }

    Write-Host ""
    Write-CcReplaceSectionTitle "WARNING! (NOT RECOMMENDED)" "Red" "DarkRed"

    Write-CcReplaceSummaryText "You have " -NoNewline
    Write-Host $duplicates.Count -ForegroundColor Red -NoNewline
    Write-CcReplaceSummaryText " duplicate actions:"
    Write-Host ""

    $groups = $duplicates | Group-Object TargetHeader
    $fileIndex = 1
    foreach ($group in $groups) {
        Write-Host ("{0}." -f $fileIndex) -ForegroundColor Yellow -NoNewline
        Write-CcReplaceMutedText " " -NoNewline
        Write-Host $group.Name -ForegroundColor White

        foreach ($entry in $group.Group) {
            if ($entry.Name -ne "") {
                Write-Host ("  > {0}:" -f $entry.Name) -ForegroundColor DarkCyan
            }
            else {
                Write-Host ("  > {0}:" -f $entry.PartLabel) -ForegroundColor DarkCyan
            }

            Write-CcReplaceMutedText "    action: " -NoNewline
            Write-Host $entry.DuplicateReason -ForegroundColor Yellow
            Write-CcReplaceMutedText "    target: " -NoNewline
            Write-Host $entry.TargetDescription -ForegroundColor White
        }

        Write-Host ""
        $fileIndex++
    }

    Write-CcReplaceActionPromptSection

    if ($MixedWithEffective) {
        Write-Host "1.  To proceed with caution, ignoring duplicates, type:" -ForegroundColor Cyan
        Write-Host "    1 or Y" -ForegroundColor Yellow
        Write-Host ""
        Write-Host "F.  To proceed without ignorance, applying duplicates, type:" -ForegroundColor Cyan
        Write-Host "    F or still proceed" -ForegroundColor Yellow
        Write-CcPipelineActionHints
    }
    else {
        Write-Host "F.  To proceed without ignorance, type:" -ForegroundColor Cyan
        Write-Host "    F or still proceed" -ForegroundColor Yellow
        Write-CcPipelineActionHints
    }
}

function Read-CcReplaceDecision {
    param(
        [string]$Prompt,
        [string[]]$AllowedDescriptions
    )

    foreach ($line in $AllowedDescriptions) {
        if ($line -ne "") {
            Write-Host $line
        }
    }

    while ($true) {
        Write-Host ""
        $answer = [Console]::ReadLine()
        if ($null -eq $answer) {
            return ""
        }

        $clean = $answer.Trim()
        if ($clean -ne "") {
            return $clean
        }

        $settings = Read-CcReplaceSettings
        if (-not [bool]$settings.RepeatInvalidInput) {
            return ""
        }

        Write-Host "Invalid input. Try again." -ForegroundColor Yellow
    }
}

function Write-CcPipelineActionHints {
    Write-Host ""
    Write-Host "Pipeline helpers:" -ForegroundColor DarkCyan
    Write-Host "DIR. Run ccDir.ps1 to export the project tree without leaving this screen." -ForegroundColor Cyan
    Write-Host "CC.  Run cc.ps1 to export selected files/functions without leaving this screen." -ForegroundColor Cyan
    Write-Host "GO.  Paste CC-REPLACE blocks and apply them with the normal preflight confirmation." -ForegroundColor Cyan
}

function Get-CcReplacePreflightDecision {
    param($Plan, $Settings)

    $effective = @($Plan | Where-Object { $_.IsEffective })
    $duplicates = @($Plan | Where-Object { $_.IsDuplicate })
    $hasEffective = $effective.Count -gt 0
    $hasDuplicates = $duplicates.Count -gt 0

    if ([bool]$Settings.ShowPreflightStatistics) {
        Write-CcReplaceCompactPlan $effective $duplicates $Settings
    }

    if (-not [bool]$Settings.ConfirmationStage) {
        if ($hasDuplicates) {
            Write-Host ""
            Write-Host "Confirmation stage is OFF: duplicate actions will be ignored." -ForegroundColor Yellow
        }
        return "effective_only"
    }

    if ($hasDuplicates) {
        Write-CcReplaceDuplicateWarning $duplicates $hasEffective
    }

    if ($hasEffective -and -not $hasDuplicates) {
        Write-CcReplaceActionPromptSection
        Write-Host "1.  To proceed, type:" -ForegroundColor Cyan
        Write-Host "    1 or Y" -ForegroundColor Yellow
        Write-CcPipelineActionHints
    }

    while ($true) {
        Write-Host ""
        Write-Host "Your Choice: " -ForegroundColor Cyan -NoNewline
        $answer = [Console]::ReadLine()
        if ($null -eq $answer) { $answer = "" }
        $clean = $answer.Trim()

        if (Invoke-CcPipelineCommand $clean) {
            Write-CcReplaceActionPromptSection
            if ($hasDuplicates -and $hasEffective) {
                Write-Host "Back at patch decision. Type 1/Y to apply effective edits, F/still proceed to include duplicates, or use DIR/CC again." -ForegroundColor DarkGray
            }
            elseif ($hasDuplicates) {
                Write-Host "Back at patch decision. Type F/still proceed to apply duplicates, or use DIR/CC again." -ForegroundColor DarkGray
            }
            else {
                Write-Host "Back at patch decision. Type 1/Y to apply, or use DIR/CC again." -ForegroundColor DarkGray
            }
            continue
        }

        if ($hasDuplicates -and ($clean -ieq "still proceed" -or $clean -ieq "F")) {
            return "all"
        }

        if ($hasEffective -and ($clean -eq "1" -or $clean -match '^[Yy]')) {
            return "effective_only"
        }

        if (-not [bool]$Settings.RepeatInvalidInput) {
            Write-Host "cancelled"
            return "cancel"
        }

        if ($hasDuplicates -and $hasEffective) {
            Write-Host "Invalid input. Type 1/Y to apply effective edits, F/still proceed to apply duplicates too, or Ctrl+C to stop." -ForegroundColor Yellow
        }
        elseif ($hasDuplicates) {
            Write-Host "Invalid input. Type F or still proceed, or Ctrl+C to stop." -ForegroundColor Yellow
        }
        else {
            Write-Host "Invalid input. Type 1 or Y, or Ctrl+C to stop." -ForegroundColor Yellow
        }
    }
}
