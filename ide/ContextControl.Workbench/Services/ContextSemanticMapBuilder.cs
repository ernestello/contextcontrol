// CC-DESC: Builds a compact semantic navigation map from a DIR export and source signals.

using System.Text;
using System.Text.RegularExpressions;

namespace ContextControl.Workbench.Services;

public sealed partial class ContextSemanticMapBuilder
{
    private const int MaxSignalFileReadCharacters = 240_000;
    private const int MaxHighSignalFiles = 90;
    private const int MaxPathIndexFiles = 280;

    private static readonly string[] IgnoredPathParts =
    [
        ".git",
        ".tmp",
        ".ccReplace.versions",
        "bin",
        "obj",
        "build",
        "vcpkg_installed",
        "vendor",
        "external",
        "third_party"
    ];

    private static readonly string[] IgnoredFileNames =
    [
        "cc_project_dir.md",
        "cc_semantic_map.md",
        "cc_code_export.md",
        "patch.txt",
        "mainwindow_axaml.txt",
        ".ccWorkbench.chat-history.json"
    ];

    private static readonly string[] SignalWords =
    [
        "prompt",
        "send",
        "button",
        "chat",
        "llm",
        "ollama",
        "dir",
        "cc",
        "go",
        "replace",
        "patch",
        "skillbook",
        "settings",
        "theme",
        "style",
        "window",
        "browser",
        "terminal",
        "attachment",
        "token",
        "context"
    ];

    public async Task<string> BuildAsync(string projectRoot, string directoryExportText, CancellationToken cancellationToken = default)
    {
        var files = ExtractFilesFromDirExport(directoryExportText)
            .Where(IsSemanticMapCandidate)
            .ToList();
        if (files.Count == 0)
        {
            files = EnumerateProjectFiles(projectRoot)
                .Where(IsSemanticMapCandidate)
                .ToList();
        }

        var signals = new List<FileSignal>();
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            signals.Add(await BuildSignalAsync(projectRoot, file, cancellationToken).ConfigureAwait(false));
        }

        var highSignal = signals
            .Where(signal => signal.Score > 0)
            .OrderByDescending(signal => signal.Score)
            .ThenBy(signal => signal.Path, StringComparer.OrdinalIgnoreCase)
            .Take(MaxHighSignalFiles)
            .ToArray();

        var builder = new StringBuilder();
        builder.AppendLine("# ContextControl semantic map");
        builder.AppendLine();
        builder.AppendLine("Purpose: choose exact paths for cc.ps1 from project structure and local file signals.");
        builder.AppendLine("Rules: copy paths exactly; prefer concrete files over FIND when a likely owner is listed; use FIND only when no listed path is clearly relevant.");
        builder.AppendLine();
        builder.AppendLine($"Project root: {projectRoot}");
        builder.AppendLine($"Indexed files: {files.Count:N0}");
        builder.AppendLine();

        AppendSection(builder, "Prompt / Chat / Local LLM", highSignal, "prompt", "chat", "llm", "ollama", "attachment", "token", "context", "terminal");
        AppendSection(builder, "UI / Theme / Buttons", highSignal, "button", "style", "theme", "window", "send");
        AppendSection(builder, "CC Workflow / Replace", highSignal, "dir", "cc", "go", "replace", "patch");
        AppendSection(builder, "Settings / Skillbook / Browser", highSignal, "settings", "skillbook", "browser");

        builder.AppendLine("## High-signal path index");
        foreach (var signal in highSignal)
        {
            builder.Append("- ");
            builder.Append(signal.Path);
            builder.Append(" — ");
            builder.Append(signal.Role);
            if (signal.Terms.Count > 0)
            {
                builder.Append("; signals: ");
                builder.Append(string.Join(", ", signal.Terms.Take(8)));
            }

            if (signal.Markers.Count > 0)
            {
                builder.Append("; markers: ");
                builder.Append(string.Join(", ", signal.Markers.Take(6)));
            }

            builder.AppendLine();
        }

        builder.AppendLine();
        builder.AppendLine("## Exact file paths");
        foreach (var path in files.Take(MaxPathIndexFiles))
        {
            builder.AppendLine(path);
        }

        if (files.Count > MaxPathIndexFiles)
        {
            builder.AppendLine($"... {files.Count - MaxPathIndexFiles:N0} more file(s) omitted from compact path index.");
        }

        return builder.ToString().TrimEnd() + Environment.NewLine;
    }

    private static void AppendSection(StringBuilder builder, string title, IReadOnlyList<FileSignal> signals, params string[] terms)
    {
        var items = signals
            .Where(signal => terms.Any(term => signal.Terms.Contains(term, StringComparer.OrdinalIgnoreCase)))
            .Take(16)
            .ToArray();
        if (items.Length == 0)
        {
            return;
        }

        builder.AppendLine($"## {title}");
        foreach (var item in items)
        {
            builder.Append("- ");
            builder.Append(item.Path);
            builder.Append(" — ");
            builder.AppendLine(item.Role);
        }

        builder.AppendLine();
    }

    private static async Task<FileSignal> BuildSignalAsync(string projectRoot, string relativePath, CancellationToken cancellationToken)
    {
        var pathText = relativePath.Replace('\\', '/');
        var combined = pathText;
        var markers = new List<string>();

        var fullPath = ResolveProjectFile(projectRoot, relativePath);
        if (!string.IsNullOrWhiteSpace(fullPath) && File.Exists(fullPath) && IsTextSignalFile(relativePath))
        {
            try
            {
                var text = await File.ReadAllTextAsync(fullPath, cancellationToken).ConfigureAwait(false);
                if (text.Length > MaxSignalFileReadCharacters)
                {
                    text = text[..MaxSignalFileReadCharacters];
                }

                combined += "\n" + text;
                markers.AddRange(ExtractMarkers(relativePath, text));
            }
            catch
            {
                markers.Add("read skipped");
            }
        }

        var terms = SignalWords
            .Where(word => combined.Contains(word, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var score = terms.Length;
        score += markers.Count * 2;
        score += pathText.Contains("MainWindow", StringComparison.OrdinalIgnoreCase) ? 8 : 0;
        score += pathText.Contains("WorkbenchDesign", StringComparison.OrdinalIgnoreCase) ? 8 : 0;
        score += pathText.Contains("ContextControlViewModel", StringComparison.OrdinalIgnoreCase) ? 8 : 0;
        score += pathText.Contains("ContextCapsuleBuilder", StringComparison.OrdinalIgnoreCase) ? 6 : 0;
        score += pathText.Contains("LocalLlm", StringComparison.OrdinalIgnoreCase) ? 5 : 0;

        return new FileSignal(relativePath, BuildRole(relativePath, terms, markers), score, terms, markers);
    }

    private static string BuildRole(string relativePath, IReadOnlyList<string> terms, IReadOnlyList<string> markers)
    {
        var extension = Path.GetExtension(relativePath);
        var name = Path.GetFileName(relativePath);
        var role = extension.ToLowerInvariant() switch
        {
            ".axaml" => "Avalonia UI layout/resources",
            ".cs" => "C# workbench logic",
            ".ps1" => "ContextControl PowerShell workflow",
            ".md" => "documentation/instructions",
            ".csproj" => "project/build configuration",
            _ => "project file"
        };

        if (name.Equals("MainWindow.axaml", StringComparison.OrdinalIgnoreCase))
        {
            return "main workbench layout; prompt/chat/browser panes and command buttons";
        }

        if (name.Equals("WorkbenchDesign.axaml", StringComparison.OrdinalIgnoreCase))
        {
            return "global workbench styles and button/control classes";
        }

        if (name.Equals("ContextControlViewModel.cs", StringComparison.OrdinalIgnoreCase))
        {
            return "DIR/CC/GO/send commands, prompt state, local chat workflow";
        }

        if (markers.Contains("cc-prompt-send", StringComparer.OrdinalIgnoreCase))
        {
            return "prompt Send button styling or layout";
        }

        if (terms.Contains("llm", StringComparer.OrdinalIgnoreCase) || terms.Contains("ollama", StringComparer.OrdinalIgnoreCase))
        {
            return "local LLM/Ollama workflow";
        }

        if (terms.Contains("replace", StringComparer.OrdinalIgnoreCase) || terms.Contains("patch", StringComparer.OrdinalIgnoreCase))
        {
            return "patch/replace workflow";
        }

        return role;
    }

    private static IEnumerable<string> ExtractMarkers(string relativePath, string text)
    {
        var markers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in XamlClassRegex().Matches(text))
        {
            var value = match.Groups["name"].Value.Trim();
            if (!string.IsNullOrWhiteSpace(value))
            {
                markers.Add(value);
            }
        }

        foreach (Match match in CSharpMemberRegex().Matches(text))
        {
            var value = match.Groups["name"].Value.Trim();
            if (!string.IsNullOrWhiteSpace(value))
            {
                markers.Add(value);
            }
        }

        foreach (var marker in markers
                     .Where(IsUsefulMarker)
                     .OrderByDescending(MarkerPriority)
                     .ThenBy(marker => marker, StringComparer.OrdinalIgnoreCase)
                     .Take(12))
        {
            yield return marker;
        }
    }

    private static bool IsUsefulMarker(string marker)
    {
        return marker.Contains("prompt", StringComparison.OrdinalIgnoreCase)
            || marker.Contains("send", StringComparison.OrdinalIgnoreCase)
            || marker.Contains("chat", StringComparison.OrdinalIgnoreCase)
            || marker.Contains("llm", StringComparison.OrdinalIgnoreCase)
            || marker.Contains("replace", StringComparison.OrdinalIgnoreCase)
            || marker.Contains("cc-", StringComparison.OrdinalIgnoreCase);
    }

    private static int MarkerPriority(string marker)
    {
        var priority = 0;
        priority += marker.Contains("prompt", StringComparison.OrdinalIgnoreCase) ? 8 : 0;
        priority += marker.Contains("send", StringComparison.OrdinalIgnoreCase) ? 8 : 0;
        priority += marker.Contains("cc-prompt", StringComparison.OrdinalIgnoreCase) ? 12 : 0;
        priority += marker.Contains("chat", StringComparison.OrdinalIgnoreCase) ? 4 : 0;
        priority += marker.Contains("llm", StringComparison.OrdinalIgnoreCase) ? 3 : 0;
        return priority;
    }

    private static IEnumerable<string> ExtractFilesFromDirExport(string directoryExportText)
    {
        var stack = new Dictionary<int, string>();
        foreach (var rawLine in (directoryExportText ?? "").Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n'))
        {
            var line = rawLine.TrimEnd();
            var match = TreeLineRegex().Match(line);
            if (!match.Success)
            {
                continue;
            }

            var depth = match.Groups["prefix"].Value.Length / 4;
            var name = match.Groups["name"].Value.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            if (name.EndsWith("/", StringComparison.Ordinal))
            {
                stack[depth] = name.TrimEnd('/');
                foreach (var key in stack.Keys.Where(key => key > depth).ToArray())
                {
                    stack.Remove(key);
                }

                continue;
            }

            var parts = new List<string>();
            for (var index = 0; index < depth; index++)
            {
                if (stack.TryGetValue(index, out var part))
                {
                    parts.Add(part);
                }
            }

            parts.Add(name);
            yield return string.Join("/", parts);
        }
    }

    private static IEnumerable<string> EnumerateProjectFiles(string projectRoot)
    {
        if (string.IsNullOrWhiteSpace(projectRoot) || !Directory.Exists(projectRoot))
        {
            yield break;
        }

        foreach (var file in Directory.EnumerateFiles(projectRoot, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(projectRoot, file).Replace('\\', '/');
            if (IgnoredPathParts.Any(part => relative.Split('/').Contains(part, StringComparer.OrdinalIgnoreCase)))
            {
                continue;
            }

            yield return relative;
        }
    }

    private static bool IsSemanticMapCandidate(string relativePath)
    {
        var fileName = Path.GetFileName(relativePath);
        if (IgnoredFileNames.Contains(fileName, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        var parts = relativePath.Replace('\\', '/').Split('/');
        return !parts.Any(part => IgnoredPathParts.Contains(part, StringComparer.OrdinalIgnoreCase));
    }

    private static string ResolveProjectFile(string projectRoot, string relativePath)
    {
        try
        {
            var root = Path.GetFullPath(projectRoot);
            var fullPath = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
            var rootWithSeparator = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase) ? fullPath : "";
        }
        catch
        {
            return "";
        }
    }

    private static bool IsTextSignalFile(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() is ".cs" or ".axaml" or ".ps1" or ".md" or ".csproj" or ".json" or ".txt";
    }

    private sealed record FileSignal(
        string Path,
        string Role,
        int Score,
        IReadOnlyList<string> Terms,
        IReadOnlyList<string> Markers);

    [GeneratedRegex("^(?<prefix>(?:(?:│| )   )*)(?:├──|└──) (?<name>.+)$")]
    private static partial Regex TreeLineRegex();

    [GeneratedRegex("(?i)(?:Classes|Selector|Content|ToolTip\\.Tip)\\s*=\\s*\"(?<name>[^\"]+)\"")]
    private static partial Regex XamlClassRegex();

    [GeneratedRegex("\\b(?<name>[A-Za-z_][A-Za-z0-9_]*(?:Command|Label|Text|Async|Model|Builder|Service))\\b")]
    private static partial Regex CSharpMemberRegex();
}
