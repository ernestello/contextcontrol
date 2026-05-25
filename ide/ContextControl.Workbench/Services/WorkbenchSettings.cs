// CC-DESC: Persists Workbench appearance and Context Control preferences between app runs.

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ContextControl.Workbench.Services;

public sealed class WorkbenchSettings
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

    private WorkbenchSettings(
        string settingsPath,
        string contextControlRoot,
        string skinKey,
        string themeKey,
        string syntaxThemeKey,
        string codeFontKey,
        string uiFontKey,
        string foldArrowPositionKey,
        bool showFoldArrows,
        bool showSummaryArrowBorders,
        bool useParentChildArrowIndentation,
        bool showVerticalScopeLines,
        string summaryFoldKinds,
        bool useColorfulFamilies,
        bool showAppearanceCodePreview,
        string selectedAiRoute,
        string selectedLocalModel,
        string fileRequestModel,
        string patchWriteModel,
        string patchReviewModel,
        string chatModel,
        string promptModeKey,
        bool promptBarOpenByDefault,
        string workspaceModeKey,
        string externalBrowserKey,
        bool showSkippedFiles,
        bool showProjectFilesPane,
        bool showBrowserRoutingPane,
        bool showProjectGraphTreePane)
    {
        SettingsPath = settingsPath;
        ContextControlRoot = contextControlRoot;
        SkinKey = NormalizeSkinKey(skinKey);
        ThemeKey = NormalizeKey(themeKey, "empty");
        SyntaxThemeKey = NormalizeKey(syntaxThemeKey, "adaptive");
        CodeFontKey = NormalizeKey(codeFontKey, "cascadia-code");
        UiFontKey = NormalizeKey(uiFontKey, "aptos");
        FoldArrowPositionKey = NormalizeFoldArrowPositionKey(foldArrowPositionKey);
        ShowFoldArrows = showFoldArrows;
        ShowSummaryArrowBorders = showSummaryArrowBorders;
        UseParentChildArrowIndentation = useParentChildArrowIndentation;
        ShowVerticalScopeLines = showVerticalScopeLines;
        SummaryFoldKinds = NormalizeSummaryFoldKinds(summaryFoldKinds);
        UseColorfulFamilies = useColorfulFamilies;
        ShowAppearanceCodePreview = showAppearanceCodePreview;
        SelectedAiRoute = string.IsNullOrWhiteSpace(selectedAiRoute) ? "Browser: ChatGPT" : selectedAiRoute.Trim();
        SelectedLocalModel = string.IsNullOrWhiteSpace(selectedLocalModel) ? "qwen2.5-coder:3b" : selectedLocalModel.Trim();
        FileRequestModel = NormalizeModelId(fileRequestModel, "qwen2.5-coder:1.5b");
        PatchWriteModel = NormalizeModelId(patchWriteModel, "qwen2.5-coder:3b");
        PatchReviewModel = NormalizeModelId(patchReviewModel, "phi4-mini");
        ChatModel = NormalizeModelId(chatModel, "qwen2.5-coder:3b");
        PromptModeKey = NormalizePromptModeKey(promptModeKey);
        PromptBarOpenByDefault = promptBarOpenByDefault;
        WorkspaceModeKey = NormalizeWorkspaceModeKey(workspaceModeKey);
        ExternalBrowserKey = NormalizeExternalBrowserKey(externalBrowserKey);
        ShowSkippedFiles = showSkippedFiles;
        ShowProjectFilesPane = showProjectFilesPane;
        ShowBrowserRoutingPane = showBrowserRoutingPane;
        ShowProjectGraphTreePane = showProjectGraphTreePane;
    }

    public string SettingsPath { get; }
    public string ContextControlRoot { get; }
    public string SkinKey { get; set; }
    public string ThemeKey { get; set; }
    public string SyntaxThemeKey { get; set; }
    public string CodeFontKey { get; set; }
    public string UiFontKey { get; set; }
    public string FoldArrowPositionKey { get; set; }
    public bool ShowFoldArrows { get; set; }
    public bool ShowSummaryArrowBorders { get; set; }
    public bool UseParentChildArrowIndentation { get; set; }
    public bool ShowVerticalScopeLines { get; set; }
    public string SummaryFoldKinds { get; set; }
    public bool UseColorfulFamilies { get; set; }
    public bool ShowAppearanceCodePreview { get; set; }
    public string SelectedAiRoute { get; set; }
    public string SelectedLocalModel { get; set; }
    public string FileRequestModel { get; set; }
    public string PatchWriteModel { get; set; }
    public string PatchReviewModel { get; set; }
    public string ChatModel { get; set; }
    public string PromptModeKey { get; set; }
    public bool PromptBarOpenByDefault { get; set; }
    public string WorkspaceModeKey { get; set; }
    public string ExternalBrowserKey { get; set; }
    public bool ShowSkippedFiles { get; set; }
    public bool ShowProjectFilesPane { get; set; }
    public bool ShowBrowserRoutingPane { get; set; }
    public bool ShowProjectGraphTreePane { get; set; }

    public static WorkbenchSettings Load()
    {
        var contextControlRoot = ResolveContextControlRoot();
        var settingsPath = Path.Combine(contextControlRoot, ".ccWorkbench.settings.json");
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
            contextControlRoot,
            data.SkinKey ?? WorkbenchSkins.DefaultKey,
            data.ThemeKey ?? "empty",
            data.SyntaxThemeKey ?? "adaptive",
            data.CodeFontKey ?? "cascadia-code",
            data.UiFontKey ?? "aptos",
            data.FoldArrowPositionKey ?? "codeEditor",
            data.ShowFoldArrows ?? true,
            data.ShowSummaryArrowBorders ?? (data.UseColorfulFamilies ?? true),
            data.UseParentChildArrowIndentation ?? true,
            data.ShowVerticalScopeLines ?? true,
            data.SummaryFoldKinds ?? DefaultSummaryFoldKinds,
            data.UseColorfulFamilies ?? true,
            data.ShowAppearanceCodePreview ?? true,
            data.SelectedAiRoute ?? "Browser: ChatGPT",
            data.SelectedLocalModel ?? "qwen2.5-coder:3b",
            data.FileRequestModel ?? "qwen2.5-coder:1.5b",
            data.PatchWriteModel ?? "qwen2.5-coder:3b",
            data.PatchReviewModel ?? "phi4-mini",
            data.ChatModel ?? "qwen2.5-coder:3b",
            data.PromptModeKey ?? "context",
            data.PromptBarOpenByDefault ?? false,
            data.WorkspaceModeKey ?? "code",
            data.ExternalBrowserKey ?? "default",
            data.ShowSkippedFiles ?? false,
            data.ShowProjectFilesPane ?? true,
            data.ShowBrowserRoutingPane ?? true,
            data.ShowProjectGraphTreePane ?? true);
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
            SkinKey = NormalizeSkinKey(SkinKey),
            ThemeKey = NormalizeKey(ThemeKey, "empty"),
            SyntaxThemeKey = NormalizeKey(SyntaxThemeKey, "adaptive"),
            CodeFontKey = NormalizeKey(CodeFontKey, "cascadia-code"),
            UiFontKey = NormalizeKey(UiFontKey, "aptos"),
            FoldArrowPositionKey = NormalizeFoldArrowPositionKey(FoldArrowPositionKey),
            ShowFoldArrows = ShowFoldArrows,
            ShowSummaryArrowBorders = ShowSummaryArrowBorders,
            UseParentChildArrowIndentation = UseParentChildArrowIndentation,
            ShowVerticalScopeLines = ShowVerticalScopeLines,
            SummaryFoldKinds = NormalizeSummaryFoldKinds(SummaryFoldKinds),
            UseColorfulFamilies = UseColorfulFamilies,
            ShowAppearanceCodePreview = ShowAppearanceCodePreview,
            SelectedAiRoute = string.IsNullOrWhiteSpace(SelectedAiRoute) ? "Browser: ChatGPT" : SelectedAiRoute.Trim(),
            SelectedLocalModel = string.IsNullOrWhiteSpace(SelectedLocalModel) ? "qwen2.5-coder:3b" : SelectedLocalModel.Trim(),
            FileRequestModel = NormalizeModelId(FileRequestModel, "qwen2.5-coder:1.5b"),
            PatchWriteModel = NormalizeModelId(PatchWriteModel, "qwen2.5-coder:3b"),
            PatchReviewModel = NormalizeModelId(PatchReviewModel, "phi4-mini"),
            ChatModel = NormalizeModelId(ChatModel, "qwen2.5-coder:3b"),
            PromptModeKey = NormalizePromptModeKey(PromptModeKey),
            PromptBarOpenByDefault = PromptBarOpenByDefault,
            WorkspaceModeKey = NormalizeWorkspaceModeKey(WorkspaceModeKey),
            ExternalBrowserKey = NormalizeExternalBrowserKey(ExternalBrowserKey),
            ShowSkippedFiles = ShowSkippedFiles,
            ShowProjectFilesPane = ShowProjectFilesPane,
            ShowBrowserRoutingPane = ShowBrowserRoutingPane,
            ShowProjectGraphTreePane = ShowProjectGraphTreePane
        };
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(data, JsonOptions) + Environment.NewLine, Utf8NoBom);
    }

    private static string ResolveContextControlRoot()
    {
        return FindContextControlRoot(Directory.GetCurrentDirectory())
            ?? FindContextControlRoot(AppContext.BaseDirectory)
            ?? AppContext.BaseDirectory;
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

    private static string NormalizeSkinKey(string? key)
    {
        return WorkbenchSkins.For(key).Key;
    }

    private static string NormalizeFoldArrowPositionKey(string? key)
    {
        return string.Equals(key?.Trim(), "locBlock", StringComparison.OrdinalIgnoreCase)
            ? "locBlock"
            : "codeEditor";
    }

    private static string NormalizeWorkspaceModeKey(string? key)
    {
        return key?.Trim().ToLowerInvariant() switch
        {
            "browser" => "browser",
            "graph" => "graph",
            "llms" => "llms",
            "chat" => "chat",
            "skillbook" => "skillbook",
            "scanner" => "scanner",
            _ => "code"
        };
    }

    private static string NormalizePromptModeKey(string? key)
    {
        return string.Equals(key?.Trim(), "terminal", StringComparison.OrdinalIgnoreCase)
            ? "terminal"
            : "context";
    }

    private static string NormalizeModelId(string? modelId, string fallback)
    {
        return string.IsNullOrWhiteSpace(modelId) ? fallback : modelId.Trim();
    }

    private static string NormalizeExternalBrowserKey(string? key)
    {
        return string.IsNullOrWhiteSpace(key) ? "default" : key.Trim();
    }

    private const string DefaultSummaryFoldKinds = "namespace,class,struct,interface,enum,method,property,object,block,array,arguments";

    private static string NormalizeSummaryFoldKinds(string? value)
    {
        if (value is null)
        {
            return DefaultSummaryFoldKinds;
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var allowed = DefaultSummaryFoldKinds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var selected = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(allowed.Contains)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return selected.Length == 0 ? string.Empty : string.Join(",", selected.Select(item => item.ToLowerInvariant()));
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private sealed class WorkbenchSettingsJson
    {
        public string? ThemeKey { get; set; }
        public string? SkinKey { get; set; }
        public string? SyntaxThemeKey { get; set; }
        public string? CodeFontKey { get; set; }
        public string? UiFontKey { get; set; }
        public string? FoldArrowPositionKey { get; set; }
        public bool? ShowFoldArrows { get; set; }
        public bool? ShowSummaryArrowBorders { get; set; }
        public bool? UseParentChildArrowIndentation { get; set; }
        public bool? ShowVerticalScopeLines { get; set; }
        public string? SummaryFoldKinds { get; set; }
        public bool? UseColorfulFamilies { get; set; }
        public bool? ShowAppearanceCodePreview { get; set; }
        public string? SelectedAiRoute { get; set; }
        public string? SelectedLocalModel { get; set; }
        public string? FileRequestModel { get; set; }
        public string? PatchWriteModel { get; set; }
        public string? PatchReviewModel { get; set; }
        public string? ChatModel { get; set; }
        public string? PromptModeKey { get; set; }
        public bool? PromptBarOpenByDefault { get; set; }
        public string? WorkspaceModeKey { get; set; }
        public string? ExternalBrowserKey { get; set; }
        public bool? ShowSkippedFiles { get; set; }
        public bool? ShowProjectFilesPane { get; set; }
        public bool? ShowBrowserRoutingPane { get; set; }
        public bool? ShowProjectGraphTreePane { get; set; }
    }
}
