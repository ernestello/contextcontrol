// CC-DESC: Persists Workbench appearance preferences between app runs.

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ContextControl.Workbench.Services;

public sealed class WorkbenchSettings
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

    private WorkbenchSettings(string settingsPath, string themeKey, string syntaxThemeKey)
    {
        SettingsPath = settingsPath;
        ThemeKey = NormalizeKey(themeKey, "empty");
        SyntaxThemeKey = NormalizeKey(syntaxThemeKey, "adaptive");
    }

    public string SettingsPath { get; }
    public string ThemeKey { get; set; }
    public string SyntaxThemeKey { get; set; }

    public static WorkbenchSettings Load()
    {
        var settingsPath = ResolveSettingsPath();
        var data = new WorkbenchSettingsJson();

        if (File.Exists(settingsPath))
        {
            try
            {
                data = JsonSerializer.Deserialize<WorkbenchSettingsJson>(File.ReadAllText(settingsPath), JsonOptions)
                    ?? new WorkbenchSettingsJson();
            }
            catch
            {
                data = new WorkbenchSettingsJson();
            }
        }

        return new WorkbenchSettings(
            settingsPath,
            data.ThemeKey ?? "empty",
            data.SyntaxThemeKey ?? "adaptive");
    }

    public void Save()
    {
        var parent = Path.GetDirectoryName(SettingsPath);
        if (!string.IsNullOrWhiteSpace(parent))
        {
            Directory.CreateDirectory(parent);
        }

        var data = new WorkbenchSettingsJson
        {
            ThemeKey = NormalizeKey(ThemeKey, "empty"),
            SyntaxThemeKey = NormalizeKey(SyntaxThemeKey, "adaptive")
        };
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(data, JsonOptions) + Environment.NewLine, Utf8NoBom);
    }

    private static string ResolveSettingsPath()
    {
        var root = FindContextControlRoot(Directory.GetCurrentDirectory())
            ?? FindContextControlRoot(AppContext.BaseDirectory)
            ?? AppContext.BaseDirectory;

        return Path.Combine(root, ".ccWorkbench.settings.json");
    }

    private static string? FindContextControlRoot(string startPath)
    {
        if (string.IsNullOrWhiteSpace(startPath))
        {
            return null;
        }

        var directory = File.Exists(startPath)
            ? new DirectoryInfo(Path.GetDirectoryName(startPath) ?? "")
            : new DirectoryInfo(startPath);

        while (directory is not null)
        {
            if (LooksLikeContextControl(directory.FullName))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static bool LooksLikeContextControl(string path)
    {
        return Directory.Exists(path)
            && File.Exists(Path.Combine(path, "ccStart.ps1"))
            && File.Exists(Path.Combine(path, "ccDir.ps1"))
            && File.Exists(Path.Combine(path, "cc.ps1"))
            && File.Exists(Path.Combine(path, "ccReplace.ps1"));
    }

    private static string NormalizeKey(string? key, string fallback)
    {
        return string.IsNullOrWhiteSpace(key) ? fallback : key.Trim().ToLowerInvariant();
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private sealed class WorkbenchSettingsJson
    {
        public string? ThemeKey { get; set; }
        public string? SyntaxThemeKey { get; set; }
    }
}
