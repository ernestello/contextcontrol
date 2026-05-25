using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using ContextControl.Workbench.ViewModels;

namespace ContextControl.Workbench.Controls;

public enum ProjectTreeHitKind
{
    None,
    Row,
    Toggle,
    Include
}

public readonly record struct ProjectTreeHitTestResult(TreeRowViewModel? Row, ProjectTreeHitKind Kind, int Index);

public sealed class ProjectTreeRenderControl : Control
{
    public static readonly StyledProperty<IReadOnlyList<TreeRowViewModel>?> ItemsProperty =
        AvaloniaProperty.Register<ProjectTreeRenderControl, IReadOnlyList<TreeRowViewModel>?>(nameof(Items));

    public static readonly StyledProperty<bool> ShowFileDetailsProperty =
        AvaloniaProperty.Register<ProjectTreeRenderControl, bool>(nameof(ShowFileDetails));

    public static readonly StyledProperty<string> ThemeKeyProperty =
        AvaloniaProperty.Register<ProjectTreeRenderControl, string>(nameof(ThemeKey), "empty");

    public static readonly StyledProperty<string> UiFontFamilyProperty =
        AvaloniaProperty.Register<ProjectTreeRenderControl, string>(nameof(UiFontFamily), "fonts:Inter, Segoe UI");

    public static readonly StyledProperty<string> CodeFontFamilyProperty =
        AvaloniaProperty.Register<ProjectTreeRenderControl, string>(nameof(CodeFontFamily), "avares://ContextControl.Workbench/Assets/Fonts#Cascadia Code, Consolas");

    private const double ContentLeft = 4.0;
    private const double ContentTop = 4.0;
    private const double ContentBottom = 9.0;
    private const double ViewportOverscan = 34.0;
    private const double RailStep = 9.0;
    private const double RailInset = 6.0;
    private const double ToggleSize = 11.0;
    private const double ToggleCornerRadius = 3.0;
    private const double ArrowSize = 8.0;
    private const double IncludeWidth = 38.0;
    private const double IncludeHeight = 15.0;
    private const double IncludeCornerRadius = 4.0;
    private const double NameLeftMargin = 3.0;
    private const double RightInset = 3.0;
    private const double DetailSpacing = 4.0;
    private const int MaxTextCacheEntries = 4096;

    private static readonly FontFamily DefaultCodeFontFamily = new("Consolas");
    private static readonly FontFamily DefaultUiFontFamily = new("Segoe UI");
    private static readonly Cursor HandCursor = new(StandardCursorType.Hand);
    private static readonly Cursor ArrowCursor = new(StandardCursorType.Arrow);

    private static readonly IBrush TransparentBrush = Brushes.Transparent;
    private static readonly IBrush EmptyArrowBrush = new SolidColorBrush(Color.FromRgb(83, 97, 102));
    private static readonly IBrush DarkArrowBrush = new SolidColorBrush(Color.FromRgb(183, 199, 203));
    private static readonly IBrush MatrixArrowBrush = new SolidColorBrush(Color.FromRgb(150, 255, 195));
    private static readonly Pen EmptyRailPen = new(new SolidColorBrush(Color.FromRgb(178, 195, 197)), 1);
    private static readonly Pen EmptyBranchPen = new(new SolidColorBrush(Color.FromRgb(139, 188, 192)), 1);
    private static readonly Pen DarkRailPen = new(new SolidColorBrush(Color.FromRgb(48, 67, 72)), 1);
    private static readonly Pen DarkBranchPen = new(new SolidColorBrush(Color.FromRgb(79, 139, 143)), 1);
    private static readonly Pen MatrixRailPen = new(new SolidColorBrush(Color.FromRgb(42, 118, 91)), 1);
    private static readonly Pen MatrixBranchPen = new(new SolidColorBrush(Color.FromRgb(101, 240, 178)), 1);

    private static readonly IBrush PanelFallbackBrush = Brush.Parse("#F4F2EC");
    private static readonly IBrush CommandBorderFallbackBrush = Brush.Parse("#C9D0D2");
    private static readonly IBrush HistoryActiveFallbackBrush = Brush.Parse("#E7EEF0");
    private static readonly IBrush DirectoryHighlightFallbackBrush = Brush.Parse("#EBF0EA");
    private static readonly IBrush CurrentRowFallbackBrush = Brush.Parse("#DCEBEE");
    private static readonly IBrush CurrentRowBorderFallbackBrush = Brush.Parse("#B7CBD1");
    private static readonly IBrush SelectedRowFallbackBrush = Brush.Parse("#CFE8EC");
    private static readonly IBrush FolderTextFallbackBrush = Brush.Parse("#31464B");
    private static readonly IBrush FileTextFallbackBrush = Brush.Parse("#51656B");
    private static readonly IBrush ExternalTextFallbackBrush = Brush.Parse("#8A6262");
    private static readonly IBrush TextMutedFallbackBrush = Brush.Parse("#6F7F85");
    private static readonly IBrush NodeTextFallbackBrush = Brush.Parse("#475B61");
    private static readonly IBrush SkipTextFallbackBrush = Brush.Parse("#9B6868");
    private static readonly IBrush MetricFileFallbackBrush = Brush.Parse("#4E7D88");
    private static readonly IBrush MetricLocFallbackBrush = Brush.Parse("#5E766D");
    private static readonly IBrush IncludeBackgroundFallbackBrush = Brush.Parse("#E9F4EE");
    private static readonly IBrush IncludeBorderFallbackBrush = Brush.Parse("#A5CAB8");
    private static readonly IBrush IncludeTextFallbackBrush = Brush.Parse("#2F6E53");
    private static readonly IBrush CommandPrimaryBackgroundFallbackBrush = Brush.Parse("#DDF1E7");
    private static readonly IBrush AccentBorderFallbackBrush = Brush.Parse("#79BDA0");
    private static readonly IBrush AccentFallbackBrush = Brush.Parse("#227A58");

    private readonly List<INotifyPropertyChanged> _subscribedRows = [];
    private readonly Dictionary<TextCacheKey, FormattedText> _textCache = new();
    private readonly Dictionary<double, StreamGeometry> _arrowGeometryCache = new();
    private double[] _rowTops = [ContentTop];
    private double _totalHeight = ContentTop + ContentBottom;
    private INotifyCollectionChanged? _itemsCollectionChanged;
    private ScrollViewer? _scrollViewer;
    private IDisposable? _offsetSubscription;
    private IDisposable? _viewportSubscription;
    private int _hoveredIndex = -1;
    private ProjectTreeHitKind _hoveredKind = ProjectTreeHitKind.None;

    public IReadOnlyList<TreeRowViewModel>? Items
    {
        get => GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    public bool ShowFileDetails
    {
        get => GetValue(ShowFileDetailsProperty);
        set => SetValue(ShowFileDetailsProperty, value);
    }

    public string ThemeKey
    {
        get => GetValue(ThemeKeyProperty);
        set => SetValue(ThemeKeyProperty, value);
    }

    public string UiFontFamily
    {
        get => GetValue(UiFontFamilyProperty);
        set => SetValue(UiFontFamilyProperty, value);
    }

    public string CodeFontFamily
    {
        get => GetValue(CodeFontFamilyProperty);
        set => SetValue(CodeFontFamilyProperty, value);
    }

    public ProjectTreeRenderControl()
    {
        Focusable = false;
    }

    public void BringRowIntoView(TreeRowViewModel? row)
    {
        var items = Items;
        if (row is null || items is null || items.Count == 0)
        {
            return;
        }

        var index = -1;
        for (var itemIndex = 0; itemIndex < items.Count; itemIndex++)
        {
            if (ReferenceEquals(items[itemIndex], row))
            {
                index = itemIndex;
                break;
            }
        }

        if (index < 0 || _rowTops.Length <= index + 1)
        {
            return;
        }

        var scrollViewer = _scrollViewer
            ?? this.GetVisualAncestors().OfType<ScrollViewer>().FirstOrDefault();
        if (scrollViewer is null)
        {
            return;
        }

        var rowTop = _rowTops[index];
        var rowBottom = _rowTops[index + 1];
        var rowCenter = (rowTop + rowBottom) * 0.5;
        var maxY = Math.Max(0, scrollViewer.Extent.Height - scrollViewer.Viewport.Height);
        var targetY = Math.Clamp(rowCenter - scrollViewer.Viewport.Height * 0.5, 0, maxY);
        scrollViewer.Offset = new Vector(scrollViewer.Offset.X, targetY);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        AttachToScrollViewer(this.GetVisualAncestors().OfType<ScrollViewer>().FirstOrDefault());
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        AttachToScrollViewer(null);
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ItemsProperty)
        {
            AttachItems();
        }
        else if (change.Property == ShowFileDetailsProperty)
        {
            InvalidateVisual();
        }
        else if (change.Property == ThemeKeyProperty)
        {
            _textCache.Clear();
            _arrowGeometryCache.Clear();
            InvalidateVisual();
        }
        else if (change.Property == UiFontFamilyProperty
            || change.Property == CodeFontFamilyProperty)
        {
            _textCache.Clear();
            InvalidateVisual();
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsFinite(availableSize.Width) ? availableSize.Width : 1.0;
        return new Size(Math.Max(1.0, width), _totalHeight);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var hit = HitTestRow(e.GetPosition(this));
        SetHoveredHit(hit.Index, hit.Kind);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        SetHoveredHit(-1, ProjectTreeHitKind.None);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var viewportTop = Math.Max(0.0, _scrollViewer?.Offset.Y ?? 0.0);
        var viewportHeight = _scrollViewer?.Viewport.Height ?? Math.Min(Bounds.Height, 800.0);
        if (!double.IsFinite(viewportHeight) || viewportHeight <= 0.0)
        {
            viewportHeight = Math.Min(Bounds.Height, 800.0);
        }

        context.DrawRectangle(TransparentBrush, null, new Rect(0, viewportTop, Bounds.Width, viewportHeight));

        var items = Items;
        if (items is null || items.Count == 0 || _rowTops.Length <= 1)
        {
            return;
        }

        var startY = Math.Max(0.0, viewportTop - ViewportOverscan);
        var endY = Math.Min(_totalHeight, viewportTop + viewportHeight + ViewportOverscan);
        var startIndex = FindRowIndexAtOrAfter(startY);
        if (startIndex < 0)
        {
            return;
        }

        var uiFontFamily = ResolveFontFamily(UiFontFamily, Resource("UiFontFamily", DefaultUiFontFamily));
        var codeFontFamily = ResolveFontFamily(CodeFontFamily, Resource("CodeFontFamily", DefaultCodeFontFamily));
        var width = Bounds.Width;
        for (var index = startIndex; index < items.Count; index++)
        {
            var rowTop = _rowTops[index];
            if (rowTop > endY)
            {
                break;
            }

            var rowBottom = _rowTops[index + 1];
            if (rowBottom < startY)
            {
                continue;
            }

            DrawRow(context, items[index], index, rowTop, rowBottom - rowTop, width, uiFontFamily, codeFontFamily);
        }
    }

    public ProjectTreeHitTestResult HitTestRow(Point point)
    {
        var items = Items;
        if (items is null
            || items.Count == 0
            || point.X < 0
            || point.Y < 0
            || point.X > Bounds.Width
            || point.Y > _totalHeight)
        {
            return default;
        }

        var index = FindRowIndexAtOrAfter(point.Y);
        if (index < 0 || index >= items.Count)
        {
            return default;
        }

        if (point.Y < _rowTops[index] || point.Y >= _rowTops[index + 1])
        {
            return default;
        }

        var row = items[index];
        if (!row.HasNode)
        {
            return new ProjectTreeHitTestResult(row, ProjectTreeHitKind.None, index);
        }

        var rowTop = _rowTops[index];
        var rowHeight = _rowTops[index + 1] - rowTop;
        if (row.ShowDisclosure && ToggleRect(row, rowTop, rowHeight).Contains(point))
        {
            return new ProjectTreeHitTestResult(row, ProjectTreeHitKind.Toggle, index);
        }

        if (row.CanIncludeExternal && IncludeRect(rowTop, rowHeight, Bounds.Width).Contains(point))
        {
            return new ProjectTreeHitTestResult(row, ProjectTreeHitKind.Include, index);
        }

        return new ProjectTreeHitTestResult(row, ProjectTreeHitKind.Row, index);
    }

    private void DrawRow(
        DrawingContext context,
        TreeRowViewModel row,
        int index,
        double rowTop,
        double rowHeight,
        double width,
        FontFamily uiFontFamily,
        FontFamily codeFontFamily)
    {
        if (!row.HasNode)
        {
            return;
        }

        var rowRect = new Rect(0, rowTop, width, rowHeight);
        DrawRowHighlights(context, row, rowRect);
        DrawTreeLines(context, row, rowTop, rowHeight);
        DrawToggle(context, row, index, rowTop, rowHeight);

        var detailLeft = DrawRightDetails(context, row, index, rowTop, rowHeight, width, codeFontFamily);
        DrawName(context, row, rowTop, rowHeight, detailLeft, uiFontFamily);
    }

    private void DrawRowHighlights(DrawingContext context, TreeRowViewModel row, Rect rowRect)
    {
        var borderBrush = Resource("CurrentRowBorderBrush", CurrentRowBorderFallbackBrush);
        if (row.IsSelected)
        {
            using (context.PushOpacity(0.94))
            {
                context.DrawRectangle(
                    Resource("DropdownSelectedBrush", SelectedRowFallbackBrush),
                    new Pen(borderBrush, 1),
                    rowRect,
                    4,
                    4);
            }
        }
        else if (row.ShowCurrentHighlight)
        {
            context.DrawRectangle(
                Resource("CurrentRowBrush", CurrentRowFallbackBrush),
                new Pen(borderBrush, 1),
                rowRect,
                4,
                4);
        }

        if (row.IsRegularFolder)
        {
            var x = ContentLeft + row.RailWidth;
            context.DrawRectangle(
                Resource("DirectoryHighlightBrush", DirectoryHighlightFallbackBrush),
                null,
                new Rect(x, rowRect.Y + 1, Math.Max(0.0, rowRect.Width - x), Math.Max(0.0, rowRect.Height - 2)),
                4,
                4);
        }
    }

    private void DrawTreeLines(DrawingContext context, TreeRowViewModel row, double rowTop, double rowHeight)
    {
        var centerY = Math.Floor(rowTop + rowHeight * 0.5) + 0.5;
        var ancestors = row.AncestorContinues;
        for (var level = 0; level < row.Depth - 1; level++)
        {
            if (level < ancestors.Count && ancestors[level])
            {
                DrawRail(context, level, rowTop, rowTop + rowHeight);
            }
        }

        if (row.IsSpacer)
        {
            DrawRail(context, Math.Max(0, row.Depth - 1), rowTop, rowTop + rowHeight);
            return;
        }

        if (row.Depth <= 0)
        {
            DrawLineToContent(context, 0, centerY, row.RailWidth);
            if (row.HasExpandedChildren)
            {
                DrawRail(context, 0, centerY, rowTop + rowHeight);
            }

            return;
        }

        var parentLevel = row.Depth - 1;
        DrawRail(context, parentLevel, rowTop, centerY);
        DrawLineToContent(context, parentLevel, centerY, row.RailWidth);

        if (!row.IsLast)
        {
            DrawRail(context, parentLevel, centerY, rowTop + rowHeight);
        }

        if (row.HasExpandedChildren)
        {
            DrawRail(context, row.Depth, centerY, rowTop + rowHeight);
        }
    }

    private void DrawToggle(DrawingContext context, TreeRowViewModel row, int index, double rowTop, double rowHeight)
    {
        if (!row.ShowDisclosure)
        {
            return;
        }

        var isHovered = _hoveredIndex == index && _hoveredKind == ProjectTreeHitKind.Toggle;
        var rect = ToggleRect(row, rowTop, rowHeight);
        context.DrawRectangle(
            isHovered ? Resource("HistoryActiveBrush", HistoryActiveFallbackBrush) : Resource("PanelBackgroundBrush", PanelFallbackBrush),
            new Pen(Resource("CommandBorderBrush", CommandBorderFallbackBrush), 1),
            rect,
            ToggleCornerRadius,
            ToggleCornerRadius);

        var arrowX = rect.X + (rect.Width - ArrowSize) * 0.5;
        var arrowY = rect.Y + (rect.Height - ArrowSize) * 0.5;
        using (context.PushTransform(Matrix.CreateTranslation(arrowX, arrowY)))
        {
            context.DrawGeometry(ArrowBrush, null, GetArrowGeometry(row.ArrowAngle));
        }
    }

    private double DrawRightDetails(
        DrawingContext context,
        TreeRowViewModel row,
        int index,
        double rowTop,
        double rowHeight,
        double width,
        FontFamily codeFontFamily)
    {
        var right = Math.Max(ContentLeft, width - RightInset);
        if (row.CanIncludeExternal)
        {
            DrawIncludeButton(context, index, rowTop, rowHeight, width, codeFontFamily);
            return Math.Max(ContentLeft, right - IncludeWidth - DetailSpacing);
        }

        if (row.ShowVersionLabel)
        {
            right = DrawRightText(
                context,
                row.VersionLabel,
                Resource("TextMutedBrush", TextMutedFallbackBrush),
                codeFontFamily,
                FontWeight.Black,
                FontStyle.Normal,
                10.0,
                28.0,
                right,
                rowTop,
                rowHeight);
        }

        if (row.ShowNodeTypeLabel)
        {
            right = DrawRightText(
                context,
                row.NodeTypeLabel,
                row.IsExternal ? Resource("SkipTextBrush", SkipTextFallbackBrush) : Resource("NodeTextBrush", NodeTextFallbackBrush),
                codeFontFamily,
                FontWeight.Black,
                FontStyle.Normal,
                10.0,
                52.0,
                right,
                rowTop,
                rowHeight);
        }

        if (ShowFileDetails)
        {
            if (row.ShowLocMetricLabel)
            {
                right = DrawRightText(
                    context,
                    row.LocMetricLabel,
                    Resource("MetricLocBrush", MetricLocFallbackBrush),
                    codeFontFamily,
                    FontWeight.Black,
                    FontStyle.Normal,
                    10.0,
                    62.0,
                    right,
                    rowTop,
                    rowHeight);
            }

            if (row.ShowFileCountLabel)
            {
                right = DrawRightText(
                    context,
                    row.FileCountLabel,
                    Resource("MetricFileBrush", MetricFileFallbackBrush),
                    codeFontFamily,
                    FontWeight.Black,
                    FontStyle.Normal,
                    10.0,
                    34.0,
                    right,
                    rowTop,
                    rowHeight);
            }
        }

        return right;
    }

    private double DrawRightText(
        DrawingContext context,
        string text,
        IBrush brush,
        FontFamily fontFamily,
        FontWeight weight,
        FontStyle style,
        double fontSize,
        double minWidth,
        double right,
        double rowTop,
        double rowHeight)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return right;
        }

        var formatted = GetFormattedText(text, brush, fontFamily, weight, style, fontSize);
        var slotWidth = Math.Max(minWidth, formatted.Width);
        var slotLeft = Math.Max(ContentLeft, right - slotWidth);
        var textX = Math.Max(slotLeft, right - formatted.Width);
        DrawClippedText(context, formatted, new Rect(slotLeft, rowTop, slotWidth, rowHeight), new Point(textX, CenteredTextY(rowTop, rowHeight, formatted.Height)));
        return slotLeft - DetailSpacing;
    }

    private void DrawName(
        DrawingContext context,
        TreeRowViewModel row,
        double rowTop,
        double rowHeight,
        double detailLeft,
        FontFamily uiFontFamily)
    {
        if (string.IsNullOrWhiteSpace(row.DisplayName))
        {
            return;
        }

        var nameX = ContentLeft + row.RailWidth + NameLeftMargin;
        var nameWidth = Math.Max(0.0, detailLeft - nameX - DetailSpacing);
        if (nameWidth <= 1.0)
        {
            return;
        }

        var brush = row.IsExternal
            ? Resource("ExternalTextBrush", ExternalTextFallbackBrush)
            : row.IsFolder
                ? Resource("FolderTextBrush", FolderTextFallbackBrush)
                : Resource("FileTextBrush", FileTextFallbackBrush);
        var weight = row.IsFolder ? FontWeight.ExtraBold : FontWeight.SemiBold;
        var style = row.IsExternal ? FontStyle.Italic : FontStyle.Normal;
        var formatted = GetFormattedText(row.DisplayName, brush, uiFontFamily, weight, style, 9.5);
        DrawClippedText(context, formatted, new Rect(nameX, rowTop, nameWidth, rowHeight), new Point(nameX, CenteredTextY(rowTop, rowHeight, formatted.Height)));
    }

    private void DrawIncludeButton(
        DrawingContext context,
        int index,
        double rowTop,
        double rowHeight,
        double width,
        FontFamily codeFontFamily)
    {
        var rect = IncludeRect(rowTop, rowHeight, width);
        var isHovered = _hoveredIndex == index && _hoveredKind == ProjectTreeHitKind.Include;
        var background = isHovered
            ? Resource("CommandPrimaryBackgroundBrush", CommandPrimaryBackgroundFallbackBrush)
            : Resource("IncludeBackgroundBrush", IncludeBackgroundFallbackBrush);
        var border = isHovered
            ? Resource("AccentBorderBrush", AccentBorderFallbackBrush)
            : Resource("IncludeBorderBrush", IncludeBorderFallbackBrush);
        var foreground = isHovered
            ? Resource("AccentBrush", AccentFallbackBrush)
            : Resource("IncludeTextBrush", IncludeTextFallbackBrush);

        context.DrawRectangle(background, new Pen(border, 1), rect, IncludeCornerRadius, IncludeCornerRadius);

        var label = isHovered ? "include" : "skip";
        var formatted = GetFormattedText(label, foreground, codeFontFamily, FontWeight.Bold, FontStyle.Normal, 7.5);
        var point = new Point(
            rect.X + Math.Max(0.0, (rect.Width - formatted.Width) * 0.5),
            rect.Y + Math.Max(0.0, (rect.Height - formatted.Height) * 0.5));
        DrawClippedText(context, formatted, rect, point);
    }

    private void DrawLineToContent(DrawingContext context, int level, double y, double railWidth)
    {
        var x = RailX(level);
        context.DrawLine(BranchPen, new Point(x, y), new Point(Math.Max(x, ContentLeft + railWidth - 2), y));
    }

    private void DrawRail(DrawingContext context, int level, double startY, double endY)
    {
        var x = RailX(level);
        context.DrawLine(RailPen, new Point(x, startY), new Point(x, endY));
    }

    private void DrawClippedText(DrawingContext context, FormattedText formatted, Rect clip, Point point)
    {
        if (clip.Width <= 0 || clip.Height <= 0)
        {
            return;
        }

        using (context.PushClip(clip))
        {
            context.DrawText(formatted, point);
        }
    }

    private void AttachItems()
    {
        if (_itemsCollectionChanged is not null)
        {
            _itemsCollectionChanged.CollectionChanged -= OnItemsCollectionChanged;
            _itemsCollectionChanged = null;
        }

        foreach (var row in _subscribedRows)
        {
            row.PropertyChanged -= OnRowPropertyChanged;
        }

        _subscribedRows.Clear();

        if (Items is INotifyCollectionChanged collectionChanged)
        {
            _itemsCollectionChanged = collectionChanged;
            collectionChanged.CollectionChanged += OnItemsCollectionChanged;
        }

        SubscribeRows();
        RebuildRowTops();
    }

    private void SubscribeRows()
    {
        var items = Items;
        if (items is null)
        {
            return;
        }

        foreach (var row in items)
        {
            if (row is INotifyPropertyChanged notify)
            {
                notify.PropertyChanged += OnRowPropertyChanged;
                _subscribedRows.Add(notify);
            }
        }
    }

    private void OnItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var row in _subscribedRows)
        {
            row.PropertyChanged -= OnRowPropertyChanged;
        }

        _subscribedRows.Clear();
        SubscribeRows();
        RebuildRowTops();
    }

    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TreeRowViewModel.RowHeight))
        {
            RebuildRowTops();
            return;
        }

        InvalidateVisual();
    }

    private void RebuildRowTops()
    {
        var items = Items;
        var count = items?.Count ?? 0;
        var rowTops = new double[count + 1];
        var y = ContentTop;
        rowTops[0] = y;

        if (items is not null)
        {
            for (var index = 0; index < count; index++)
            {
                y += Math.Max(1.0, items[index].RowHeight);
                rowTops[index + 1] = y;
            }
        }

        _rowTops = rowTops;
        _totalHeight = y + ContentBottom;
        _hoveredIndex = -1;
        _hoveredKind = ProjectTreeHitKind.None;
        _textCache.Clear();
        InvalidateMeasure();
        InvalidateVisual();
    }

    private int FindRowIndexAtOrAfter(double y)
    {
        var items = Items;
        var count = items?.Count ?? 0;
        if (count == 0 || _rowTops.Length <= count)
        {
            return -1;
        }

        var lo = 0;
        var hi = count - 1;
        while (lo < hi)
        {
            var mid = lo + ((hi - lo) / 2);
            if (_rowTops[mid + 1] <= y)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }

        return lo;
    }

    private void AttachToScrollViewer(ScrollViewer? scrollViewer)
    {
        if (ReferenceEquals(_scrollViewer, scrollViewer))
        {
            return;
        }

        _offsetSubscription?.Dispose();
        _viewportSubscription?.Dispose();
        _offsetSubscription = null;
        _viewportSubscription = null;
        _scrollViewer = scrollViewer;

        if (scrollViewer is null)
        {
            return;
        }

        _offsetSubscription = scrollViewer
            .GetObservable(ScrollViewer.OffsetProperty)
            .Subscribe(new ValueObserver<Vector>(_ => InvalidateVisual()));
        _viewportSubscription = scrollViewer
            .GetObservable(ScrollViewer.ViewportProperty)
            .Subscribe(new ValueObserver<Size>(_ => InvalidateVisual()));
    }

    private void SetHoveredHit(int index, ProjectTreeHitKind kind)
    {
        if (_hoveredIndex == index && _hoveredKind == kind)
        {
            return;
        }

        _hoveredIndex = index;
        _hoveredKind = kind;
        Cursor = kind is ProjectTreeHitKind.Toggle or ProjectTreeHitKind.Include ? HandCursor : ArrowCursor;
        InvalidateVisual();
    }

    private Rect ToggleRect(TreeRowViewModel row, double rowTop, double rowHeight)
    {
        var x = ContentLeft + Math.Max(0.0, RailInset + Math.Max(0, row.Depth) * RailStep - ToggleSize * 0.5);
        return new Rect(x, rowTop + Math.Max(0.0, (rowHeight - ToggleSize) * 0.5), ToggleSize, ToggleSize);
    }

    private static Rect IncludeRect(double rowTop, double rowHeight, double width)
    {
        var x = Math.Max(ContentLeft, width - RightInset - IncludeWidth);
        return new Rect(x, rowTop + Math.Max(0.0, (rowHeight - IncludeHeight) * 0.5), IncludeWidth, IncludeHeight);
    }

    private FormattedText GetFormattedText(
        string text,
        IBrush brush,
        FontFamily fontFamily,
        FontWeight weight,
        FontStyle style,
        double fontSize)
    {
        var key = new TextCacheKey(
            text,
            RuntimeHelpers.GetHashCode(brush),
            fontFamily.ToString(),
            weight,
            style,
            fontSize);
        if (_textCache.TryGetValue(key, out var formatted))
        {
            return formatted;
        }

        if (_textCache.Count > MaxTextCacheEntries)
        {
            _textCache.Clear();
        }

        formatted = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface(fontFamily, style, weight),
            fontSize,
            brush);
        _textCache[key] = formatted;
        return formatted;
    }

    private StreamGeometry GetArrowGeometry(double angle)
    {
        var normalizedAngle = Math.Abs(angle - 90.0) < 0.001 ? 90.0 : 0.0;
        if (_arrowGeometryCache.TryGetValue(normalizedAngle, out var geometry))
        {
            return geometry;
        }

        var center = new Point(ArrowSize * 0.5, ArrowSize * 0.5);
        var backX = ArrowSize * 0.22;
        var halfHeight = ArrowSize * 0.30;
        var tipX = ArrowSize * 0.24;
        var left = Rotate(new Point(center.X - backX, center.Y - halfHeight), center, normalizedAngle);
        var tip = Rotate(new Point(center.X + tipX, center.Y), center, normalizedAngle);
        var right = Rotate(new Point(center.X - backX, center.Y + halfHeight), center, normalizedAngle);

        geometry = new StreamGeometry();
        using (var stream = geometry.Open())
        {
            stream.BeginFigure(left, true);
            stream.LineTo(tip);
            stream.LineTo(right);
            stream.EndFigure(true);
        }

        _arrowGeometryCache[normalizedAngle] = geometry;
        return geometry;
    }

    private T Resource<T>(string key, T fallback)
    {
        for (var control = this as Control; control is not null; control = control.GetVisualParent() as Control)
        {
            if (control.Resources.TryGetValue(key, out var value) && value is T typed)
            {
                return typed;
            }
        }

        if (Application.Current?.Resources.TryGetValue(key, out var appValue) == true && appValue is T appTyped)
        {
            return appTyped;
        }

        return fallback;
    }

    private static FontFamily ResolveFontFamily(string? value, FontFamily fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        try
        {
            return new FontFamily(value);
        }
        catch
        {
            return fallback;
        }
    }

    private IBrush ArrowBrush => IsMatrixTheme(ThemeKey)
        ? MatrixArrowBrush
        : IsDarkTheme(ThemeKey)
            ? DarkArrowBrush
            : EmptyArrowBrush;

    private Pen RailPen => IsMatrixTheme(ThemeKey)
        ? MatrixRailPen
        : IsDarkTheme(ThemeKey)
            ? DarkRailPen
            : EmptyRailPen;

    private Pen BranchPen => IsMatrixTheme(ThemeKey)
        ? MatrixBranchPen
        : IsDarkTheme(ThemeKey)
            ? DarkBranchPen
            : EmptyBranchPen;

    private static double RailX(int level)
    {
        return ContentLeft + Math.Floor(RailInset + Math.Max(0, level) * RailStep) + 0.5;
    }

    private static double CenteredTextY(double rowTop, double rowHeight, double textHeight)
    {
        return rowTop + Math.Max(0.0, (rowHeight - textHeight) * 0.5);
    }

    private static Point Rotate(Point point, Point center, double degrees)
    {
        var radians = degrees * Math.PI / 180.0;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        var x = point.X - center.X;
        var y = point.Y - center.Y;

        return new Point(
            center.X + x * cos - y * sin,
            center.Y + x * sin + y * cos);
    }

    private static bool IsMatrixTheme(string? themeKey)
    {
        return string.Equals(themeKey, "matrix", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDarkTheme(string? themeKey)
    {
        return string.Equals(themeKey, "dark", StringComparison.OrdinalIgnoreCase)
            || string.Equals(themeKey, "nocturne", StringComparison.OrdinalIgnoreCase)
            || string.Equals(themeKey, "onyx", StringComparison.OrdinalIgnoreCase)
            || string.Equals(themeKey, "smoke", StringComparison.OrdinalIgnoreCase)
            || string.Equals(themeKey, "carbon", StringComparison.OrdinalIgnoreCase)
            || string.Equals(themeKey, "obsidian", StringComparison.OrdinalIgnoreCase)
            || string.Equals(themeKey, "ash", StringComparison.OrdinalIgnoreCase)
            || string.Equals(themeKey, "graphene", StringComparison.OrdinalIgnoreCase)
            || string.Equals(themeKey, "ruby", StringComparison.OrdinalIgnoreCase)
            || string.Equals(themeKey, "amethyst", StringComparison.OrdinalIgnoreCase)
            || string.Equals(themeKey, "ember", StringComparison.OrdinalIgnoreCase)
            || string.Equals(themeKey, "cobalt", StringComparison.OrdinalIgnoreCase)
            || string.Equals(themeKey, "contrast", StringComparison.OrdinalIgnoreCase);
    }

    private readonly record struct TextCacheKey(
        string Text,
        int BrushId,
        string FontFamily,
        FontWeight Weight,
        FontStyle Style,
        double FontSize);

    private sealed class ValueObserver<T>(Action<T> onNext) : IObserver<T>
    {
        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }

        public void OnNext(T value)
        {
            onNext(value);
        }
    }
}
