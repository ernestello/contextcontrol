using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace ContextControl.Workbench.Views.MainWindowParts;

public sealed partial class ConversationPage : UserControl
{
    private const double CollapsedChatHistoryWidth = 10.0;
    private const double ExpandedChatHistoryWidth = 220.0;

    public ConversationPage()
    {
        InitializeComponent();
    }

    private MainWindow? OwnerWindow => this.FindAncestorOfType<MainWindow>();

    private void OnChatHistoryHoverEntered(object? sender, PointerEventArgs e)
    {
        ChatHistoryHoverShell.Width = ExpandedChatHistoryWidth;
        ChatHistoryPanel.Opacity = 1;
        ChatHistoryPanel.IsHitTestVisible = true;
    }

    private void OnChatHistoryHoverExited(object? sender, PointerEventArgs e)
    {
        ChatHistoryHoverShell.Width = CollapsedChatHistoryWidth;
        ChatHistoryPanel.Opacity = 0;
        ChatHistoryPanel.IsHitTestVisible = false;
    }

    private void OnAttachmentRowTapped(object? sender, TappedEventArgs e) => OwnerWindow?.OnAttachmentRowTapped(sender, e);
}
