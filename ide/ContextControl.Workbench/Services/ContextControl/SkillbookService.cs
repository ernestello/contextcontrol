// CC-DESC: Loads global and project ContextControl instruction entries for local LLM capsules.

using System.Text;

namespace ContextControl.Workbench.Services;

public sealed record SkillbookEntry(
    string Key,
    string Title,
    string Text,
    string Source,
    bool Enabled);

public sealed class SkillbookService
{
    private const string DefaultEntryFileName = "cc-patch-author.md";
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

    public SkillbookService(string contextControlRoot)
    {
        ContextControlRoot = contextControlRoot;
        GlobalRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ContextControl",
            "skillbook");
        ProjectRoot = Path.Combine(contextControlRoot, "skillbook");
    }

    public string ContextControlRoot { get; }
    public string GlobalRoot { get; }
    public string ProjectRoot { get; }

    public IReadOnlyList<SkillbookEntry> LoadEntries()
    {
        EnsureSeedFiles();

        var entries = new List<SkillbookEntry>();
        entries.AddRange(CodexInstructionCatalog.SkillbookEntries);

        var byKey = new Dictionary<string, SkillbookEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in ReadDirectory(GlobalRoot, "global"))
        {
            byKey[entry.Key] = entry;
        }

        foreach (var entry in ReadDirectory(ProjectRoot, "project"))
        {
            byKey[entry.Key] = entry;
        }

        entries.AddRange(byKey.Values);

        return entries
            .OrderBy(entry => SourceRank(entry.Source))
            .ThenBy(entry => entry.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public string BuildEnabledInstructionText()
    {
        var entries = LoadEntries()
            .Where(entry => entry.Enabled
                && !IsBuiltInCodexEntry(entry)
                && !string.IsNullOrWhiteSpace(entry.Text))
            .ToArray();

        if (entries.Length == 0)
        {
            return "";
        }

        var builder = new StringBuilder();
        foreach (var entry in entries)
        {
            builder.AppendLine($"# {entry.Title}");
            builder.AppendLine(entry.Text.Trim());
            builder.AppendLine();
        }

        return builder.ToString().TrimEnd();
    }

    public string BuildCodexInstructionText(ContextCapsulePhase phase)
    {
        return CodexInstructionCatalog.BuildCodexInstructionText(phase);
    }

    private void EnsureSeedFiles()
    {
        Directory.CreateDirectory(GlobalRoot);
        Directory.CreateDirectory(ProjectRoot);

        var projectDefault = Path.Combine(ProjectRoot, DefaultEntryFileName);
        if (File.Exists(projectDefault))
        {
            return;
        }

        File.WriteAllText(projectDefault, BuildFallbackInstructions().TrimEnd() + Environment.NewLine, Utf8NoBom);
    }

    private static string BuildFallbackInstructions()
    {
        return """
            # CC-native local model skill

            You are not an autonomous filesystem agent. You only see the capsule text sent by ContextControl.

            Follow the active phase:
            - DIR context: identify the smallest useful next CC request.
            - CC source context: answer from the provided source, or emit CC-REPLACE blocks.
            - Patch context: review or repair the provided patch.
            - No context: chat normally and ask for DIR/CC when code evidence is needed.

            Keep outputs mechanical:
            - Prefer exact files, FUNCTION path :: symbol, or FIND: exactText.
            - In DIR phase, exact file paths must exist in the attached tree. If a user/generator names a missing path, use it only as a hint and return real tree paths or FIND:.
            - In DIR phase, never return END by itself. If unsure, output a few FIND: terms from the user request, then END.
            - Avoid broad folders and invented APIs.
            - Patch output must use raw BEGIN/END CC-REPLACE blocks.
            """;
    }

    private static IEnumerable<SkillbookEntry> ReadDirectory(string directory, string source)
    {
        if (!Directory.Exists(directory))
        {
            yield break;
        }

        foreach (var path in Directory.EnumerateFiles(directory, "*.md").OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var key = Path.GetFileNameWithoutExtension(path);
            var title = MakeTitle(key);
            string text;
            try
            {
                text = File.ReadAllText(path);
            }
            catch
            {
                continue;
            }

            yield return new SkillbookEntry(key, title, text, source, Enabled: true);
        }
    }

    private static string MakeTitle(string key)
    {
        var clean = (key ?? "")
            .Replace('-', ' ')
            .Replace('_', ' ')
            .Trim();
        return string.IsNullOrWhiteSpace(clean) ? "Instruction" : clean;
    }

    private static int SourceRank(string source)
    {
        return source.ToLowerInvariant() switch
        {
            "codex" => 0,
            "skillflow" => 1,
            "project" => 2,
            "global" => 3,
            _ => 4
        };
    }

    private static bool IsBuiltInCodexEntry(SkillbookEntry entry)
    {
        return entry.Source.Equals("codex", StringComparison.OrdinalIgnoreCase)
            || entry.Source.Equals("skillflow", StringComparison.OrdinalIgnoreCase);
    }
}
