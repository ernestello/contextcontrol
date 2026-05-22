using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ContextControl.Workbench.Services;
using ContextControl.Workbench.ViewModels;

namespace ContextControl.Workbench.Views;

public sealed partial class ThemeSettingsWindow : Window
{
    private bool _contextParseMode;
    private ThemeOptionViewModel? _hoveredTheme;
    private ThemeOptionViewModel? _hoveredSyntaxTheme;

    public ThemeSettingsWindow()
    {
        InitializeComponent();
        WorkbenchThemeResources.Apply(this, "empty");
        ShowAppearancePage();
        RefreshAppearancePreview();
    }

    public void ApplyTheme(string? themeKey)
    {
        WorkbenchThemeResources.Apply(this, themeKey);
        RefreshAppearancePreview();
    }

    private WorkbenchViewModel? ViewModel => DataContext as WorkbenchViewModel;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        _hoveredTheme = null;
        _hoveredSyntaxTheme = null;
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

    private void OnAppearanceNavClick(object? sender, RoutedEventArgs e)
    {
        ShowAppearancePage();
    }

    private void OnFileRulesNavClick(object? sender, RoutedEventArgs e)
    {
        ShowFileRulesPage();
    }

    private void ShowAppearancePage()
    {
        AppearancePage.IsVisible = true;
        FileRulesPage.IsVisible = false;
        SetActive(AppearanceNavButton, true);
        SetActive(FileRulesNavButton, false);
    }

    private void ShowFileRulesPage()
    {
        AppearancePage.IsVisible = false;
        FileRulesPage.IsVisible = true;
        SetActive(AppearanceNavButton, false);
        SetActive(FileRulesNavButton, true);
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

    private void OnThemeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        _hoveredTheme = null;
        RefreshAppearancePreview();
    }

    private void OnSyntaxThemeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        _hoveredSyntaxTheme = null;
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

    private void RefreshAppearancePreview()
    {
        var selectedTheme = ThemePicker?.SelectedItem as ThemeOptionViewModel ?? ViewModel?.SelectedTheme;
        var selectedSyntaxTheme = SyntaxThemePicker?.SelectedItem as ThemeOptionViewModel ?? ViewModel?.SelectedSyntaxTheme;
        var previewTheme = _hoveredTheme ?? selectedTheme;
        var previewSyntaxTheme = _hoveredSyntaxTheme ?? selectedSyntaxTheme;

        AppearanceSyntaxPreview.ThemeKey = previewTheme?.Key ?? "empty";
        AppearanceSyntaxPreview.SyntaxThemeKey = previewSyntaxTheme?.Key ?? "adaptive";
        PreviewCaption.Text = $"{previewTheme?.Name ?? "Porcelain"} + {previewSyntaxTheme?.Name ?? "Adaptive"}";
    }

    private static bool IsSameOption(ThemeOptionViewModel? left, ThemeOptionViewModel right)
    {
        return ReferenceEquals(left, right)
            || string.Equals(left?.Key, right.Key, StringComparison.OrdinalIgnoreCase);
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
        editor.ApplyTheme(viewModel.ThemeKey);

        var saved = await editor.ShowDialog<bool>(this);
        if (saved)
        {
            viewModel.ReplaceFileRuleEntries(kind, editor.Values);
        }
    }

    private void OnSettingsContextToggleClick(object? sender, RoutedEventArgs e)
    {
        _contextParseMode = !_contextParseMode;
        ParseAppearanceButton.IsVisible = _contextParseMode;
        ParseFileRulesButton.IsVisible = _contextParseMode;
    }

    private async void OnSettingsContextScopeClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { Tag: string scope } || ViewModel is not { } viewModel)
        {
            return;
        }

        var payload = BuildSettingsContextPayload(viewModel, scope);
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is not null)
        {
            await clipboard.SetTextAsync(payload);
        }

        Title = $"Settings - copied {scope}";
    }

    private static string BuildSettingsContextPayload(WorkbenchViewModel viewModel, string scope)
    {
        return scope switch
        {
            "settings.appearance" =>
                "Context Control settings scope: Appearance" + Environment.NewLine
                + $"Settings file: {viewModel.AppearanceSettingsPath}" + Environment.NewLine
                + $"Current IDE theme: {viewModel.SelectedTheme.Name} ({viewModel.SelectedTheme.Key})" + Environment.NewLine
                + $"Current syntax theme: {viewModel.SelectedSyntaxTheme.Name} ({viewModel.SelectedSyntaxTheme.Key})" + Environment.NewLine
                + "Available IDE themes: " + string.Join(", ", viewModel.Themes.Select(theme => $"{theme.Name}/{theme.Key}")) + Environment.NewLine
                + "Available syntax themes: " + string.Join(", ", viewModel.SyntaxThemes.Select(theme => $"{theme.Name}/{theme.Key}")) + Environment.NewLine
                + "Primary files: Views/ThemeSettingsWindow.axaml, Views/ThemeSettingsWindow.axaml.cs, Controls/SyntaxPreviewControl.cs, Services/WorkbenchThemeResources.cs, ViewModels/WorkbenchViewModel.cs",
            "settings.fileRules" =>
                "Context Control settings scope: File rules" + Environment.NewLine
                + $"Rules file: {viewModel.FileRulesPath}" + Environment.NewLine
                + $"Summary: {viewModel.FileRulesSummary}" + Environment.NewLine
                + "Skipped folders: " + string.Join(", ", viewModel.GetFileRuleEntries(WorkbenchViewModel.RuleKindIgnoredDirectories)) + Environment.NewLine
                + "Skipped files: " + string.Join(", ", viewModel.GetFileRuleEntries(WorkbenchViewModel.RuleKindIgnoredFileNames)) + Environment.NewLine
                + "Skipped file types: " + string.Join(", ", viewModel.GetFileRuleEntries(WorkbenchViewModel.RuleKindIgnoredFileTypes)) + Environment.NewLine
                + "Allowed file types: " + string.Join(", ", viewModel.GetFileRuleEntries(WorkbenchViewModel.RuleKindSupportedFileTypes)) + Environment.NewLine
                + "Primary files: Services/ProjectFileRules.cs, ViewModels/WorkbenchViewModel.cs, Views/ThemeSettingsWindow.axaml, Views/FileRuleListEditorWindow.cs",
            _ => "Context Control settings scope: " + scope
        };
    }
}
