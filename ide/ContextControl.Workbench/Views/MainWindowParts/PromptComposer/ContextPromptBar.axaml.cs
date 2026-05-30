using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace ContextControl.Workbench.Views.MainWindowParts;

public sealed partial class ContextPromptBar : UserControl
{
    public ContextPromptBar()
    {
        InitializeComponent();
    }

    internal TextBox PromptTextBox => ContextPromptTextBox;

    private MainWindow? OwnerWindow => this.FindAncestorOfType<MainWindow>();

    private void OnPromptTextBoxGotFocus(object? sender, GotFocusEventArgs e) => OwnerWindow?.OnPromptTextBoxGotFocus(sender, e);

    private void OnPromptTextBoxLostFocus(object? sender, RoutedEventArgs e) => OwnerWindow?.OnPromptTextBoxLostFocus(sender, e);

    private void OnPromptTextBoxKeyDown(object? sender, KeyEventArgs e) => OwnerWindow?.OnPromptTextBoxKeyDown(sender, e);

    private void OnPromptTextBoxTextChanged(object? sender, TextChangedEventArgs e) => OwnerWindow?.OnPromptTextBoxTextChanged(sender, e);
}
