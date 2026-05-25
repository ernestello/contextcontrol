// CC-DESC: Represents a parsed code or ContextControl snippet inside local chat.

namespace ContextControl.Workbench.ViewModels;

public sealed class ChatSnippetViewModel(string kind, string language, string text) : ObservableObject
{
    private bool _isExpanded = ShouldExpandByDefault(text);

    public string Kind { get; } = string.IsNullOrWhiteSpace(kind) ? "code" : kind.Trim();
    public string Language { get; } = string.IsNullOrWhiteSpace(language) ? "text" : language.Trim();
    public string Text { get; } = text ?? "";

    public bool IsPatch => string.Equals(Kind, "patch", StringComparison.OrdinalIgnoreCase);
    public bool IsRequestList => string.Equals(Kind, "request", StringComparison.OrdinalIgnoreCase);
    public bool IsCode => !IsPatch && !IsRequestList;

    public string Title => IsPatch
        ? "CC-REPLACE patch"
        : IsRequestList ? "CC request list" : $"Code snippet: {Language}";

    public string ActionLabel => IsPatch
        ? "Preview"
        : IsRequestList ? "Use for CC" : "Copy";

    public int LineCount => string.IsNullOrEmpty(Text)
        ? 0
        : Text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n').Length;

    public string MetaLabel => $"{Language} - {LineCount:N0} lines - {Text.Length:N0} chars";

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (SetProperty(ref _isExpanded, value))
            {
                OnPropertyChanged(nameof(IsCollapsed));
                OnPropertyChanged(nameof(ToggleLabel));
            }
        }
    }

    public bool IsCollapsed => !IsExpanded;

    public string ToggleLabel => IsExpanded ? "Collapse" : "Expand";

    public string CollapsedPreview
    {
        get
        {
            var clean = (Text ?? "")
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n')
                .Trim();
            if (clean.Length <= 420)
            {
                return clean;
            }

            return clean[..420] + " ...";
        }
    }

    public void ToggleExpanded()
    {
        IsExpanded = !IsExpanded;
    }

    private static bool ShouldExpandByDefault(string? text)
    {
        var clean = text ?? "";
        if (clean.Length > 1800)
        {
            return false;
        }

        var lineCount = clean.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .Length;
        return lineCount <= 32;
    }
}
