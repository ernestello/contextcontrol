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

        var byKey = new Dictionary<string, SkillbookEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in ReadDirectory(GlobalRoot, "global"))
        {
            byKey[entry.Key] = entry;
        }

        foreach (var entry in ReadDirectory(ProjectRoot, "project"))
        {
            byKey[entry.Key] = entry;
        }

        return byKey.Values
            .OrderBy(entry => entry.Source.Equals("project", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(entry => entry.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public string BuildEnabledInstructionText()
    {
        var entries = LoadEntries()
            .Where(entry => entry.Enabled && !string.IsNullOrWhiteSpace(entry.Text))
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

    private void EnsureSeedFiles()
    {
        Directory.CreateDirectory(GlobalRoot);
        Directory.CreateDirectory(ProjectRoot);

        var projectDefault = Path.Combine(ProjectRoot, DefaultEntryFileName);
        if (File.Exists(projectDefault))
        {
            return;
        }

        var agentPrompt = Path.Combine(ContextControlRoot, "agentPrompt.txt");
        var text = File.Exists(agentPrompt)
            ? File.ReadAllText(agentPrompt)
            : BuildFallbackInstructions();

        File.WriteAllText(projectDefault, text.TrimEnd() + Environment.NewLine, Utf8NoBom);
    }

    private static string BuildFallbackInstructions()
    {
        return """
            You are working in ContextControl economy mode.

            ROLE:
            You are a patch author, not a repo agent.

            RULES:
            - Use only the context attached to the current request.
            - Ask for the smallest safe file/function/FIND list when only DIR context is available.
            - Produce raw BEGIN CC-REPLACE blocks when CC source context is available and a patch is requested.
            - Never claim to have read files that were not included in the prompt.
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
}
