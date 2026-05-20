// CC-DESC: Persists per-project supported/ignored file rules for tree export and external change tracking.

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ContextControl.Workbench.Services;

public sealed class ProjectFileRules
{
    private static readonly StringComparer NameComparer = StringComparer.OrdinalIgnoreCase;
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

    private static readonly string[] DefaultIgnoredDirectories =
    [
        ".ccReplace.versions",
        ".git",
        ".idea",
        ".vs",
        ".vscode",
        "bin",
        "build",
        "build-debug",
        "build-release",
        "cmake-build-debug",
        "cmake-build-release",
        "CMakeFiles",
        "Debug",
        "dist",
        "external",
        "extern",
        "node_modules",
        "obj",
        "out",
        "packages",
        "Release",
        "third_party",
        "thirdparty",
        "vendor",
        "vcpkg_installed",
        "x64"
    ];

    private static readonly string[] DefaultIgnoredExtensions =
    [
        ".bak",
        ".bin",
        ".bmp",
        ".cache",
        ".collision",
        ".db",
        ".dds",
        ".dll",
        ".exe",
        ".exp",
        ".flac",
        ".ilk",
        ".import",
        ".ipch",
        ".jpg",
        ".jpeg",
        ".lastbuildstate",
        ".lib",
        ".log",
        ".mp3",
        ".o",
        ".obj",
        ".ogg",
        ".opendb",
        ".pdb",
        ".png",
        ".sdf",
        ".snapshot",
        ".spv",
        ".svo",
        ".tga",
        ".tlog",
        ".tmp",
        ".uid",
        ".unsuccessfulbuild",
        ".wav",
        ".webp"
    ];

    private static readonly string[] DefaultSupportedExtensions =
    [
        ".axaml",
        ".bat",
        ".c",
        ".cc",
        ".cmd",
        ".comp",
        ".cpp",
        ".cs",
        ".csproj",
        ".css",
        ".cxx",
        ".frag",
        ".fs",
        ".fsproj",
        ".glsl",
        ".h",
        ".hh",
        ".hpp",
        ".html",
        ".hxx",
        ".inc",
        ".ini",
        ".inl",
        ".ipp",
        ".js",
        ".json",
        ".jsx",
        ".lua",
        ".m",
        ".md",
        ".metal",
        ".mm",
        ".props",
        ".ps1",
        ".psd1",
        ".psm1",
        ".py",
        ".rs",
        ".sh",
        ".slang",
        ".targets",
        ".toml",
        ".ts",
        ".tsx",
        ".txt",
        ".vert",
        ".wgsl",
        ".xaml",
        ".xml",
        ".yaml",
        ".yml"
    ];

    private readonly HashSet<string> _ignoredDirectories;
    private readonly HashSet<string> _ignoredExtensions;
    private readonly HashSet<string> _supportedExtensions;

    private ProjectFileRules(
        string projectRoot,
        string rulesPath,
        IEnumerable<string> ignoredDirectories,
        IEnumerable<string> ignoredExtensions,
        IEnumerable<string> supportedExtensions)
    {
        ProjectRoot = Path.GetFullPath(projectRoot);
        RulesPath = rulesPath;
        _ignoredDirectories = NormalizeNames(ignoredDirectories).ToHashSet(NameComparer);
        _ignoredExtensions = NormalizeExtensions(ignoredExtensions).ToHashSet(NameComparer);
        _supportedExtensions = NormalizeExtensions(supportedExtensions).ToHashSet(NameComparer);
    }

    public string ProjectRoot { get; }
    public string RulesPath { get; }
    public IReadOnlyCollection<string> IgnoredDirectories => _ignoredDirectories.OrderBy(value => value, NameComparer).ToArray();
    public IReadOnlyCollection<string> IgnoredExtensions => _ignoredExtensions.OrderBy(value => value, NameComparer).ToArray();
    public IReadOnlyCollection<string> SupportedExtensions => _supportedExtensions.OrderBy(value => value, NameComparer).ToArray();
    public string SupportedLabel => string.Join(", ", SupportedExtensions);
    public string IgnoredLabel => string.Join(", ", IgnoredExtensions);
    public string IgnoredDirectoriesLabel => string.Join(", ", IgnoredDirectories);
    public string SupportedExtensionsText => string.Join(Environment.NewLine, SupportedExtensions);
    public string IgnoredExtensionsText => string.Join(Environment.NewLine, IgnoredExtensions);
    public string IgnoredDirectoriesText => string.Join(Environment.NewLine, IgnoredDirectories);

    public static ProjectFileRules Load(string projectRoot)
    {
        var fullProjectRoot = Path.GetFullPath(projectRoot);
        var rulesPath = ResolveRulesPath(fullProjectRoot);
        var data = new ProjectFileRulesJson();

        if (File.Exists(rulesPath))
        {
            try
            {
                data = JsonSerializer.Deserialize<ProjectFileRulesJson>(File.ReadAllText(rulesPath), JsonOptions) ?? new ProjectFileRulesJson();
            }
            catch
            {
                data = new ProjectFileRulesJson();
            }
        }

        return new ProjectFileRules(
            fullProjectRoot,
            rulesPath,
            data.IgnoredDirectories.Count == 0 ? DefaultIgnoredDirectories : data.IgnoredDirectories,
            data.IgnoredExtensions.Count == 0 ? DefaultIgnoredExtensions : data.IgnoredExtensions,
            data.SupportedExtensions.Count == 0 ? DefaultSupportedExtensions : data.SupportedExtensions);
    }

    public void Save()
    {
        var parent = Path.GetDirectoryName(RulesPath);
        if (!string.IsNullOrWhiteSpace(parent))
        {
            Directory.CreateDirectory(parent);
        }

        var data = new ProjectFileRulesJson
        {
            IgnoredDirectories = IgnoredDirectories.ToList(),
            IgnoredExtensions = IgnoredExtensions.ToList(),
            SupportedExtensions = SupportedExtensions.ToList()
        };
        File.WriteAllText(RulesPath, JsonSerializer.Serialize(data, JsonOptions) + Environment.NewLine, Utf8NoBom);
    }

    public void UpdateRules(
        string ignoredDirectoriesText,
        string ignoredExtensionsText,
        string supportedExtensionsText)
    {
        ReplaceSet(_ignoredDirectories, NormalizeNames(SplitRuleText(ignoredDirectoriesText)));
        ReplaceSet(_ignoredExtensions, NormalizeExtensions(SplitRuleText(ignoredExtensionsText)));
        ReplaceSet(_supportedExtensions, NormalizeExtensions(SplitRuleText(supportedExtensionsText)));
    }

    public void ResetToDefaults()
    {
        ReplaceSet(_ignoredDirectories, NormalizeNames(DefaultIgnoredDirectories));
        ReplaceSet(_ignoredExtensions, NormalizeExtensions(DefaultIgnoredExtensions));
        ReplaceSet(_supportedExtensions, NormalizeExtensions(DefaultSupportedExtensions));
    }

    public ProjectFileTrackDecision GetTrackDecision(string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
        {
            return ProjectFileTrackDecision.Ignore("empty path");
        }

        var normalizedFullPath = Path.GetFullPath(fullPath);
        var relativePath = Path.GetRelativePath(ProjectRoot, normalizedFullPath);
        if (relativePath.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relativePath))
        {
            return ProjectFileTrackDecision.Ignore("outside project");
        }

        var fileName = Path.GetFileName(normalizedFullPath);
        if (fileName.Equals(".ccFileRules.json", StringComparison.OrdinalIgnoreCase))
        {
            return ProjectFileTrackDecision.Ignore("project file-rule config");
        }

        if (fileName.Contains(".ccbak.", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith("~", StringComparison.Ordinal)
            || fileName.EndsWith(".snapshot", StringComparison.OrdinalIgnoreCase))
        {
            return ProjectFileTrackDecision.Ignore("backup/temp/snapshot file");
        }

        var parts = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var ignoredPart = parts.FirstOrDefault(part => _ignoredDirectories.Contains(part));
        if (!string.IsNullOrWhiteSpace(ignoredPart))
        {
            return ProjectFileTrackDecision.Ignore($"ignored directory: {ignoredPart}");
        }

        var extension = NormalizeExtension(Path.GetExtension(normalizedFullPath));
        if (_ignoredExtensions.Contains(extension))
        {
            return ProjectFileTrackDecision.Ignore($"ignored extension: {extension}");
        }

        if (!_supportedExtensions.Contains(extension))
        {
            return ProjectFileTrackDecision.Ignore($"unsupported extension: {extension}");
        }

        return ProjectFileTrackDecision.Track();
    }

    public bool IsSupportedExtension(string extension)
    {
        return _supportedExtensions.Contains(NormalizeExtension(extension));
    }

    public bool ShouldSkipDirectory(string directoryName)
    {
        return _ignoredDirectories.Contains(directoryName);
    }

    private static IEnumerable<string> SplitRuleText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        return value
            .Split(['\r', '\n', ',', ';', '\t', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(NameComparer);
    }

    private static void ReplaceSet(HashSet<string> target, IEnumerable<string> values)
    {
        target.Clear();
        foreach (var value in values)
        {
            target.Add(value);
        }
    }

    private static string ResolveRulesPath(string projectRoot)
    {
        var contextControlRoot = FindContextControlRoot(projectRoot) ?? projectRoot;
        return Path.Combine(contextControlRoot, ".ccFileRules.json");
    }

    private static string? FindContextControlRoot(string projectRoot)
    {
        if (LooksLikeContextControl(projectRoot))
        {
            return projectRoot;
        }

        var nested = Path.Combine(projectRoot, "contextcontrol");
        return LooksLikeContextControl(nested) ? nested : null;
    }

    private static bool LooksLikeContextControl(string path)
    {
        return Directory.Exists(path)
            && File.Exists(Path.Combine(path, "ccStart.ps1"))
            && File.Exists(Path.Combine(path, "ccDir.ps1"))
            && File.Exists(Path.Combine(path, "cc.ps1"))
            && File.Exists(Path.Combine(path, "ccReplace.ps1"));
    }

    private static IEnumerable<string> NormalizeNames(IEnumerable<string> values)
    {
        return values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(NameComparer);
    }

    private static IEnumerable<string> NormalizeExtensions(IEnumerable<string> values)
    {
        return values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(NormalizeExtension)
            .Distinct(NameComparer);
    }

    private static string NormalizeExtension(string extension)
    {
        var clean = string.IsNullOrWhiteSpace(extension) ? "" : extension.Trim().ToLowerInvariant();
        return clean.StartsWith(".", StringComparison.Ordinal) ? clean : "." + clean;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private sealed class ProjectFileRulesJson
    {
        public List<string> IgnoredDirectories { get; set; } = [];
        public List<string> IgnoredExtensions { get; set; } = [];
        public List<string> SupportedExtensions { get; set; } = [];
    }
}

public sealed record ProjectFileTrackDecision(bool ShouldTrack, string IgnoredReason)
{
    public static ProjectFileTrackDecision Track() => new(true, "");
    public static ProjectFileTrackDecision Ignore(string reason) => new(false, reason);
}
