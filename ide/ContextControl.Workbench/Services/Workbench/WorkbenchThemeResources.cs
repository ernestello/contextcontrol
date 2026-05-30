using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;

namespace ContextControl.Workbench.Services;

public static partial class WorkbenchThemeResources
{
    private const string DefaultUiFontFamily = "fonts:Inter, Segoe UI";
    private const string DefaultCodeFontFamily = "avares://ContextControl.Workbench/Assets/Fonts#Cascadia Code, Consolas";
    private static readonly Uri OriginalAppIcon64 = new("avares://ContextControl.Workbench/Assets/Icons/contextcontrol64x64.png");
    private static readonly Uri OriginalAppIcon32 = new("avares://ContextControl.Workbench/Assets/Icons/contextcontrol32x32.png");
    private static readonly Uri LightAppIcon64 = new("avares://ContextControl.Workbench/Assets/Icons/contextcontrol64x64light.png");
    private static readonly Uri LightAppIcon32 = new("avares://ContextControl.Workbench/Assets/Icons/contextcontrol32x32light.png");
    private static readonly Lazy<Bitmap> OriginalMicroIcon = new(() => LoadBitmap(OriginalAppIcon32));
    private static readonly Lazy<Bitmap> LightMicroIcon = new(() => LoadBitmap(LightAppIcon32));

    public static void Apply(
        Window window,
        string? themeKey,
        string? uiFontFamily = null,
        string? codeFontFamily = null,
        bool updateThemeVariant = true,
        string? skinKey = null,
        string? uiFontColorModeKey = null,
        string? customUiFontColor = null,
        bool themeAdaptFileCountColor = false,
        bool themeAdaptLocColor = false,
        bool themeAdaptVersionColor = false,
        bool themeAdaptBytesColor = false)
    {
        var skin = WorkbenchSkins.For(skinKey);
        if (skin.IsActive)
        {
            themeKey = skin.ThemeKey;
            uiFontFamily = skin.UiFontFamily;
            codeFontFamily = skin.CodeFontFamily;
        }

        SetClass(window, "matrix-console-skin", skin.IsMatrixConsole);
        window.Resources["SkinKey"] = skin.Key;
        var palette = ThemePalette.For(themeKey);
        if (updateThemeVariant)
        {
            window.RequestedThemeVariant = palette.IsDark ? ThemeVariant.Dark : ThemeVariant.Light;
        }

        window.Resources["UiFontFamily"] = CreateFontFamily(uiFontFamily, DefaultUiFontFamily, "Segoe UI");
        window.Resources["CodeFontFamily"] = CreateFontFamily(codeFontFamily, DefaultCodeFontFamily, "Consolas");
        Set(window, "AppBackgroundBrush", palette.AppBackground);
        Set(window, "PanelBackgroundBrush", palette.PanelBackground);
        Set(window, "PanelBorderBrush", palette.PanelBorder);
        Set(window, "HeaderBackgroundBrush", palette.HeaderBackground);
        Set(window, "TitleBarBackgroundBrush", palette.TitleBarBackground);
        Set(window, "TitleBarBorderBrush", palette.TitleBarBorder);
        var textPrimary = Color.Parse(palette.TextPrimary);
        var textMuted = Color.Parse(palette.TextMuted);
        if (string.Equals(uiFontColorModeKey?.Trim(), "custom", StringComparison.OrdinalIgnoreCase)
            && TryParseColor(customUiFontColor, out var customText))
        {
            textPrimary = customText;
            textMuted = Blend(customText, Color.Parse(palette.PanelBackground), 0.58);
        }

        Set(window, "TextPrimaryBrush", textPrimary);
        Set(window, "TextMutedBrush", textMuted);
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
        if (string.Equals(uiFontColorModeKey?.Trim(), "custom", StringComparison.OrdinalIgnoreCase))
        {
            Set(window, "FolderTextBrush", textPrimary);
            Set(window, "FileTextBrush", Blend(textPrimary, Color.Parse(palette.PanelBackground), 0.82));
            Set(window, "NodeTextBrush", Blend(textPrimary, Color.Parse(palette.PanelBackground), 0.72));
        }
        Set(window, "EditorSurfaceBrush", palette.EditorSurface);
        Set(window, "HistoryHoverBrush", palette.HistoryHover);
        Set(window, "HistoryActiveBrush", palette.HistoryActive);
        Set(window, "GoodBrush", palette.Good);
        Set(window, "BadBrush", palette.Bad);
        Set(window, "FixedGoodBrush", "#2FA36B");
        Set(window, "FixedBadBrush", "#D95D5D");
        Set(window, "IncludeBackgroundBrush", palette.IncludeBackground);
        Set(window, "IncludeBorderBrush", palette.IncludeBorder);
        Set(window, "IncludeTextBrush", palette.IncludeText);
        Set(window, "SettingsSurfaceBrush", palette.SettingsSurface);
        Set(window, "DropdownBackgroundBrush", palette.DropdownBackground);
        Set(window, "DropdownBorderBrush", palette.DropdownBorder);
        Set(window, "DropdownHoverBrush", palette.DropdownHover);
        Set(window, "DropdownSelectedBrush", palette.DropdownSelected);
        Set(window, "ScopePinBackgroundBrush", palette.Accent);
        Set(window, "ScopePinBorderBrush", palette.AccentBorder);
        Set(window, "MetricFileBrush", palette.Accent);
        Set(window, "MetricLocBrush", palette.IncludeText);
        Set(window, "TreeFileFixedBrush", "#355A86");
        Set(window, "TreeLocFixedBrush", "#D89042");
        Set(window, "TreeVersionFixedBrush", "#858B91");
        Set(window, "TreeBytesFixedBrush", "#858B91");
        Set(window, "MetricFileDisplayBrush", themeAdaptFileCountColor ? palette.Accent : "#355A86");
        Set(window, "MetricLocDisplayBrush", themeAdaptLocColor ? palette.IncludeText : "#D89042");
        Set(window, "MetricVersionDisplayBrush", themeAdaptVersionColor ? palette.TextMuted : "#858B91");
        Set(window, "MetricBytesDisplayBrush", themeAdaptBytesColor ? palette.TextMuted : "#858B91");
        ApplyIconResources(window, palette.UseLightIcons);
    }

    private static void SetClass(Window window, string className, bool enabled)
    {
        if (enabled)
        {
            if (!window.Classes.Contains(className))
            {
                window.Classes.Add(className);
            }

            return;
        }

        window.Classes.Remove(className);
    }

    private static void Set(Window window, string key, string color)
    {
        window.Resources[key] = new SolidColorBrush(Color.Parse(color));
    }

    private static void Set(Window window, string key, Color color)
    {
        window.Resources[key] = new SolidColorBrush(color);
    }

    private static bool TryParseColor(string? value, out Color color)
    {
        try
        {
            color = Color.Parse(string.IsNullOrWhiteSpace(value) ? "#DDE6E8" : value.Trim());
            return true;
        }
        catch
        {
            color = default;
            return false;
        }
    }

    private static Color Blend(Color source, Color target, double sourceWeight)
    {
        sourceWeight = Math.Clamp(sourceWeight, 0, 1);
        var targetWeight = 1 - sourceWeight;
        return Color.FromRgb(
            (byte)Math.Round(source.R * sourceWeight + target.R * targetWeight),
            (byte)Math.Round(source.G * sourceWeight + target.G * targetWeight),
            (byte)Math.Round(source.B * sourceWeight + target.B * targetWeight));
    }

    private static string NormalizeFontFamily(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static FontFamily CreateFontFamily(string? value, string fallback, string finalFallback)
    {
        try
        {
            return new FontFamily(NormalizeFontFamily(value, fallback));
        }
        catch
        {
            return new FontFamily(finalFallback);
        }
    }

    private static Bitmap LoadBitmap(Uri uri)
    {
        return new Bitmap(AssetLoader.Open(uri));
    }

    private static void ApplyIconResources(Window window, bool useLightIcons)
    {
        var icon64 = useLightIcons ? LightAppIcon64 : OriginalAppIcon64;
        var icon32 = useLightIcons ? LightMicroIcon.Value : OriginalMicroIcon.Value;
        window.Resources["AppMicroIconImage"] = icon32;

        try
        {
            window.Icon = new WindowIcon(AssetLoader.Open(icon64));
        }
        catch
        {
            // A missing icon should not prevent the workbench from opening.
        }
    }

}
