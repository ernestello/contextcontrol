using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ContextControl.Workbench.Controls;
using ContextControl.Workbench.Services;
using ContextControl.Workbench.ViewModels;

namespace ContextControl.Workbench.Views;

public sealed partial class MainWindow
{
    private ContextMenu? _projectTreeContextMenu;
    private bool _projectTreeContextSelectionTransient;
    private TreeRowViewModel? _projectTreeSelectionAnchor;

    private void OnExternalChangesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshWindowTitle();
        RefreshProjectInfoHeader();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WorkbenchViewModel.ThemeKey)
            || e.PropertyName == nameof(WorkbenchViewModel.UiFontFamily)
            || e.PropertyName == nameof(WorkbenchViewModel.CodeFontFamily)
            || e.PropertyName == nameof(WorkbenchViewModel.SkinKey))
        {
            ApplySelectedTheme();
        }

        if (e.PropertyName == nameof(WorkbenchViewModel.ExternalQueueTitle)
            || e.PropertyName == nameof(WorkbenchViewModel.HasExternalChanges)
            || e.PropertyName == nameof(WorkbenchViewModel.CurrentProject))
        {
            if (e.PropertyName == nameof(WorkbenchViewModel.CurrentProject))
            {
                _projectTreeSelectionAnchor = null;
            }

            RefreshWindowTitle();
            RefreshProjectInfoHeader();
        }

        if (e.PropertyName == nameof(WorkbenchViewModel.ProjectTreeFocusVersion))
        {
            Dispatcher.UIThread.Post(() =>
            {
                ProjectTreeView.BringRowIntoView(ViewModel?.SelectedTreeRow);
                RefreshHoveredScrollableMenus();
            });
        }
    }

    private void OnContextControlPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ContextControlViewModel.HasAttachments)
            || e.PropertyName == nameof(ContextControlViewModel.AttachmentSummary))
        {
            Dispatcher.UIThread.Post(() =>
            {
                UpdateAttachmentListHostHeight();
                RefreshHoveredScrollableMenus();
            });
        }

        if (e.PropertyName == nameof(ContextControlViewModel.IsPromptOpen)
            && ViewModel?.ContextControl.IsPromptOpen == true)
        {
            Dispatcher.UIThread.Post(() => ContextPromptTextBox.Focus());
        }
        else if (e.PropertyName == nameof(ContextControlViewModel.IsPromptOpen))
        {
            FocusEditorShell();
        }
    }

    private void ApplySelectedTheme()
    {
        var key = ViewModel?.ThemeKey ?? "empty";
        var uiFont = ViewModel?.UiFontFamily;
        var codeFont = ViewModel?.CodeFontFamily;
        var skin = ViewModel?.SkinKey;
        WorkbenchThemeResources.Apply(this, key, uiFont, codeFont, skinKey: skin);
        _themeSettingsWindow?.ApplyTheme(key, uiFont, codeFont, skin);
    }

    private void RefreshWindowTitle()
    {
        var queueLabel = ViewModel?.HasExternalChanges == true
            ? $" - {ViewModel.ExternalQueueTitle}"
            : "";
        Title = $"Context Control{queueLabel}";
    }

    private void RefreshProjectInfoHeader()
    {
        ScheduleUiPolish();
    }

    private void ResetProjectInfoHeaderBinding()
    {
        ScheduleUiPolish();
    }

    private void ScheduleUiPolish()
    {
        Dispatcher.UIThread.Post(() =>
        {
            CenterSettingsButtons();
            ColorizeSignedDeltaTextBlocks();
        });
    }

    private void CenterSettingsButtons()
    {
        foreach (var button in this.GetVisualDescendants().OfType<Button>())
        {
            var label = GetButtonSearchText(button);
            if (!label.Contains("settings", StringComparison.OrdinalIgnoreCase)
                && !label.Contains("cog", StringComparison.OrdinalIgnoreCase)
                && !label.Contains("gear", StringComparison.OrdinalIgnoreCase)
                && !label.Contains("⚙", StringComparison.Ordinal))
            {
                continue;
            }

            button.HorizontalContentAlignment = HorizontalAlignment.Center;
            button.VerticalContentAlignment = VerticalAlignment.Center;
            if (button.Padding.Left > 0 || button.Padding.Top > 0 || button.Padding.Right > 0 || button.Padding.Bottom > 0)
            {
                button.Padding = new Thickness(Math.Min(button.Padding.Left, 6), Math.Min(button.Padding.Top, 4), Math.Min(button.Padding.Right, 6), Math.Min(button.Padding.Bottom, 4));
            }
        }
    }

    private static string GetButtonSearchText(Button button)
    {
        var classText = button.Classes.Count == 0 ? "" : string.Join(" ", button.Classes);
        return $"{button.Name} {classText} {button.Content}";
    }

    private void ColorizeSignedDeltaTextBlocks()
    {
        foreach (var textBlock in this.GetVisualDescendants().OfType<TextBlock>())
        {
            var text = (textBlock.Text ?? string.Empty).Trim();
            if (LooksLikePositiveDelta(text))
            {
                textBlock.Foreground = PositiveBrush;
            }
            else if (LooksLikeNegativeDelta(text))
            {
                textBlock.Foreground = NegativeBrush;
            }
        }
    }

    private static bool LooksLikePositiveDelta(string text)
    {
        return text.Length > 1 && text[0] == '+' && text.Skip(1).Any(char.IsDigit);
    }

    private static bool LooksLikeNegativeDelta(string text)
    {
        return text.Length > 1 && text[0] == '-' && text.Skip(1).Any(char.IsDigit);
    }

    private void OnProjectTreeViewPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (ViewModel is null || sender is not ProjectTreeRenderControl treeView)
        {
            return;
        }

        var hit = treeView.HitTestRow(e.GetPosition(treeView));
        if (hit.Row is not { HasNode: true } row)
        {
            return;
        }

        var point = e.GetCurrentPoint(treeView);
        if (point.Properties.IsRightButtonPressed)
        {
            e.Handled = true;
            var rows = SelectProjectTreeContextRows(row);
            OpenProjectTreeContextMenu(treeView, rows);
            return;
        }

        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (hit.Kind == ProjectTreeHitKind.Toggle && row.ShowDisclosure)
        {
            if (ViewModel.ToggleNodeCommand.CanExecute(row))
            {
                ViewModel.ToggleNodeCommand.Execute(row);
                Dispatcher.UIThread.Post(RefreshHoveredScrollableMenus);
            }

            e.Handled = true;
            return;
        }

        if (hit.Kind == ProjectTreeHitKind.Include && row.Node is { } node && row.CanIncludeExternal)
        {
            if (ViewModel.IncludeExternalNodeCommand.CanExecute(node))
            {
                ViewModel.IncludeExternalNodeCommand.Execute(node);
            }

            e.Handled = true;
            return;
        }

        UpdateProjectTreeSelection(row, e.KeyModifiers);
        e.Handled = true;
    }

    private void OnProjectTreeViewDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (ViewModel is null || sender is not ProjectTreeRenderControl treeView)
        {
            return;
        }

        var hit = treeView.HitTestRow(e.GetPosition(treeView));
        if (hit.Row is not { HasNode: true } row)
        {
            return;
        }

        if (hit.Kind != ProjectTreeHitKind.Row)
        {
            e.Handled = true;
            return;
        }

        ViewModel.SelectTreeRow(row, false);
        if (row.Node is { IsFile: true } node)
        {
            ViewModel.ToggleHistoryForNode(node);
        }

        e.Handled = true;
    }

    private void OnProjectTreeRowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (ViewModel is null
            || sender is not Control rowControl
            || rowControl.DataContext is not TreeRowViewModel row
            || IsTreeRowActionSource(e.Source))
        {
            return;
        }

        var point = e.GetCurrentPoint(ProjectTreeList);

        if (point.Properties.IsRightButtonPressed)
        {
            e.Handled = true;
            var rows = SelectProjectTreeContextRows(row);
            OpenProjectTreeContextMenu(rowControl, rows);
            return;
        }

        if (point.Properties.IsLeftButtonPressed)
        {
            UpdateProjectTreeSelection(row, e.KeyModifiers);
            e.Handled = true;
        }
    }

    private void OnProjectTreeRowDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (ViewModel is null
            || sender is not Control { DataContext: TreeRowViewModel row }
            || IsTreeRowActionSource(e.Source))
        {
            return;
        }

        ViewModel.SelectTreeRow(row, false);
        if (row.Node is { IsFile: true } node)
        {
            ViewModel.ToggleHistoryForNode(node);
        }

        e.Handled = true;
    }

    private static bool IsTreeRowActionSource(object? source)
    {
        for (var current = source as Visual; current is not null; current = current.GetVisualParent())
        {
            if (current is Button button
                && (button.Classes.Contains("tree-toggle") || button.Classes.Contains("include-external")))
            {
                return true;
            }
        }

        return false;
    }

    private IReadOnlyList<TreeRowViewModel> SelectProjectTreeContextRows(TreeRowViewModel row)
    {
        var selectedRows = ViewModel?.SelectedTreeRows
            .Where(item => item.Node is not null)
            .ToArray() ?? [];

        if (selectedRows.Contains(row))
        {
            _projectTreeContextSelectionTransient = false;
            return selectedRows;
        }

        SelectProjectTreeRows([row], row);
        _projectTreeContextSelectionTransient = true;
        return [row];
    }

    private void UpdateProjectTreeSelection(TreeRowViewModel row, KeyModifiers modifiers)
    {
        _projectTreeContextSelectionTransient = false;
        if ((modifiers & KeyModifiers.Shift) != 0 && _projectTreeSelectionAnchor is not null)
        {
            SelectProjectTreeRows(GetProjectTreeSelectionRange(_projectTreeSelectionAnchor, row), _projectTreeSelectionAnchor);
            return;
        }

        if ((modifiers & KeyModifiers.Control) != 0)
        {
            ToggleProjectTreeRowSelection(row);
            return;
        }

        _projectTreeSelectionAnchor = row;
        ViewModel?.SetSelectedTreeRows([]);
        ViewModel?.SelectTreeRow(row, false);
    }

    private IReadOnlyList<TreeRowViewModel> GetProjectTreeSelectionRange(TreeRowViewModel anchor, TreeRowViewModel row)
    {
        if (ViewModel is null)
        {
            return [row];
        }

        var start = ViewModel.VisibleTreeRows.IndexOf(anchor);
        var end = ViewModel.VisibleTreeRows.IndexOf(row);
        if (start < 0 || end < 0)
        {
            return [row];
        }

        if (start > end)
        {
            (start, end) = (end, start);
        }

        return ViewModel.VisibleTreeRows
            .Skip(start)
            .Take(end - start + 1)
            .Where(item => item.Node is not null)
            .ToArray();
    }

    private void ToggleProjectTreeRowSelection(TreeRowViewModel row)
    {
        if (ViewModel is null)
        {
            return;
        }

        var rows = ViewModel.SelectedTreeRows
            .Where(item => item.Node is not null)
            .ToList();

        if (!rows.Remove(row))
        {
            rows.Add(row);
        }

        SelectProjectTreeRows(rows, row);
    }

    private void SelectProjectTreeRows(IEnumerable<TreeRowViewModel> rows, TreeRowViewModel? anchor)
    {
        var selectedRows = rows
            .Where(row => row.Node is not null)
            .Distinct()
            .ToArray();

        ViewModel?.SetSelectedTreeRows(selectedRows);
        _projectTreeSelectionAnchor = selectedRows.Length == 0
            ? null
            : anchor is not null && selectedRows.Contains(anchor) ? anchor : selectedRows[^1];
    }

    private void OpenProjectTreeContextMenu(Control target, IReadOnlyList<TreeRowViewModel> rows)
    {
        if (ViewModel is null || rows.Count == 0)
        {
            return;
        }

        CloseProjectTreeContextMenu();

        var capturedRows = rows.ToArray();
        var menu = new ContextMenu();
        menu.Classes.Add("project-tree-context-menu");
        menu.Closing += OnProjectTreeContextMenuClosing;
        var copyItem = CreateProjectTreeContextItem(BuildProjectTreeCopyHeader(capturedRows));
        copyItem.Click += (_, _) => RunProjectTreeContextActionAsync(() => ViewModel.CopyTreeContextAsync(capturedRows));
        menu.Items.Add(copyItem);
        menu.Items.Add(CreateProjectTreeContextSeparator());

        if (capturedRows.Length > 1)
        {
            var skipSelectionItem = CreateProjectTreeContextItem("Skip selection");
            skipSelectionItem.Click += (_, _) => RunProjectTreeContextActionAsync(() => ViewModel.SkipTreeContextAsync(capturedRows));
            menu.Items.Add(skipSelectionItem);

            var showSelectionItem = CreateProjectTreeContextItem("Show selection");
            showSelectionItem.Click += (_, _) => RunProjectTreeContextActionAsync(() => ViewModel.ShowTreeContextAsync(capturedRows));
            menu.Items.Add(showSelectionItem);
        }
        else if (capturedRows[0].Node is { } node)
        {
            var skipShowItem = CreateProjectTreeContextItem(BuildProjectTreeSkipShowHeader(node));
            skipShowItem.IsEnabled = !string.IsNullOrWhiteSpace(node.Path);
            skipShowItem.Click += (_, _) => RunProjectTreeContextActionAsync(async () =>
            {
                if (node.IsExternal)
                {
                    await ViewModel.ShowTreeContextAsync(capturedRows);
                    return;
                }

                await ViewModel.SkipTreeContextAsync(capturedRows);
            });
            menu.Items.Add(skipShowItem);

            if (node.IsFile && ViewModel.CanToggleTreeFileExtension(node))
            {
                var extensionItem = CreateProjectTreeContextItem(ViewModel.GetTreeFileExtensionRuleLabel(node));
                extensionItem.Click += (_, _) => RunProjectTreeContextActionAsync(() => ViewModel.ToggleTreeFileExtensionAsync(node));
                menu.Items.Add(extensionItem);

                var locItem = CreateProjectTreeContextItem(ViewModel.GetTreeFileLocRuleLabel(node));
                locItem.Click += (_, _) => RunProjectTreeContextActionAsync(() => ViewModel.ToggleTreeFileLocExtensionAsync(node));
                menu.Items.Add(locItem);
            }
        }

        _projectTreeContextMenu = menu;
        menu.Open(target);
    }

    private void RunProjectTreeContextActionAsync(Func<Task> action)
    {
        CloseProjectTreeContextMenu();
        _ = RunProjectTreeContextActionCoreAsync(action);
    }

    private async Task RunProjectTreeContextActionCoreAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            ViewModel?.ReportProjectTreeActionError(ex.Message);
        }
    }

    private void CloseProjectTreeContextMenu()
    {
        var menu = _projectTreeContextMenu;
        if (menu is null)
        {
            return;
        }

        menu.Closing -= OnProjectTreeContextMenuClosing;
        _projectTreeContextMenu = null;
        menu.Close();
        ClearTransientProjectTreeContextSelection();
    }

    private void OnProjectTreeContextMenuClosing(object? sender, CancelEventArgs e)
    {
        if (!ReferenceEquals(sender, _projectTreeContextMenu))
        {
            return;
        }

        _projectTreeContextMenu = null;
        ClearTransientProjectTreeContextSelection();
    }

    private void ClearTransientProjectTreeContextSelection()
    {
        if (!_projectTreeContextSelectionTransient)
        {
            return;
        }

        _projectTreeContextSelectionTransient = false;
        ViewModel?.SetSelectedTreeRows([]);
    }

    private static MenuItem CreateProjectTreeContextItem(string header)
    {
        var item = new MenuItem { Header = header };
        item.Classes.Add("project-tree-context-item");
        return item;
    }

    private static Separator CreateProjectTreeContextSeparator()
    {
        var separator = new Separator();
        separator.Classes.Add("project-tree-context-separator");
        return separator;
    }

    private static string BuildProjectTreeCopyHeader(IReadOnlyList<TreeRowViewModel> rows)
    {
        if (rows.Count != 1 || rows[0].Node is not { } node)
        {
            return "Copy all selection context";
        }

        return node.IsFolder ? "Copy all folder context" : "Copy all file context";
    }

    private static string BuildProjectTreeSkipShowHeader(ProjectNodeViewModel node)
    {
        var action = node.IsExternal ? "Show" : "Skip";
        var target = node.IsFolder ? "folder" : "file";
        return $"{action} {target}";
    }

    private void OnHistoryVersionPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (ViewModel is null || sender is not Control { DataContext: VersionEntryViewModel version })
        {
            return;
        }

        ViewModel.OpenVersionFromHistory(version);
        ScheduleUiPolish();
        e.Handled = true;
    }

    private void OnProjectGraphFitLayoutClick(object? sender, RoutedEventArgs e)
    {
        ProjectGraphView.ResetLayout();
    }

    private async void OnProjectGraphCopyTreeClick(object? sender, RoutedEventArgs e)
    {
        var text = ViewModel?.ProjectGraphTreeText;
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is not null)
        {
            await clipboard.SetTextAsync(text);
        }
    }
}
