using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace ContextControl.Workbench.Services;

public static class WorkbenchThemeResources
{
    public static void Apply(Window window, string? themeKey)
    {
        var palette = ThemePalette.For(themeKey);
        window.RequestedThemeVariant = palette.IsDark ? ThemeVariant.Dark : ThemeVariant.Light;

        window.Resources["UiFontFamily"] = new FontFamily("Aptos, Segoe UI Variable Text, Segoe UI, sans-serif");
        window.Resources["CodeFontFamily"] = new FontFamily("Cascadia Code, Cascadia Mono, Consolas, monospace");
        Set(window, "AppBackgroundBrush", palette.AppBackground);
        Set(window, "PanelBackgroundBrush", palette.PanelBackground);
        Set(window, "PanelBorderBrush", palette.PanelBorder);
        Set(window, "HeaderBackgroundBrush", palette.HeaderBackground);
        Set(window, "TextPrimaryBrush", palette.TextPrimary);
        Set(window, "TextMutedBrush", palette.TextMuted);
        Set(window, "CommandBackgroundBrush", palette.CommandBackground);
        Set(window, "CommandBorderBrush", palette.CommandBorder);
        Set(window, "CommandPrimaryBackgroundBrush", palette.CommandPrimaryBackground);
        Set(window, "AccentBrush", palette.Accent);
        Set(window, "AccentBorderBrush", palette.AccentBorder);
        Set(window, "ProjectTileBackgroundBrush", palette.ProjectTileBackground);
        Set(window, "ProjectTileActiveBrush", palette.ProjectTileActive);
        Set(window, "DirectoryHighlightBrush", palette.DirectoryHighlight);
        Set(window, "CurrentRowBrush", palette.CurrentRow);
        Set(window, "CurrentRowBorderBrush", palette.CurrentRowBorder);
        Set(window, "FolderTextBrush", palette.FolderText);
        Set(window, "FileTextBrush", palette.FileText);
        Set(window, "ExternalTextBrush", palette.ExternalText);
        Set(window, "NodeTextBrush", palette.NodeText);
        Set(window, "SkipTextBrush", palette.SkipText);
        Set(window, "EditorSurfaceBrush", palette.EditorSurface);
        Set(window, "HistoryHoverBrush", palette.HistoryHover);
        Set(window, "HistoryActiveBrush", palette.HistoryActive);
        Set(window, "GoodBrush", palette.Good);
        Set(window, "BadBrush", palette.Bad);
        Set(window, "IncludeBackgroundBrush", palette.IncludeBackground);
        Set(window, "IncludeBorderBrush", palette.IncludeBorder);
        Set(window, "IncludeTextBrush", palette.IncludeText);
        Set(window, "SettingsSurfaceBrush", palette.SettingsSurface);
        Set(window, "ScopePinBackgroundBrush", palette.Accent);
        Set(window, "ScopePinBorderBrush", palette.AccentBorder);
        Set(window, "MetricFileBrush", palette.Accent);
        Set(window, "MetricLocBrush", palette.IncludeText);
    }

    private static void Set(Window window, string key, string color)
    {
        window.Resources[key] = new SolidColorBrush(Color.Parse(color));
    }

    private sealed record ThemePalette(
        bool IsDark,
        string AppBackground,
        string PanelBackground,
        string PanelBorder,
        string HeaderBackground,
        string TextPrimary,
        string TextMuted,
        string CommandBackground,
        string CommandBorder,
        string CommandPrimaryBackground,
        string Accent,
        string AccentBorder,
        string ProjectTileBackground,
        string ProjectTileActive,
        string DirectoryHighlight,
        string CurrentRow,
        string CurrentRowBorder,
        string FolderText,
        string FileText,
        string ExternalText,
        string NodeText,
        string SkipText,
        string EditorSurface,
        string HistoryHover,
        string HistoryActive,
        string Good,
        string Bad,
        string IncludeBackground,
        string IncludeBorder,
        string IncludeText,
        string SettingsSurface)
    {
        public static ThemePalette For(string? themeKey)
        {
            return themeKey?.ToLowerInvariant() switch
            {
                "dark" => Dark,
                "matrix" => Matrix,
                _ => Empty
            };
        }

        private static readonly ThemePalette Empty = new(
            false,
            "#EEF2F2",
            "#FAFBF8",
            "#CBD6D7",
            "#F4F7F6",
            "#1F2629",
            "#687579",
            "#FEFEFA",
            "#B8C5C7",
            "#E3F2F1",
            "#0D6B72",
            "#8BBCC0",
            "#EEF3F1",
            "#FFFFFF",
            "#ECF4F1",
            "#E2F1EE",
            "#87B9B5",
            "#172225",
            "#354147",
            "#84785E",
            "#536166",
            "#8B9698",
            "#FFFFFF",
            "#EFF6F5",
            "#DCEBE8",
            "#1E7F57",
            "#B24A42",
            "#FFF7E2",
            "#D7B965",
            "#7A5A17",
            "#FFFFFF");

        private static readonly ThemePalette Dark = new(
            true,
            "#0D1113",
            "#151A1D",
            "#2A363A",
            "#111618",
            "#E2E8EA",
            "#8A989D",
            "#1A2124",
            "#354247",
            "#142A2E",
            "#6BD3D1",
            "#2E7478",
            "#192124",
            "#20292C",
            "#192423",
            "#173336",
            "#327E80",
            "#F0F6F7",
            "#D8E0E2",
            "#B7AA82",
            "#BBC8CB",
            "#DDBB69",
            "#0B0E10",
            "#1E2829",
            "#173336",
            "#73D59B",
            "#FF7B72",
            "#2B2519",
            "#896B31",
            "#E3B75E",
            "#171D20");

        private static readonly ThemePalette Matrix = new(
            true,
            "#030807",
            "#07110F",
            "#17372F",
            "#04100D",
            "#CCFFE7",
            "#78BFA7",
            "#091915",
            "#1C604F",
            "#102922",
            "#65F0B2",
            "#2AA979",
            "#0A1A16",
            "#0D241D",
            "#0A1B16",
            "#103329",
            "#2AA979",
            "#E2FFF2",
            "#CCFFE7",
            "#8FD9B7",
            "#A7F0CC",
            "#D7F77A",
            "#020604",
            "#0C1E18",
            "#102922",
            "#6AFFB9",
            "#FF7979",
            "#14200A",
            "#66A640",
            "#D7F77A",
            "#06140F");
    }
}
