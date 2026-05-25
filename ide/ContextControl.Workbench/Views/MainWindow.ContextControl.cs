using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ContextControl.Workbench.Controls;
using ContextControl.Workbench.ViewModels;

namespace ContextControl.Workbench.Views;

public sealed partial class MainWindow
{
    private const double AttachmentListPreferredMinimumHeight = 96;
    private const double AttachmentSectionHeaderHeight = 24;
    private const string ScrollbarExpandedClass = "scrollbar-expanded";
    private const double ProjectTreeWheelStep = 54.0;
    private const double ProjectTreeWheelMinimumDelta = 0.01;
    private const double ProjectTreeScrollbarWheelZone = 18.0;
    private const double ProjectTreeSmoothSnapDistance = 0.35;
    private readonly HashSet<Control> _hoveredScrollableMenus = [];
    private ScrollViewer? _projectTreeScrollViewer;
    private double _projectTreeSmoothTargetY = double.NaN;
    private bool _projectTreeSmoothFrameQueued;

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        _closeHistoryTimer.Stop();

        if (HandlePromptShortcut(e))
        {
            return;
        }

        ViewModel?.ScanExternalChangesNow();
        RefreshProjectInfoHeader();
        ScheduleUiPolish();
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        _closeHistoryTimer.Stop();
        if (HandleProjectGraphSearchShortcut(e))
        {
            return;
        }

        HandlePromptShortcut(e);
    }

    private void OnPreviewPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _closeHistoryTimer.Stop();
        if (ViewModel?.IsProjectGraphSearchOpen != true)
        {
            return;
        }

        if (IsPointerInsideVisual(e.Source, ProjectGraphSearchPanel))
        {
            return;
        }

        ViewModel.CloseProjectGraphSearch();
    }

    private static bool IsPointerInsideVisual(object? source, Visual target)
    {
        for (var visual = source as Visual; visual is not null; visual = visual.GetVisualParent())
        {
            if (ReferenceEquals(visual, target))
            {
                return true;
            }
        }

        return false;
    }

    private bool HandleProjectGraphSearchShortcut(KeyEventArgs e)
    {
        if (e.Key == Key.F
            && (e.KeyModifiers & KeyModifiers.Control) != 0
            && ViewModel?.IsProjectGraphMode == true)
        {
            ViewModel.OpenProjectGraphSearch();
            e.Handled = true;
            Dispatcher.UIThread.Post(() =>
            {
                ProjectGraphSearchBox.Focus();
                ProjectGraphSearchBox.SelectAll();
            });
            return true;
        }

        if (e.Key == Key.Escape && ViewModel?.IsProjectGraphSearchOpen == true)
        {
            ViewModel.CloseProjectGraphSearch();
            e.Handled = true;
            ProjectGraphView.Focus();
            return true;
        }

        return e.Handled;
    }

    private void OnProjectGraphSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            ViewModel?.CloseProjectGraphSearch();
            e.Handled = true;
            ProjectGraphView.Focus();
            return;
        }

        if (e.Key == Key.Enter)
        {
            ViewModel?.AcceptProjectGraphSearch();
            e.Handled = true;
        }
    }

    private bool HandlePromptShortcut(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && ViewModel?.ContextControl.IsPromptOpen == true)
        {
            ViewModel.ContextControl.ClosePrompt();
            e.Handled = true;
            FocusEditorShell();
            return true;
        }

        if (e.Key == Key.Space
            && e.KeyModifiers == KeyModifiers.None
            && ViewModel?.ContextControl is { IsPromptOpen: false } contextControl
            && !IsUserTypingSource(e.Source))
        {
            contextControl.OpenPrompt();
            e.Handled = true;
            Dispatcher.UIThread.Post(() => ContextPromptTextBox.Focus());
            return true;
        }

        return e.Handled;
    }

    private void OnWorkspacePointerMoved(object? sender, PointerEventArgs e)
    {
        _closeHistoryTimer.Stop();
    }

    private void ConfigurePromptFileDropTargets()
    {
        ConfigurePromptFileDropTarget(WorkspaceRoot);
    }

    private void ConfigurePromptFileDropTarget(Interactive target)
    {
        DragDrop.SetAllowDrop(target, true);
        DragDrop.AddDragEnterHandler(target, OnPromptFileDragEnter);
        DragDrop.AddDragLeaveHandler(target, OnPromptFileDragLeave);
        DragDrop.AddDragOverHandler(target, OnPromptFileDragOver);
        DragDrop.AddDropHandler(target, OnPromptFileDrop);
    }

    private void OnPromptFileDragEnter(object? sender, DragEventArgs e)
    {
        if (!HasDroppedFileFormat(e.DataTransfer))
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }

        _fileDragHoverCount++;
        SetPromptFileDropOverlayVisible(true);
        e.DragEffects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private void OnPromptFileDragLeave(object? sender, DragEventArgs e)
    {
        if (_fileDragHoverCount > 0)
        {
            _fileDragHoverCount--;
        }

        if (_fileDragHoverCount == 0)
        {
            SetPromptFileDropOverlayVisible(false);
        }

        e.Handled = true;
    }

    private void OnPromptFileDragOver(object? sender, DragEventArgs e)
    {
        var canDrop = HasDroppedFileFormat(e.DataTransfer);
        e.DragEffects = canDrop
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        SetPromptFileDropOverlayVisible(canDrop);
        e.Handled = true;
    }

    private void OnPromptFileDrop(object? sender, DragEventArgs e)
    {
        _fileDragHoverCount = 0;
        SetPromptFileDropOverlayVisible(false);

        var files = GetDroppedLocalFilePaths(e.DataTransfer);
        if (ViewModel?.ContextControl is not { } contextControl || files.Count == 0)
        {
            e.DragEffects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        contextControl.AttachFiles(files);
        e.DragEffects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private static bool HasDroppedFileFormat(IDataTransfer dataTransfer)
    {
        return dataTransfer.Formats.Contains(DataFormat.File);
    }

    private static IReadOnlyList<string> GetDroppedLocalFilePaths(IDataTransfer dataTransfer)
    {
        var storageItems = dataTransfer.TryGetFiles();
        if (storageItems is null)
        {
            return [];
        }

        return storageItems
            .Select(item => NormalizeFullPath(item.TryGetLocalPath()))
            .Where(path => path is not null && File.Exists(path))
            .Select(path => path!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string? NormalizeFullPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(path);
        }
        catch
        {
            return null;
        }
    }

    private void SetPromptFileDropOverlayVisible(bool isVisible)
    {
        if (PromptFileDropOverlay.IsVisible == isVisible)
        {
            return;
        }

        PromptFileDropOverlay.IsVisible = isVisible;
    }

    private void ConfigureContextControlBridge()
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        viewModel.ContextControl.SetClipboardWriter(async text =>
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is not null)
            {
                await clipboard.SetTextAsync(text);
            }
        });
    }

    private bool IsUserTypingSource(object? source)
    {
        if (source is not Control control)
        {
            return false;
        }

        return control.GetVisualAncestors()
            .OfType<Control>()
            .Prepend(control)
            .Any(IsActiveTextInput);
    }

    private bool IsActiveTextInput(Control control)
    {
        if (ReferenceEquals(control, ContextPromptTextBox)
            && ViewModel?.ContextControl.IsPromptOpen != true)
        {
            return false;
        }

        return control is TextBox;
    }

    private void FocusEditorShell()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (ContextPromptTextBox.IsFocused)
            {
                WorkspaceRoot.Focus();
            }
        });
    }

    private void OnPromptTextBoxGotFocus(object? sender, GotFocusEventArgs e)
    {
        ViewModel?.ContextControl.SetPromptTypingActive(false);
    }

    private void OnPromptTextBoxLostFocus(object? sender, RoutedEventArgs e)
    {
        _promptTypingTimer.Stop();
        ViewModel?.ContextControl.SetPromptTypingActive(false);
    }

    private void OnPromptTextBoxKeyDown(object? sender, KeyEventArgs e)
    {
        MarkPromptTypingActive();
    }

    private void OnPromptTextBoxTextChanged(object? sender, TextChangedEventArgs e)
    {
        MarkPromptTypingActive();
    }

    private void MarkPromptTypingActive()
    {
        if (ViewModel?.ContextControl.IsPromptOpen != true || !ContextPromptTextBox.IsFocused)
        {
            return;
        }

        ViewModel.ContextControl.SetPromptTypingActive(true);
        _promptTypingTimer.Stop();
        _promptTypingTimer.Start();
    }

    private void OnAttachmentRegionSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        UpdateAttachmentListHostHeight();
        Dispatcher.UIThread.Post(RefreshHoveredScrollableMenus);
    }

    private void UpdateAttachmentListHostHeight()
    {
        var availableHeight = AttachmentRegion.Bounds.Height - AttachmentSectionHeaderHeight;
        if (double.IsNaN(availableHeight))
        {
            return;
        }

        var clampedHeight = Math.Max(0, availableHeight);
        AttachmentListHost.MaxHeight = clampedHeight;
        AttachmentListHost.MinHeight = Math.Min(AttachmentListPreferredMinimumHeight, clampedHeight);
    }

    private void OnScrollableMenuPointerEntered(object? sender, PointerEventArgs e)
    {
        if (sender is not Control control)
        {
            return;
        }

        _hoveredScrollableMenus.Add(control);
        UpdateScrollableMenuScrollbar(control);
    }

    private void OnScrollableMenuPointerExited(object? sender, PointerEventArgs e)
    {
        if (sender is not Control control)
        {
            return;
        }

        if (ContainsPointer(control, e))
        {
            UpdateScrollableMenuScrollbarIfHovered(control);
            return;
        }

        _hoveredScrollableMenus.Remove(control);
        SetScrollableMenuScrollbarExpanded(control, false);
    }

    private void OnScrollableMenuSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (sender is not Control control)
        {
            return;
        }

        // SizeChanged can fire while extent/viewport are in-flight during layout.
        // Re-evaluate later; hovered menus latch their visible scrollbar until exit.
        Dispatcher.UIThread.Post(() => UpdateScrollableMenuScrollbarIfHovered(control));
    }

    private void OnProjectTreeToggleClick(object? sender, RoutedEventArgs e)
    {
        Dispatcher.UIThread.Post(RefreshHoveredScrollableMenus);
    }

    private void OnProjectTreeRequestBringIntoView(object? sender, RequestBringIntoViewEventArgs e)
    {
        e.Handled = true;
    }

    private void OnProjectTreePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (IsProjectTreeScrollChromeSource(e.Source))
        {
            ResetProjectTreeSmoothScroll();
        }
    }

    private void OnProjectTreePointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (Math.Abs(e.Delta.Y) < ProjectTreeWheelMinimumDelta
            || IsProjectTreeScrollChromeSource(e.Source)
            || IsProjectTreeScrollbarWheelZone(e))
        {
            return;
        }

        if (GetProjectTreeScrollViewer() is not { } scrollViewer
            || !TryGetProjectTreeScrollRange(out var maxY))
        {
            return;
        }

        var startY = double.IsNaN(_projectTreeSmoothTargetY)
            ? scrollViewer.Offset.Y
            : _projectTreeSmoothTargetY;
        _projectTreeSmoothTargetY = Math.Clamp(startY - e.Delta.Y * ProjectTreeWheelStep, 0, maxY);
        e.Handled = true;

        QueueProjectTreeSmoothScrollFrame();
    }

    private void QueueProjectTreeSmoothScrollFrame()
    {
        if (_projectTreeSmoothFrameQueued)
        {
            return;
        }

        _projectTreeSmoothFrameQueued = true;
        (TopLevel.GetTopLevel(ProjectTreeList) ?? this).RequestAnimationFrame(AnimateProjectTreeSmoothScroll);
    }

    private void AnimateProjectTreeSmoothScroll(TimeSpan frameTime)
    {
        _projectTreeSmoothFrameQueued = false;

        if (double.IsNaN(_projectTreeSmoothTargetY)
            || !TryGetProjectTreeScrollRange(out var maxY)
            || _projectTreeScrollViewer is not { } scrollViewer)
        {
            ResetProjectTreeSmoothScroll();
            return;
        }

        _projectTreeSmoothTargetY = Math.Clamp(_projectTreeSmoothTargetY, 0, maxY);
        var offset = scrollViewer.Offset;
        var distance = _projectTreeSmoothTargetY - offset.Y;
        if (Math.Abs(distance) <= ProjectTreeSmoothSnapDistance)
        {
            scrollViewer.Offset = new Vector(offset.X, _projectTreeSmoothTargetY);
            ResetProjectTreeSmoothScroll();
            return;
        }

        if (Math.Abs(distance) >= ProjectTreeSmoothSnapDistance)
        {
            scrollViewer.Offset = new Vector(offset.X, _projectTreeSmoothTargetY);
        }

        ResetProjectTreeSmoothScroll();
    }

    private void ResetProjectTreeSmoothScroll()
    {
        _projectTreeSmoothTargetY = double.NaN;
    }

    private ScrollViewer? GetProjectTreeScrollViewer()
    {
        if (_projectTreeScrollViewer is not null)
        {
            return _projectTreeScrollViewer;
        }

        if (ProjectTreeList is ScrollViewer scrollViewer)
        {
            return _projectTreeScrollViewer = scrollViewer;
        }

        return _projectTreeScrollViewer = ProjectTreeList
            .GetVisualDescendants()
            .OfType<ScrollViewer>()
            .FirstOrDefault();
    }

    private bool TryGetProjectTreeScrollRange(out double maxY)
    {
        var scrollViewer = GetProjectTreeScrollViewer();
        if (scrollViewer is null)
        {
            maxY = 0;
            return false;
        }

        maxY = Math.Max(0, scrollViewer.Extent.Height - scrollViewer.Viewport.Height);
        return !double.IsNaN(maxY) && !double.IsInfinity(maxY) && maxY > 0;
    }

    private static bool IsProjectTreeScrollChromeSource(object? source)
    {
        var control = source as Control;
        while (control is not null)
        {
            if (control is ScrollBar or Thumb or Track or RepeatButton)
            {
                return true;
            }

            control = control.GetVisualParent() as Control;
        }

        return false;
    }

    private bool IsProjectTreeScrollbarWheelZone(PointerEventArgs e)
    {
        var bounds = ProjectTreeList.Bounds;
        if (bounds.Width <= ProjectTreeScrollbarWheelZone || bounds.Height <= 0)
        {
            return false;
        }

        var point = e.GetPosition(ProjectTreeList);
        return point.X >= bounds.Width - ProjectTreeScrollbarWheelZone
            && point.X <= bounds.Width
            && point.Y >= 0
            && point.Y <= bounds.Height;
    }

    private void RefreshHoveredScrollableMenus()
    {
        HoverScrollbarBehavior.Refresh(ProjectTreeList);
        HoverScrollbarBehavior.Refresh(AttachmentList);
        UpdateScrollableMenuScrollbarIfHovered(ProjectTreeList);
        UpdateScrollableMenuScrollbarIfHovered(AttachmentList);
    }

    private void UpdateScrollableMenuScrollbarIfHovered(object? sender)
    {
        if (sender is not Control control || !_hoveredScrollableMenus.Contains(control))
        {
            return;
        }

        var isExpanded = control.Classes.Contains(ScrollbarExpandedClass);
        SetScrollableMenuScrollbarExpanded(control, isExpanded || HasScrollableContent(control));
    }

    private static void UpdateScrollableMenuScrollbar(object? sender)
    {
        SetScrollableMenuScrollbarExpanded(sender, HasScrollableContent(sender));
    }

    private static void SetScrollableMenuScrollbarExpanded(object? sender, bool isExpanded)
    {
        if (sender is not Control control)
        {
            return;
        }

        var shouldAutoHide = !isExpanded;
        var hasExpandedClass = control.Classes.Contains(ScrollbarExpandedClass);
        if (ScrollViewer.GetAllowAutoHide(control) == shouldAutoHide && hasExpandedClass == isExpanded)
        {
            return;
        }

        ScrollViewer.SetAllowAutoHide(control, shouldAutoHide);

        if (isExpanded)
        {
            if (!hasExpandedClass)
            {
                control.Classes.Add(ScrollbarExpandedClass);
            }
        }
        else
        {
            control.Classes.Remove(ScrollbarExpandedClass);
        }
    }

    private static bool HasScrollableContent(object? sender)
    {
        if (sender is not Control control)
        {
            return false;
        }

        var scrollViewer = control
            .GetVisualDescendants()
            .OfType<ScrollViewer>()
            .FirstOrDefault();

        if (scrollViewer is null)
        {
            return control.Classes.Contains(ScrollbarExpandedClass);
        }

        var extent = scrollViewer.Extent.Height;
        var viewport = scrollViewer.Viewport.Height;
        var extentReady = !double.IsNaN(extent) && !double.IsInfinity(extent) && extent > 0;
        var viewportReady = !double.IsNaN(viewport) && !double.IsInfinity(viewport) && viewport > 0;
        if (!extentReady || !viewportReady)
        {
            return control.Classes.Contains(ScrollbarExpandedClass);
        }

        return extent > viewport + 0.1;
    }

    private void OnAttachmentRowTapped(object? sender, TappedEventArgs e)
    {
        if (ViewModel is null || sender is not Control { DataContext: ContextControlAttachmentViewModel attachment })
        {
            return;
        }

        ViewModel.OpenAttachment(attachment.Path);
        ScheduleUiPolish();
        e.Handled = true;
    }

    private void OnAttachmentRemoveTapped(object? sender, TappedEventArgs e)
    {
        if (ViewModel is null || sender is not Control { DataContext: ContextControlAttachmentViewModel attachment })
        {
            return;
        }

        ViewModel.ContextControl.RemoveAttachment(attachment);
        ScheduleUiPolish();
        e.Handled = true;
    }
}
