# CC-DESC: Owns CC-REPLACE patch analysis and plan entry construction.

function Get-CcReplaceCodeLineCount {
    param([string]$Code)

    return @(Get-TrimmedCodeLineArray $Code).Count
}

function New-CcReplacePlanEntry {
    param(
        $Block,
        [string]$Mode,
        [string]$TargetHeader,
        [string]$TargetPath,
        [string]$Name,
        [string]$ActionLabel,
        [string]$PartLabel,
        [bool]$IsDirectory,
        [bool]$IsDuplicate,
        [string]$DuplicateAction,
        [string]$DuplicateTarget,
        [int]$Added,
        [int]$Removed,
        [int]$TotalLocAfter,
        $NewLines,
        [string]$Newline,
        [bool]$SkipWrite,
        [bool]$CreatesFile,
        [bool]$CreatesDirectory
    )

    return [pscustomobject]@{
        Block = $Block
        Mode = $Mode
        TargetHeader = $TargetHeader
        TargetPath = $TargetPath
        Name = $Name
        ActionLabel = $ActionLabel
        PartLabel = $PartLabel
        IsDirectory = $IsDirectory
        IsDuplicate = $IsDuplicate
        IsEffective = (-not $IsDuplicate)
        DuplicateAction = $DuplicateAction
        DuplicateTarget = $DuplicateTarget
        Added = $Added
        Removed = $Removed
        TotalLocAfter = $TotalLocAfter
        NewLines = $NewLines
        Newline = $Newline
        SkipWrite = $SkipWrite
        CreatesFile = $CreatesFile
        CreatesDirectory = $CreatesDirectory
    }
}

function Analyze-CcReplaceBlock {
    param($Block)

    $headers = $Block.Headers

    $mode = "function"
    if ($headers.ContainsKey("MODE")) {
        $mode = $headers["MODE"].ToLowerInvariant()
    }

    $targetHeader = Get-TargetHeader $headers
    $targetPath = Resolve-TargetPath $targetHeader

    $name = ""
    if ($headers.ContainsKey("NAME")) {
        $name = $headers["NAME"]
    }

    if ($mode -eq "create_directory") {
        $exists = Test-Path -LiteralPath $targetPath
        $dupAction = if ($exists) { "Directory already exists" } else { "" }
        return New-CcReplacePlanEntry $Block $mode $targetHeader $targetPath $name "create_directory" $targetHeader $true $exists $dupAction $targetHeader 0 0 0 $null "`n" $false $false (-not $exists)
    }

    if ($mode -ne "whole_file" -and -not (Test-Path -LiteralPath $targetPath)) {
        throw "Target file not found: $targetPath"
    }

    $lines = @()
    $newline = "`n"
    $fileExists = Test-Path -LiteralPath $targetPath

    if ($fileExists) {
        $read = Read-TargetLines $targetPath
        $lines = @(ConvertTo-LineArray $read.Lines)
        $newline = $read.Newline
    }

    switch ($mode) {
        "function" {
            if ($name -eq "") { throw "MODE:function requires NAME." }
            $range = Find-FunctionRange $targetPath $lines $name
            Assert-OptionalHashMatches $headers $lines $range.Start $range.End "$targetHeader :: $name"
            $isDup = Test-CodeRegionMatches $lines $range.Start $range.End $Block.Code
            $newLines = Replace-LineRange $lines $range.Start $range.End $Block.Code
            $added = Get-CcReplaceCodeLineCount $Block.Code
            $removed = $range.End - $range.Start
            return New-CcReplacePlanEntry $Block $mode $targetHeader $targetPath $name "function" $name $false $isDup "Function body already matches patch" "$targetHeader :: $name" $added $removed @($newLines).Count $newLines $newline $false $false $false
        }

        "insert_after_function" {
            if ($name -eq "") { throw "MODE:insert_after_function requires NAME." }
            $range = Find-FunctionRange $targetPath $lines $name
            $isDup = Test-CodeBlockAlreadyInLines $lines $Block.Code
            $newLines = Insert-LinesAt $lines $range.End $Block.Code
            $added = Get-CcReplaceCodeLineCount $Block.Code
            return New-CcReplacePlanEntry $Block $mode $targetHeader $targetPath $name "insert_after_function" "after $name" $false $isDup "Inserted code block already exists in file" "$targetHeader :: after $name" $added 0 @($newLines).Count $newLines $newline $false $false $false
        }

        "insert_before_function" {
            if ($name -eq "") { throw "MODE:insert_before_function requires NAME." }
            $range = Find-FunctionRange $targetPath $lines $name
            $isDup = Test-CodeBlockAlreadyInLines $lines $Block.Code
            $newLines = Insert-LinesAt $lines $range.Start $Block.Code
            $added = Get-CcReplaceCodeLineCount $Block.Code
            return New-CcReplacePlanEntry $Block $mode $targetHeader $targetPath $name "insert_before_function" "before $name" $false $isDup "Inserted code block already exists in file" "$targetHeader :: before $name" $added 0 @($newLines).Count $newLines $newline $false $false $false
        }

        "delete_function" {
            if ($name -eq "") { throw "MODE:delete_function requires NAME." }
            $range = Find-FunctionRange $targetPath $lines $name
            $newLines = Replace-LineRange $lines $range.Start $range.End ""
            $removed = $range.End - $range.Start
            return New-CcReplacePlanEntry $Block $mode $targetHeader $targetPath $name "delete_function" $name $false $false "" "" 0 $removed @($newLines).Count $newLines $newline $false $false $false
        }

        "replace_region" {
            if ($name -eq "") { throw "MODE:replace_region requires NAME." }
            $range = Find-MarkerRange $lines $name
            Assert-OptionalHashMatches $headers $lines $range.Start $range.End "$targetHeader :: $name"
            $isDup = Test-CodeRegionMatches $lines $range.Start $range.End $Block.Code
            $newLines = Replace-LineRange $lines $range.Start $range.End $Block.Code
            $added = Get-CcReplaceCodeLineCount $Block.Code
            $removed = $range.End - $range.Start
            return New-CcReplacePlanEntry $Block $mode $targetHeader $targetPath $name "replace_region" $name $false $isDup "Marker region already matches patch" "$targetHeader :: $name" $added $removed @($newLines).Count $newLines $newline $false $false $false
        }

        "insert_include" {
            $header = Get-IncludeHeaderValue $headers
            $includeResult = Insert-IncludeLine $lines $header
            $newLines = $includeResult.Lines
            $isDup = -not [bool]$includeResult.Changed
            $added = if ($isDup) { 0 } else { 1 }
            return New-CcReplacePlanEntry $Block $mode $targetHeader $targetPath $name "insert_include" "#include $header" $false $isDup "Include already exists" "$targetHeader :: #include $header" $added 0 @($newLines).Count $newLines $newline $isDup $false $false
        }

        "append_to_file" {
            $isDup = Test-CodeBlockAlreadyInLines $lines $Block.Code
            $newLines = Insert-LinesAt $lines $lines.Count $Block.Code
            $added = Get-CcReplaceCodeLineCount $Block.Code
            return New-CcReplacePlanEntry $Block $mode $targetHeader $targetPath $name "append_to_file" $targetHeader $false $isDup "Append block already exists in file" $targetHeader $added 0 @($newLines).Count $newLines $newline $false $false $false
        }

        "whole_file" {
            $trimmed = Trim-CodeBlankEdges $Block.Code
            if ($trimmed -eq "") {
                $newLines = @()
            }
            else {
                $newLines = @(ConvertTo-LineArray $trimmed)
            }

            $isDup = ($fileExists -and (Test-LineArraysEqual $lines $newLines))
            $added = @($newLines).Count
            $removed = if ($fileExists) { @($lines).Count } else { 0 }
            $action = if ($fileExists) { "whole_file" } else { "create_file" }
            $dupAction = if ($isDup) { "Whole file already matches patch" } else { "" }
            return New-CcReplacePlanEntry $Block $mode $targetHeader $targetPath $name $action $targetHeader $false $isDup $dupAction $targetHeader $added $removed @($newLines).Count $newLines $newline $false (-not $fileExists) $false
        }

        default {
            throw "Unknown MODE: $mode"
        }
    }
}

function Analyze-CcReplaceBlocks {
    param($Blocks)

    $plan = New-Object System.Collections.Generic.List[object]

    foreach ($block in @($Blocks)) {
        $plan.Add((Analyze-CcReplaceBlock $block))
    }

    return @($plan.ToArray())
}
