using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace ContextControl.Workbench.Views.ThemeSettingsWindowParts;

public sealed partial class LlmsSettingsPage : UserControl
{
    public LlmsSettingsPage()
    {
        InitializeComponent();
    }

    private ThemeSettingsWindow? OwnerWindow => this.FindAncestorOfType<ThemeSettingsWindow>();

    private void OnOllamaModelsBrowseClick(object? sender, RoutedEventArgs e) => OwnerWindow?.OnOllamaModelsBrowseClick(sender, e);
}
