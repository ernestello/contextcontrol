// CC-DESC: Represents a parsed local LLM chat transcript row with snippets and stats.

using System.Collections.ObjectModel;
using System.Text;
using System.Text.RegularExpressions;
using ContextControl.Workbench.Services;

namespace ContextControl.Workbench.ViewModels;

public sealed partial class LocalLlmChatMessageViewModel : ObservableObject
{
    private readonly string _rawText;
    private bool _isThinkingExpanded;
    private bool _isDiagnosticExpanded;

    public LocalLlmChatMessageViewModel(
        string role,
        string text,
        string modelId = "",
        string phase = "",
        string capsuleSummary = "",
        LocalLlmUsageStats? stats = null,
        IReadOnlyList<ContextControlAttachmentViewModel>? attachments = null,
        DateTime? createdUtc = null,
        string diagnosticPrompt = "")
    {
        Role = string.IsNullOrWhiteSpace(role) ? "assistant" : role.Trim();
        ModelId = modelId ?? "";
        Phase = phase ?? "";
        CapsuleSummary = capsuleSummary ?? "";
        Stats = stats;
        DiagnosticPrompt = diagnosticPrompt ?? "";
        AttachedFiles = new ObservableCollection<ContextControlAttachmentViewModel>(attachments ?? []);
        CreatedUtc = createdUtc ?? DateTime.UtcNow;
        Time = CreatedUtc.ToLocalTime().ToString("HH:mm");

        _rawText = text ?? "";
        var parsed = ParseMessage(_rawText);
        ThinkingText = parsed.Thinking;
        VisibleText = parsed.VisibleText;
        Parts = new ObservableCollection<LocalLlmChatPartViewModel>(parsed.Parts);
        Snippets = new ObservableCollection<ChatSnippetViewModel>(parsed.Snippets);
    }

    public string Role { get; }
    public DateTime CreatedUtc { get; }
    public string Time { get; }
    public string ModelId { get; }
    public string Phase { get; }
    public string CapsuleSummary { get; }
    public LocalLlmUsageStats? Stats { get; }
    public string DiagnosticPrompt { get; }
    public string VisibleText { get; }
    public string ThinkingText { get; }
    public ObservableCollection<LocalLlmChatPartViewModel> Parts { get; }
    public ObservableCollection<ChatSnippetViewModel> Snippets { get; }
    public ObservableCollection<ContextControlAttachmentViewModel> AttachedFiles { get; }

    public bool IsUser => string.Equals(Role, "user", StringComparison.OrdinalIgnoreCase);
    public bool IsContextControlGenerated => string.Equals(ModelId, "ContextControl", StringComparison.OrdinalIgnoreCase)
        || string.Equals(ModelId, "ccReplace", StringComparison.OrdinalIgnoreCase);
    public string RoleLabel => IsUser ? "You" : IsContextControlGenerated ? ModelId : "Local";
    public bool HasStats => Stats is not null;
    public bool HasDiagnosticPrompt => !string.IsNullOrWhiteSpace(DiagnosticPrompt);
    public bool HasThinking => !string.IsNullOrWhiteSpace(ThinkingText);
    public bool HasAttachments => AttachedFiles.Count > 0;
    public bool HasSentAttachments => IsUser && HasAttachments;
    public bool HasSnippets => Snippets.Count > 0;
    public bool CanCreateProject => !IsUser && Snippets.Count(snippet => snippet.CanCreateProjectFile) > 0;
    public string Text => VisibleText;
    public string RawText => _rawText;
    public LocalLlmChatPartViewModel? PrimaryTextPart => Parts.FirstOrDefault(part => part.IsText);

    public string MetaLabel
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(ModelId))
            {
                parts.Add(ModelId);
            }

            if (!string.IsNullOrWhiteSpace(Phase))
            {
                parts.Add(Phase);
            }

            if (Stats is not null)
            {
                parts.Add(Stats.Summary);
            }
            else if (!string.IsNullOrWhiteSpace(CapsuleSummary))
            {
                parts.Add(CapsuleSummary);
            }

            return string.Join(" | ", parts);
        }
    }

    public bool IsThinkingExpanded
    {
        get => _isThinkingExpanded;
        set => SetProperty(ref _isThinkingExpanded, value);
    }

    public bool IsDiagnosticExpanded
    {
        get => _isDiagnosticExpanded;
        set => SetProperty(ref _isDiagnosticExpanded, value);
    }

    public void ToggleThinking()
    {
        IsThinkingExpanded = !IsThinkingExpanded;
    }

    public void ToggleDiagnostic()
    {
        IsDiagnosticExpanded = !IsDiagnosticExpanded;
    }

    private static ParsedMessage ParseMessage(string text)
    {
        var clean = text ?? "";
        var thinking = ExtractThinking(clean, out clean);
        var snippets = new List<ChatSnippetViewModel>();
        var parts = new List<LocalLlmChatPartViewModel>();

        var patchMatches = PatchBlockRegex().Matches(clean).Cast<Match>().ToArray();
        foreach (var patch in patchMatches)
        {
            snippets.Add(new ChatSnippetViewModel("patch", "cc-replace", patch.Value.Trim()));
        }

        var patchIndex = 0;
        clean = PatchBlockRegex().Replace(clean, _ =>
        {
            patchIndex++;
            return $"{Environment.NewLine}[CC-REPLACE patch block {patchIndex}]{Environment.NewLine}";
        });

        var requestScanText = CodeFenceRegex().Replace(clean, Environment.NewLine);
        requestScanText = PatchPlaceholderRegex().Replace(requestScanText, Environment.NewLine);

        var cursor = 0;
        foreach (Match match in CodeFenceRegex().Matches(clean))
        {
            var precedingText = clean[cursor..match.Index];
            AddTextPart(precedingText, parts);
            var language = match.Groups["lang"].Value;
            var code = match.Groups["code"].Value.Trim();
            if (PatchPlaceholderRegex().IsMatch(code))
            {
                cursor = match.Index + match.Length;
                continue;
            }

            var requestFromFence = IsMostlyRequestList(code)
                ? ExtractRequestList(code)
                : null;
            var snippet = requestFromFence ?? new ChatSnippetViewModel(
                "code",
                language,
                code,
                InferSuggestedFileName(language, precedingText));
            snippets.Add(snippet);
            parts.Add(new LocalLlmChatPartViewModel("snippet", "", snippet));
            cursor = match.Index + match.Length;
        }

        AddTextPart(clean[cursor..], parts);

        var requestSnippet = ExtractRequestList(requestScanText);
        if (requestSnippet is not null && snippets.All(snippet => !snippet.IsRequestList))
        {
            snippets.Add(requestSnippet);
            parts.Add(new LocalLlmChatPartViewModel("snippet", "", requestSnippet));
        }

        foreach (var patch in snippets.Where(snippet => snippet.IsPatch))
        {
            if (parts.All(part => !ReferenceEquals(part.Snippet, patch)))
            {
                parts.Add(new LocalLlmChatPartViewModel("snippet", "", patch));
            }
        }

        var visible = string.Join(Environment.NewLine, parts.Where(part => part.IsText).Select(part => part.Text)).Trim();
        return new ParsedMessage(visible, thinking, parts, snippets);
    }

    private static void AddTextPart(string text, List<LocalLlmChatPartViewModel> parts)
    {
        var clean = text.Trim();
        if (!string.IsNullOrWhiteSpace(clean))
        {
            parts.Add(new LocalLlmChatPartViewModel("text", clean));
        }
    }

    private static string ExtractThinking(string text, out string withoutThinking)
    {
        var builder = new StringBuilder();
        withoutThinking = ThinkRegex().Replace(text, match =>
        {
            if (builder.Length > 0)
            {
                builder.AppendLine();
            }

            builder.Append(match.Groups["body"].Value.Trim());
            return "";
        });

        return builder.ToString().Trim();
    }

    private static ChatSnippetViewModel? ExtractRequestList(string text)
    {
        var lines = (text ?? "")
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToArray();

        var requestLines = new List<string>();
        foreach (var line in lines)
        {
            var normalized = ContextPromptBuilder.NormalizeCodeExportRequestLine(line);
            if (normalized.Equals("END", StringComparison.OrdinalIgnoreCase))
            {
                if (requestLines.Count > 0)
                {
                    requestLines.Add("END");
                    return new ChatSnippetViewModel("request", "cc-request", string.Join(Environment.NewLine, requestLines));
                }

                continue;
            }

            if (IsRequestLine(normalized))
            {
                requestLines.Add(normalized);
            }
        }

        if (requestLines.Count == 0)
        {
            return null;
        }

        requestLines.Add("END");
        return new ChatSnippetViewModel("request", "cc-request", string.Join(Environment.NewLine, requestLines.Distinct(StringComparer.OrdinalIgnoreCase)));
    }

    private static bool IsMostlyRequestList(string text)
    {
        var lines = (text ?? "")
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToArray();
        if (lines.Length == 0)
        {
            return false;
        }

        var requestLike = lines.Count(line =>
        {
            var normalized = ContextPromptBuilder.NormalizeCodeExportRequestLine(line);
            return normalized.Equals("END", StringComparison.OrdinalIgnoreCase)
                || IsRequestLine(normalized);
        });
        return requestLike == lines.Length && lines.Any(line => IsRequestLine(ContextPromptBuilder.NormalizeCodeExportRequestLine(line)));
    }

    private static bool IsRequestLine(string line)
    {
        return ContextPromptBuilder.IsCodeExportRequestLine(line);
    }

    private static string InferSuggestedFileName(string language, string precedingText)
    {
        var context = TakeTailLines(precedingText, 8);
        foreach (Match match in FileNameHintRegex().Matches(context).Cast<Match>().Reverse())
        {
            var fileName = match.Groups["file"].Value.Trim();
            if (IsPlausibleSnippetFileName(fileName, language))
            {
                return fileName;
            }
        }

        return "";
    }

    private static string TakeTailLines(string text, int maxLines)
    {
        var lines = (text ?? "")
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');
        return string.Join('\n', lines.TakeLast(Math.Max(1, maxLines)));
    }

    private static bool IsPlausibleSnippetFileName(string fileName, string language)
    {
        var clean = (fileName ?? "").Trim().Trim('`', '\'', '"');
        if (string.IsNullOrWhiteSpace(clean) || clean.Length > 160)
        {
            return false;
        }

        var leaf = clean.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "";
        if (string.IsNullOrWhiteSpace(leaf)
            || leaf.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || !leaf.Contains('.', StringComparison.Ordinal))
        {
            return false;
        }

        var extension = Path.GetExtension(leaf);
        if (string.IsNullOrWhiteSpace(extension))
        {
            return false;
        }

        var lang = (language ?? "").Trim().ToLowerInvariant();
        return lang switch
        {
            "html" or "htm" => extension.Equals(".html", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".htm", StringComparison.OrdinalIgnoreCase),
            "javascript" or "js" or "jsx" or "mjs" => extension.Equals(".js", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".mjs", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".jsx", StringComparison.OrdinalIgnoreCase),
            "typescript" or "ts" or "tsx" => extension.Equals(".ts", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".tsx", StringComparison.OrdinalIgnoreCase),
            "css" or "scss" or "sass" or "less" => extension.Equals(".css", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".scss", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".sass", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".less", StringComparison.OrdinalIgnoreCase),
            _ => true
        };
    }

    private sealed record ParsedMessage(
        string VisibleText,
        string Thinking,
        IReadOnlyList<LocalLlmChatPartViewModel> Parts,
        IReadOnlyList<ChatSnippetViewModel> Snippets);

    [GeneratedRegex("(?ms)<think>\\s*(?<body>.*?)\\s*</think>")]
    private static partial Regex ThinkRegex();

    [GeneratedRegex("(?ms)^\\s*BEGIN\\s+CC-REPLACE\\s*$.*?^\\s*END\\s+CC-REPLACE\\s*$")]
    private static partial Regex PatchBlockRegex();

    [GeneratedRegex("^\\s*\\[CC-REPLACE patch block \\d+\\]\\s*$")]
    private static partial Regex PatchPlaceholderRegex();

    [GeneratedRegex("(?ms)^[ \\t]*(?<fence>`{3,}|~{3,})[ \\t]*(?<lang>[^\\r\\n`]*)\\r?\\n(?<code>.*?)(?:\\r?\\n[ \\t]*\\k<fence>[ \\t]*$|\\z)")]
    private static partial Regex CodeFenceRegex();

    [GeneratedRegex(@"(?i)(?:\(|`|file\s+named\s+|file\s+|named\s+|save\s+(?:it|this|as)?\s*)(?<file>[A-Z0-9._/\- ]+\.[A-Z0-9]{1,12})(?:\)|`|:)?")]
    private static partial Regex FileNameHintRegex();
}
