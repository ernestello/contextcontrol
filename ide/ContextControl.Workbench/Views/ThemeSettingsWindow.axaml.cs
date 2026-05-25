using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ContextControl.Workbench.Services;
using ContextControl.Workbench.ViewModels;

namespace ContextControl.Workbench.Views;

public sealed partial class ThemeSettingsWindow : Window
{
    private const string AppearancePreviewSample = """
        public sealed class ThemeProbe
        {
            private static readonly (string Background, string Border, string Foreground)[] ExtensionColors =
            [
                ("#203A5A", "#2B5E8C", "#D9ECFF"),
                ("#2E4A2D", "#4D7A46", "#DEFFD9"),
                ("#443123", "#7D5238", "#FFE8D9"),
            ];

            public void Render(int index)
            {
                if (index < 0 || index >= ExtensionColors.Length)
                {
                    return;
                }

                var selected = ExtensionColors[index];
            }
        }
        """;

    private bool _summaryArrowOptionsExpanded = true;
    private ThemeOptionViewModel? _hoveredTheme;
    private ThemeOptionViewModel? _hoveredSyntaxTheme;
    private ThemeOptionViewModel? _hoveredCodeFont;
    private string? _pendingThemeKey;
    private string? _pendingUiFontFamily;
    private string? _pendingCodeFontFamily;
    private string? _pendingSkinKey;

    public ThemeSettingsWindow()
    {
        InitializeComponent();
        WorkbenchThemeResources.Apply(this, "empty");
        ShowAppearancePage();
        InitializeAppearancePreview();
        RefreshAppearancePreview();
    }

    public void ApplyTheme(string? themeKey, string? uiFontFamily = null, string? codeFontFamily = null, string? skinKey = null)
    {
        if (IsAnyOptionPickerOpen())
        {
            _pendingThemeKey = themeKey;
            _pendingUiFontFamily = uiFontFamily;
            _pendingCodeFontFamily = codeFontFamily;
            _pendingSkinKey = skinKey;
            return;
        }

        WorkbenchThemeResources.Apply(this, themeKey, uiFontFamily, codeFontFamily, skinKey: skinKey);
        RefreshAppearancePreview();
    }

    private WorkbenchViewModel? ViewModel => DataContext as WorkbenchViewModel;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        _hoveredTheme = null;
        _hoveredSyntaxTheme = null;
        _hoveredCodeFont = null;
        RefreshAppearancePreview();
    }

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximizeRestore();
            return;
        }

        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void OnMinimizeClick(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void OnMaximizeRestoreClick(object? sender, RoutedEventArgs e)
    {
        ToggleMaximizeRestore();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ToggleMaximizeRestore()
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void OnResizeLeftPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        BeginWindowResize(WindowEdge.West, e);
    }

    private void OnResizeRightPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        BeginWindowResize(WindowEdge.East, e);
    }

    private void OnResizeTopPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        BeginWindowResize(WindowEdge.North, e);
    }

    private void OnResizeBottomPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        BeginWindowResize(WindowEdge.South, e);
    }

    private void OnResizeTopLeftPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        BeginWindowResize(WindowEdge.NorthWest, e);
    }

    private void OnResizeTopRightPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        BeginWindowResize(WindowEdge.NorthEast, e);
    }

    private void OnResizeBottomLeftPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        BeginWindowResize(WindowEdge.SouthWest, e);
    }

    private void OnResizeBottomRightPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        BeginWindowResize(WindowEdge.SouthEast, e);
    }

    private void BeginWindowResize(WindowEdge edge, PointerPressedEventArgs e)
    {
        if (WindowState == WindowState.Maximized
            || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        BeginResizeDrag(edge, e);
        e.Handled = true;
    }

    private void OnAppearanceNavClick(object? sender, RoutedEventArgs e)
    {
        ShowAppearancePage();
    }

    private void OnFileRulesNavClick(object? sender, RoutedEventArgs e)
    {
        ShowFileRulesPage();
    }

    private void OnLlmsNavClick(object? sender, RoutedEventArgs e)
    {
        ShowLlmsPage();
    }

    private void ShowAppearancePage()
    {
        AppearancePage.IsVisible = true;
        FileRulesPage.IsVisible = false;
        LlmsPage.IsVisible = false;
        SetActive(AppearanceNavButton, true);
        SetActive(FileRulesNavButton, false);
        SetActive(LlmsNavButton, false);
    }

    private void ShowFileRulesPage()
    {
        AppearancePage.IsVisible = false;
        FileRulesPage.IsVisible = true;
        LlmsPage.IsVisible = false;
        SetActive(AppearanceNavButton, false);
        SetActive(FileRulesNavButton, true);
        SetActive(LlmsNavButton, false);
    }

    private void ShowLlmsPage()
    {
        AppearancePage.IsVisible = false;
        FileRulesPage.IsVisible = false;
        LlmsPage.IsVisible = true;
        SetActive(AppearanceNavButton, false);
        SetActive(FileRulesNavButton, false);
        SetActive(LlmsNavButton, true);
    }

    private static void SetActive(Button button, bool active)
    {
        if (active)
        {
            if (!button.Classes.Contains("active"))
            {
                button.Classes.Add("active");
            }

            return;
        }

        button.Classes.Remove("active");
    }

    private void OnSummaryArrowOptionsToggleClick(object? sender, RoutedEventArgs e)
    {
        _summaryArrowOptionsExpanded = !_summaryArrowOptionsExpanded;
        SummaryArrowOptionsPanel.IsVisible = _summaryArrowOptionsExpanded;
        SummaryArrowOptionsToggleButton.Content = _summaryArrowOptionsExpanded ? "v" : ">";
    }

    private void OnThemeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        RefreshAppearancePreview();
    }

    private void OnSkinSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        RefreshAppearancePreview();
    }

    private void OnSyntaxThemeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        RefreshAppearancePreview();
    }

    private void OnCodeFontSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        RefreshAppearancePreview();
    }

    private void OnUiFontSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        RefreshAppearancePreview();
    }

    private void OnOptionPickerDropDownClosed(object? sender, EventArgs e)
    {
        ClearHoveredOption(sender);
        ApplyPendingThemeIfNeeded();
        RefreshAppearancePreview();
    }

    private void OnThemeOptionPointerEntered(object? sender, PointerEventArgs e)
    {
        if (sender is Control { DataContext: ThemeOptionViewModel option })
        {
            _hoveredTheme = option;
            RefreshAppearancePreview();
        }
    }

    private void OnThemeOptionPointerExited(object? sender, PointerEventArgs e)
    {
        if (sender is Control { DataContext: ThemeOptionViewModel option }
            && IsSameOption(_hoveredTheme, option))
        {
            _hoveredTheme = null;
            RefreshAppearancePreview();
        }
    }

    private void OnSyntaxThemeOptionPointerEntered(object? sender, PointerEventArgs e)
    {
        if (sender is Control { DataContext: ThemeOptionViewModel option })
        {
            _hoveredSyntaxTheme = option;
            RefreshAppearancePreview();
        }
    }

    private void OnSyntaxThemeOptionPointerExited(object? sender, PointerEventArgs e)
    {
        if (sender is Control { DataContext: ThemeOptionViewModel option }
            && IsSameOption(_hoveredSyntaxTheme, option))
        {
            _hoveredSyntaxTheme = null;
            RefreshAppearancePreview();
        }
    }

    private void OnCodeFontOptionPointerEntered(object? sender, PointerEventArgs e)
    {
        if (sender is Control { DataContext: ThemeOptionViewModel option })
        {
            _hoveredCodeFont = option;
            RefreshAppearancePreview();
        }
    }

    private void OnCodeFontOptionPointerExited(object? sender, PointerEventArgs e)
    {
        if (sender is Control { DataContext: ThemeOptionViewModel option }
            && IsSameOption(_hoveredCodeFont, option))
        {
            _hoveredCodeFont = null;
            RefreshAppearancePreview();
        }
    }

    private void RefreshAppearancePreview()
    {
        if (AppearanceCodePreview is null || PreviewCaption is null)
        {
            return;
        }

        var selectedSkin = SkinPicker?.SelectedItem as ThemeOptionViewModel ?? ViewModel?.SelectedSkin;
        var skin = WorkbenchSkins.For(selectedSkin?.Key);
        var selectedTheme = ThemePicker?.SelectedItem as ThemeOptionViewModel ?? ViewModel?.SelectedTheme;
        var selectedSyntaxTheme = SyntaxThemePicker?.SelectedItem as ThemeOptionViewModel ?? ViewModel?.SelectedSyntaxTheme;
        var selectedCodeFont = CodeFontPicker?.SelectedItem as ThemeOptionViewModel ?? ViewModel?.SelectedCodeFont;
        var selectedUiFont = UiFontPicker?.SelectedItem as ThemeOptionViewModel ?? ViewModel?.SelectedUiFont;
        var previewTheme = _hoveredTheme ?? selectedTheme;
        var previewSyntaxTheme = _hoveredSyntaxTheme ?? selectedSyntaxTheme;
        var previewCodeFont = _hoveredCodeFont ?? selectedCodeFont;
        var previewThemeKey = skin.IsActive ? skin.ThemeKey : previewTheme?.Key ?? "empty";
        var previewSyntaxThemeKey = skin.IsActive ? skin.SyntaxThemeKey : previewSyntaxTheme?.Key ?? "adaptive";
        var previewCodeFontFamily = skin.IsActive ? skin.CodeFontFamily : previewCodeFont?.FontFamily ?? ViewModel?.CodeFontFamily ?? "avares://ContextControl.Workbench/Assets/Fonts#Cascadia Code, Consolas";
        var previewUiFontFamily = skin.IsActive ? skin.UiFontFamily : selectedUiFont?.FontFamily ?? ViewModel?.UiFontFamily;

        try
        {
            WorkbenchThemeResources.Apply(
                this,
                previewThemeKey,
                previewUiFontFamily,
                previewCodeFontFamily,
                updateThemeVariant: !IsAnyOptionPickerOpen(),
                skinKey: skin.Key);
        }
        catch
        {
            // A preview should never take down the settings window.
        }

        AppearanceCodePreview.ThemeKey = previewThemeKey;
        AppearanceCodePreview.SyntaxThemeKey = previewSyntaxThemeKey;
        AppearanceCodePreview.CodeFontFamily = previewCodeFontFamily;
        AppearanceCodePreview.SkinKey = skin.Key;
        PreviewCaption.Text = skin.IsActive
            ? $"{skin.Name}: {skin.ThemeName} + {skin.SyntaxThemeName} - {skin.CodeFontName} - {skin.UiFontName}"
            : $"{previewTheme?.DisplayName ?? "Porcelain"} + {previewSyntaxTheme?.Name ?? "Adaptive"} - {previewCodeFont?.Name ?? "Cascadia Code"} - {selectedUiFont?.Name ?? "Aptos"}";
    }

    private void ClearHoveredOption(object? picker)
    {
        if (ReferenceEquals(picker, ThemePicker))
        {
            _hoveredTheme = null;
        }
        else if (ReferenceEquals(picker, SyntaxThemePicker))
        {
            _hoveredSyntaxTheme = null;
        }
        else if (ReferenceEquals(picker, CodeFontPicker))
        {
            _hoveredCodeFont = null;
        }
    }

    private static bool IsSameOption(ThemeOptionViewModel? left, ThemeOptionViewModel right)
    {
        return ReferenceEquals(left, right)
            || string.Equals(left?.Key, right.Key, StringComparison.OrdinalIgnoreCase);
    }

    private bool IsAnyOptionPickerOpen()
    {
        return ThemePicker?.IsDropDownOpen == true
            || SkinPicker?.IsDropDownOpen == true
            || SyntaxThemePicker?.IsDropDownOpen == true
            || CodeFontPicker?.IsDropDownOpen == true
            || UiFontPicker?.IsDropDownOpen == true;
    }

    private void ApplyPendingThemeIfNeeded()
    {
        if (IsAnyOptionPickerOpen() || _pendingThemeKey is null)
        {
            return;
        }

        var theme = _pendingThemeKey;
        var uiFont = _pendingUiFontFamily;
        var codeFont = _pendingCodeFontFamily;
        var skin = _pendingSkinKey;
        _pendingThemeKey = null;
        _pendingUiFontFamily = null;
        _pendingCodeFontFamily = null;
        _pendingSkinKey = null;

        Dispatcher.UIThread.Post(() => ApplyTheme(theme, uiFont, codeFont, skin));
    }

    private void InitializeAppearancePreview()
    {
        AppearanceCodePreview.DocumentPath = "ThemePreview.cs";
        AppearanceCodePreview.DocumentText = AppearancePreviewSample;
    }

    private async void OnEditRuleListClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { Tag: string kind })
        {
            return;
        }

        await OpenRuleListEditorAsync(kind, null);
    }

    private async void OnEditRuleEntryClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { Tag: FileRuleEntryViewModel entry })
        {
            return;
        }

        await OpenRuleListEditorAsync(entry.Kind, entry.Value);
    }

    private async Task OpenRuleListEditorAsync(string kind, string? focusValue)
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        var editor = new FileRuleListEditorWindow(
            viewModel.GetFileRuleTitle(kind),
            viewModel.GetFileRuleEntries(kind),
            viewModel.GetFileRuleWatermark(kind),
            focusValue);
        editor.ApplyTheme(viewModel.ThemeKey, viewModel.UiFontFamily, viewModel.CodeFontFamily, viewModel.SkinKey);

        var saved = await editor.ShowDialog<bool>(this);
        if (saved)
        {
            viewModel.ReplaceFileRuleEntries(kind, editor.Values);
        }
    }

}
