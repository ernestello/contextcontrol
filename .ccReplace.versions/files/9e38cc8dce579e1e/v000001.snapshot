# CC-DESC: Source-export configuration, path filtering, display-path, and input-line helpers.

# Files that may be explicitly exported if the user asks for them.
$script:TextExtensions = @(
    ".h", ".hh", ".hpp", ".hxx", ".inl",
    ".c", ".cc", ".cpp", ".cxx", ".ixx",
    ".cmake", ".txt", ".md", ".json", ".ini", ".cfg", ".toml", ".yaml", ".yml",
    ".ps1", ".bat", ".cmd",
    ".glsl", ".vert", ".frag", ".comp", ".geom", ".tesc", ".tese", ".mesh", ".task", ".shader",
    ".gd", ".gdshader", ".tscn", ".tres",
    ".cs", ".ts", ".js", ".html", ".css"
)

# Files searched by FUNC:/FIND:.
# Intentional: excludes .md and normal .txt so generated Context Control exports do not recursively match.
$script:CodeSearchExtensions = @(
    ".h", ".hh", ".hpp", ".hxx", ".inl",
    ".c", ".cc", ".cpp", ".cxx", ".ixx",
    ".cmake",
    ".json", ".ini", ".cfg", ".toml", ".yaml", ".yml",
    ".ps1", ".bat", ".cmd",
    ".glsl", ".vert", ".frag", ".comp", ".geom", ".tesc", ".tese", ".mesh", ".task", ".shader",
    ".gd", ".gdshader", ".tscn", ".tres",
    ".cs", ".ts", ".js", ".html", ".css"
)

$script:ExcludeDirs = @(
    ".git", ".vs", ".vscode", ".idea", ".cache",
    ".project", ".import",
    "node_modules", "dist",
    "build", "build-debug", "build-release",
    "cmake-build-debug", "cmake-build-release",
    "CMakeFiles", "out", "bin", "obj", "x64",
    "Debug", "Release", "RelWithDebInfo", "MinSizeRel",
    "vcpkg_installed", "packages", "PackageCache",
    "external", "extern", "third_party", "thirdparty", "vendor", "deps", "dependencies",
    "__pycache__"
)

$script:BinaryExtensions = @(
    ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".tga", ".dds",
    ".wav", ".mp3", ".ogg", ".flac",
    ".bin", ".collision", ".svo", ".spv",
    ".pdb", ".ilk", ".obj", ".o", ".lib", ".dll", ".exe", ".exp",
    ".db", ".opendb", ".sdf", ".ipch"
)

if ($null -eq $script:ExportedFileKeys) {
    $script:ExportedFileKeys = @{}
}

function Get-NormalizedPathKey {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return ""
    }

    try {
        if (Test-Path -LiteralPath $Path) {
            $resolved = (Resolve-Path -LiteralPath $Path).Path
            return ($resolved -replace '\\', '/').ToLowerInvariant()
        }
    }
    catch {
    }

    try {
        $full = [System.IO.Path]::GetFullPath((Join-Path (Get-Location).Path $Path))
        return ($full -replace '\\', '/').ToLowerInvariant()
    }
    catch {
        return ($Path -replace '\\', '/').ToLowerInvariant()
    }
}

function Is-GptGeneratedExportFile {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return $false
    }

    $fileName = [System.IO.Path]::GetFileName($Path).ToLowerInvariant()

    # Main protection: never search/export generated Context Control exports.
    if ($fileName -like "cc_code_export*") {
        return $true
    }

    # Also protect whatever active output file name the user chose.
    $pathKey = Get-NormalizedPathKey $Path
    $outputKey = Get-NormalizedPathKey $script:OutputFileForFilters

    if ($pathKey -eq $outputKey) {
        return $true
    }

    $outputLeaf = [System.IO.Path]::GetFileName($script:OutputFileForFilters).ToLowerInvariant()
    if ($outputLeaf -ne "" -and ($fileName -eq $outputLeaf -or $fileName.StartsWith($outputLeaf + "."))) {
        return $true
    }

    return $false
}

function Get-CodeFenceLanguage {
    param([string]$Path)

    $name = [System.IO.Path]::GetFileName($Path).ToLowerInvariant()
    $ext = [System.IO.Path]::GetExtension($Path).ToLowerInvariant()

    if ($name -eq "cmakelists.txt") {
        return "cmake"
    }

    switch ($ext) {
        ".h" { return "cpp" }
        ".hh" { return "cpp" }
        ".hpp" { return "cpp" }
        ".hxx" { return "cpp" }
        ".inl" { return "cpp" }
        ".c" { return "c" }
        ".cc" { return "cpp" }
        ".cpp" { return "cpp" }
        ".cxx" { return "cpp" }
        ".ixx" { return "cpp" }
        ".cmake" { return "cmake" }
        ".glsl" { return "glsl" }
        ".vert" { return "glsl" }
        ".frag" { return "glsl" }
        ".comp" { return "glsl" }
        ".geom" { return "glsl" }
        ".tesc" { return "glsl" }
        ".tese" { return "glsl" }
        ".mesh" { return "glsl" }
        ".task" { return "glsl" }
        ".shader" { return "glsl" }
        ".gd" { return "gdscript" }
        ".gdshader" { return "glsl" }
        ".json" { return "json" }
        ".tscn" { return "ini" }
        ".tres" { return "ini" }
        ".cfg" { return "ini" }
        ".ini" { return "ini" }
        ".toml" { return "toml" }
        ".yaml" { return "yaml" }
        ".yml" { return "yaml" }
        ".ps1" { return "powershell" }
        ".bat" { return "batch" }
        ".cmd" { return "batch" }
        ".ts" { return "typescript" }
        ".js" { return "javascript" }
        ".html" { return "html" }
        ".css" { return "css" }
        ".md" { return "markdown" }
        default { return "" }
    }
}

function Clean-InputLine {
    param([string]$Line)

    if ($null -eq $Line) {
        return ""
    }

    $x = $Line.Trim()

    if ($x -eq "") {
        return ""
    }

    if ($x.StartsWith($script:Fence3)) {
        return ""
    }

    if ($x.StartsWith("#")) {
        return ""
    }

    if ($x.StartsWith("- ")) {
        $x = $x.Substring(2).Trim()
    }

    if ($x.StartsWith("* ")) {
        $x = $x.Substring(2).Trim()
    }

    $x = $x -replace '^\d+[\.!\)]\s+', ''

    $Backtick = [string][char]96

    if ($x.StartsWith($Backtick) -and $x.EndsWith($Backtick) -and $x.Length -ge 2) {
        $x = $x.Substring(1, $x.Length - 2)
    }

    if ($x.StartsWith('"') -and $x.EndsWith('"') -and $x.Length -ge 2) {
        $x = $x.Substring(1, $x.Length - 2)
    }

    if ($x.StartsWith("'") -and $x.EndsWith("'") -and $x.Length -ge 2) {
        $x = $x.Substring(1, $x.Length - 2)
    }

    return $x.Trim()
}

function Is-ExcludedPath {
    param([string]$Path)

    if (Is-GptGeneratedExportFile $Path) {
        return $true
    }

    if ($Path -like "*.ccbak.*") {
        return $true
    }

    $parts = $Path -replace '\\', '/'
    $split = $parts -split '/'

    foreach ($part in $split) {
        if ($script:ExcludeDirs -contains $part) {
            return $true
        }
    }

    return $false
}

function Is-CodeSearchExtension {
    param([string]$Path)

    $name = [System.IO.Path]::GetFileName($Path).ToLowerInvariant()
    $ext = [System.IO.Path]::GetExtension($Path).ToLowerInvariant()

    if ($name -eq "cmakelists.txt") {
        return $true
    }

    return ($script:CodeSearchExtensions -contains $ext)
}

function Is-TextSearchCandidate {
    param([System.IO.FileInfo]$File)

    if ($null -eq $File) {
        return $false
    }

    if (Is-ExcludedPath $File.FullName) {
        return $false
    }

    $name = [System.IO.Path]::GetFileName($File.Name).ToLowerInvariant()
    $ext = [System.IO.Path]::GetExtension($File.Name).ToLowerInvariant()

    # FUNC:/FIND: search should only scan real programming/project source files.
    # Do not let previous Context Control exports or analysis markdown recursively match
    # function names and bloat the next export.
    if ($name -like "cc_code_export*.md" -or
        $name -like "*code_export*.md" -or
        $name -like "engine_analysis*.md" -or
        $name -like "*.ccbak.*") {
        return $false
    }

    # Markdown exports/docs are useful when explicitly requested as paths,
    # but they are bad symbol-search candidates because they contain copied code.
    if ($ext -eq ".md") {
        return $false
    }

    if ($script:BinaryExtensions -contains $ext) {
        return $false
    }

    if (-not ($script:TextExtensions -contains $ext)) {
        return $false
    }

    if ((-not $ForceLargeFiles) -and $File.Length -gt ($MaxFileKB * 1024)) {
        return $false
    }

    return $true
}

function Get-RelativeDisplayPath {
    param([string]$Path)

    $resolved = (Resolve-Path -LiteralPath $Path).Path
    $root = (Get-Location).Path

    if ($resolved.StartsWith($root, [System.StringComparison]::OrdinalIgnoreCase)) {
        $rel = $resolved.Substring($root.Length)
        $rel = $rel -replace '^[\\/]+', ''
        return ($rel -replace '\\', '/')
    }

    return ($resolved -replace '\\', '/')
}

function Get-PathKey {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        return ($Path -replace '\\', '/').ToLowerInvariant()
    }

    $resolved = (Resolve-Path -LiteralPath $Path).Path
    return ($resolved -replace '\\', '/').ToLowerInvariant()
}
