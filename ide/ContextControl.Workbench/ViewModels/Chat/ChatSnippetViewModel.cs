// CC-DESC: Represents a parsed code or ContextControl snippet inside local chat.

namespace ContextControl.Workbench.ViewModels;

public sealed class ChatSnippetViewModel(string kind, string language, string text, string suggestedFileName = "") : ObservableObject
{
    private bool _isExpanded;

    public string Kind { get; } = string.IsNullOrWhiteSpace(kind) ? "code" : kind.Trim();
    public string Language { get; } = string.IsNullOrWhiteSpace(language) ? "text" : language.Trim();
    public string Text { get; } = text ?? "";
    public string SuggestedFileName { get; } = ResolveSuggestedFileName(language, text, suggestedFileName);

    public bool IsPatch => string.Equals(Kind, "patch", StringComparison.OrdinalIgnoreCase);
    public bool IsRequestList => string.Equals(Kind, "request", StringComparison.OrdinalIgnoreCase);
    public bool IsCode => !IsPatch && !IsRequestList;
    public bool CanSaveAsFile => IsCode && !string.IsNullOrWhiteSpace(Text);
    public bool CanCreateProjectFile => CanSaveAsFile && LooksLikeCompleteFile(Language, Text);
    public bool HasPromptAction => IsPatch || IsRequestList;

    public string TypeLabel => IsPatch
        ? "patch"
        : IsRequestList ? "request" : Language.ToLowerInvariant();

    public string Title => IsPatch
        ? "CC-REPLACE patch"
        : IsRequestList ? "CC request list" : $"Code snippet: {Language}";

    public string ActionLabel => IsPatch
        ? "Send to Prompt"
        : IsRequestList ? "Use for CC" : "Copy";

    public int LineCount => string.IsNullOrEmpty(Text)
        ? 0
        : Text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n').Length;

    public string MetaLabel => IsCode && !string.IsNullOrWhiteSpace(SuggestedFileName)
        ? $"{Language} - {LineCount:N0} lines - {Text.Length:N0} chars - file {SuggestedFileName}"
        : $"{Language} - {LineCount:N0} lines - {Text.Length:N0} chars";

    public string CompactMetaLabel
    {
        get
        {
            var meta = $"{TypeLabel} - {LineCount:N0} lines - {Text.Length:N0} chars - {FormatSize(Text.Length)}";
            return IsCode && !string.IsNullOrWhiteSpace(SuggestedFileName)
                ? $"{meta} - {SuggestedFileName}"
                : meta;
        }
    }

    public string DocumentPath => IsPatch
        ? "snippet.diff"
        : IsRequestList ? "request.md" : SuggestedFileName;

    public double CodePreviewHeight => Math.Clamp(42 + Math.Min(LineCount, 16) * 16, 88, 260);
    public double CollapsedPreviewHeight => 30;
    public double DisplayHeight => IsExpanded ? CodePreviewHeight : CollapsedPreviewHeight;
    public string DisplayText => IsExpanded ? Text : PreviewText;

    public string PreviewText
    {
        get
        {
            var clean = (Text ?? "")
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n')
                .TrimEnd();
            if (string.IsNullOrWhiteSpace(clean))
            {
                return "";
            }

            var line = clean
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(item => item.Trim())
                .FirstOrDefault(item => item.Length > 0) ?? clean.Trim();
            line = string.Join(' ', line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries));
            return line.Length <= 220 ? line : line[..220] + " ...";
        }
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (SetProperty(ref _isExpanded, value))
            {
                OnPropertyChanged(nameof(IsCollapsed));
                OnPropertyChanged(nameof(ToggleLabel));
                OnPropertyChanged(nameof(DisplayHeight));
                OnPropertyChanged(nameof(DisplayText));
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

    private static string FormatSize(int charCount)
    {
        if (charCount < 1024)
        {
            return $"{charCount:N0} B";
        }

        var kib = charCount / 1024d;
        if (kib < 1024)
        {
            return $"{kib:N1} KB";
        }

        return $"{kib / 1024d:N1} MB";
    }

    private static string ResolveSuggestedFileName(string? language, string? text, string? explicitFileName)
    {
        var cleanExplicit = CleanFileName(explicitFileName);
        if (!string.IsNullOrWhiteSpace(cleanExplicit))
        {
            return cleanExplicit;
        }

        var lang = (language ?? "").Trim().ToLowerInvariant();
        if (lang is "html" or "htm")
        {
            return "index.html";
        }

        if (lang is "css" or "scss" or "sass" or "less")
        {
            return "styles.css";
        }

        if (lang is "javascript" or "js" or "jsx" or "mjs")
        {
            return LooksLikeSnakeJavaScript(text) ? "snake.js" : "script.js";
        }

        return lang switch
        {
            "typescript" or "ts" or "tsx" => "script.ts",
            "json" => "data.json",
            "markdown" or "md" => "README.md",
            "python" or "py" => "script.py",
            "powershell" or "pwsh" or "ps1" => "script.ps1",
            "csharp" or "cs" => "Program.cs",
            "java" => "Main.java",
            "go" => "main.go",
            "rust" or "rs" => "main.rs",
            "cpp" or "c++" or "cc" or "cxx" => "main.cpp",
            "c" => "main.c",
            "xml" => "document.xml",
            "yaml" or "yml" => "config.yaml",
            "toml" => "config.toml",
            "sql" => "query.sql",
            "sh" or "bash" or "shell" => "script.sh",
            _ => "snippet.txt"
        };
    }

    private static string CleanFileName(string? fileName)
    {
        var clean = (fileName ?? "").Trim().Trim('`', '\'', '"');
        if (string.IsNullOrWhiteSpace(clean))
        {
            return "";
        }

        clean = clean.Replace('\\', '/');
        clean = clean.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? clean;
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            clean = clean.Replace(invalid, '_');
        }

        return clean;
    }

    private static bool LooksLikeSnakeJavaScript(string? text)
    {
        var clean = text ?? "";
        return clean.Contains("class Snake", StringComparison.OrdinalIgnoreCase)
            || clean.Contains("new Snake", StringComparison.OrdinalIgnoreCase)
            || clean.Contains("gameCanvas", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeCompleteFile(string? language, string? text)
    {
        var clean = (text ?? "").Trim();
        if (clean.Length < 8)
        {
            return false;
        }

        var lang = (language ?? "").Trim().ToLowerInvariant();
        if (lang is "diff" or "patch")
        {
            return false;
        }

        if (clean.Contains("*** Begin Patch", StringComparison.OrdinalIgnoreCase)
            || clean.Contains("<<<<<<<", StringComparison.Ordinal)
            || clean.Contains("@@ ", StringComparison.Ordinal))
        {
            return false;
        }

        return lang is "html" or "htm" or "css" or "scss" or "sass" or "less"
            or "javascript" or "js" or "jsx" or "mjs"
            or "typescript" or "ts" or "tsx" or "json"
            or "python" or "py" or "csharp" or "cs" or "java"
            or "go" or "rust" or "rs" or "cpp" or "c++" or "cc" or "cxx"
            or "c" or "xml" or "yaml" or "yml" or "toml" or "sql"
            or "sh" or "bash" or "shell"
            || clean.Contains("<!doctype html", StringComparison.OrdinalIgnoreCase)
            || clean.Contains("<html", StringComparison.OrdinalIgnoreCase);
    }
}
