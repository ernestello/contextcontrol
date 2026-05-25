using System.Collections.Specialized;
using System.Globalization;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using ContextControl.Workbench.ViewModels;

namespace ContextControl.Workbench.Controls;

public sealed class ProjectGraphRenderControl : Control
{
    public static readonly StyledProperty<IReadOnlyList<ProjectNodeViewModel>?> ItemsProperty =
        AvaloniaProperty.Register<ProjectGraphRenderControl, IReadOnlyList<ProjectNodeViewModel>?>(nameof(Items));

    public static readonly StyledProperty<ProjectNodeViewModel?> SelectedNodeProperty =
        AvaloniaProperty.Register<ProjectGraphRenderControl, ProjectNodeViewModel?>(
            nameof(SelectedNode),
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<int> GraphVersionProperty =
        AvaloniaProperty.Register<ProjectGraphRenderControl, int>(nameof(GraphVersion));

    public static readonly StyledProperty<int> CenterOnSelectedNodeVersionProperty =
        AvaloniaProperty.Register<ProjectGraphRenderControl, int>(nameof(CenterOnSelectedNodeVersion));

    public static readonly StyledProperty<string> ThemeKeyProperty =
        AvaloniaProperty.Register<ProjectGraphRenderControl, string>(nameof(ThemeKey), "empty");

    public static readonly StyledProperty<string> UiFontFamilyProperty =
        AvaloniaProperty.Register<ProjectGraphRenderControl, string>(nameof(UiFontFamily), "Segoe UI");

    public static readonly StyledProperty<string> CodeFontFamilyProperty =
        AvaloniaProperty.Register<ProjectGraphRenderControl, string>(nameof(CodeFontFamily), "Consolas");

    private const int MaxGraphNodes = 4200;
    private const int MaxChildrenPerParent = 120;
    private const int OmittedCountCap = 99999;
    private const double LayoutMargin = 18.0;
    private const double TreeChildGapY = 18.0;
    private const double TreePackGap = 8.0;
    private const double TreeModuleGap = 24.0;
    private const double TreeRegionPadding = 6.0;
    private const double TreeLabelSidePadding = 10.0;
    private const double RootFileBlockGap = 8.0;
    private const double MinZoom = 0.05;
    private const double MaxZoom = 2.35;
    private const double WheelPanStep = 42.0;
    private const int MaxTextCacheEntries = 4096;
    private const int MaxPenCacheEntries = 768;

    private static readonly FontFamily DefaultUiFontFamily = new("Segoe UI");
    private static readonly FontFamily DefaultCodeFontFamily = new("Consolas");
    private static readonly Cursor HandCursor = new(StandardCursorType.Hand);
    private static readonly Cursor ArrowCursor = new(StandardCursorType.Arrow);
    private static readonly Cursor PanCursor = new(StandardCursorType.SizeAll);

    private static readonly IBrush EditorSurfaceFallbackBrush = Brush.Parse("#F8F7F2");
    private static readonly IBrush PanelBorderFallbackBrush = Brush.Parse("#C9D0D2");
    private static readonly IBrush CommandBackgroundFallbackBrush = Brush.Parse("#EEF1EF");
    private static readonly IBrush DirectoryHighlightFallbackBrush = Brush.Parse("#EBF0EA");
    private static readonly IBrush HistoryActiveFallbackBrush = Brush.Parse("#E7EEF0");
    private static readonly IBrush DropdownSelectedFallbackBrush = Brush.Parse("#CFE8EC");
    private static readonly IBrush TextPrimaryFallbackBrush = Brush.Parse("#31464B");
    private static readonly IBrush TextMutedFallbackBrush = Brush.Parse("#6F7F85");
    private static readonly IBrush FolderTextFallbackBrush = Brush.Parse("#31464B");
    private static readonly IBrush FileTextFallbackBrush = Brush.Parse("#51656B");
    private static readonly IBrush ExternalTextFallbackBrush = Brush.Parse("#C9852B");
    private static readonly IBrush AccentFallbackBrush = Brush.Parse("#227A58");
    private static readonly IBrush AccentBorderFallbackBrush = Brush.Parse("#79BDA0");
    private static readonly IBrush MetricFileFallbackBrush = Brush.Parse("#4E7D88");
    private static readonly IBrush MetricLocFallbackBrush = Brush.Parse("#5E766D");
    private static readonly Color EmptyFolderDarkColor = Color.Parse("#252B2F");
    private static readonly Color EmptyFolderLightColor = Color.Parse("#DEE4E7");
    private static readonly Color EmptyFolderBorderDarkColor = Color.Parse("#58636A");
    private static readonly Color EmptyFolderBorderLightColor = Color.Parse("#88959B");
    private static readonly Color[] LightRegionPalette =
    [
        Color.Parse("#D8EBFA"),
        Color.Parse("#E3E7FB"),
        Color.Parse("#D6F1F3"),
        Color.Parse("#E8E1FA"),
        Color.Parse("#DDEFF8"),
        Color.Parse("#DCEAFD"),
        Color.Parse("#D3F0EA"),
        Color.Parse("#E7EDFA")
    ];
    private static readonly Color[] DarkRegionPalette =
    [
        Color.Parse("#102B3C"),
        Color.Parse("#1D2646"),
        Color.Parse("#10363C"),
        Color.Parse("#292346"),
        Color.Parse("#143245"),
        Color.Parse("#172E49"),
        Color.Parse("#12372F"),
        Color.Parse("#1A2D42")
    ];

    private readonly List<GraphNode> _roots = [];
    private readonly List<GraphNode> _nodes = [];
    private readonly List<GraphEdge> _edges = [];
    private readonly List<EdgeRoute> _edgeRoutes = [];
    private readonly List<EdgeSegment> _edgeRouteSegments = [];
    private readonly Dictionary<string, Point> _manualPositions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<uint, IBrush> _solidBrushCache = new();
    private readonly Dictionary<PenCacheKey, Pen> _penCache = new();
    private readonly Dictionary<TextCacheKey, FormattedText> _textCache = new();
    private static readonly Pen EdgeRouteRailPen = new(Brushes.Transparent);
    private static readonly Pen EdgeRouteBranchPen = new(Brushes.Transparent);

    private INotifyCollectionChanged? _itemsCollectionChanged;
    private bool _isBuildingEdgeRoutes;
    private bool _currentEdgeRouteHasSegments;
    private bool _layoutDirty = true;
    private bool _fitRequested = true;
    private bool _viewportInitialized;
    private Rect _currentEdgeRouteBounds;
    private Rect _contentBounds = new(0, 0, 1, 1);
    private double _zoom = 1.0;
    private Vector _pan = new(22, 22);
    private GraphNode? _dragNode;
    private Point _dragStartWorld;
    private Point _dragStartNode;
    private bool _isPanning;
    private Point _panStartScreen;
    private Vector _panStart;
    private GraphNode? _hoveredNode;

    public IReadOnlyList<ProjectNodeViewModel>? Items
    {
        get => GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    public ProjectNodeViewModel? SelectedNode
    {
        get => GetValue(SelectedNodeProperty);
        set => SetValue(SelectedNodeProperty, value);
    }

    public int GraphVersion
    {
        get => GetValue(GraphVersionProperty);
        set => SetValue(GraphVersionProperty, value);
    }

    public int CenterOnSelectedNodeVersion
    {
        get => GetValue(CenterOnSelectedNodeVersionProperty);
        set => SetValue(CenterOnSelectedNodeVersionProperty, value);
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

    public ProjectGraphRenderControl()
    {
        Focusable = true;
    }

    public void FitToView()
    {
        _fitRequested = true;
        InvalidateVisual();
    }

    public void ResetLayout()
    {
        _manualPositions.Clear();
        _layoutDirty = true;
        _fitRequested = true;
        InvalidateVisual();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ItemsProperty)
        {
            AttachItems();
            _fitRequested = true;
            MarkLayoutDirty();
        }
        else if (change.Property == GraphVersionProperty)
        {
            MarkLayoutDirty();
        }
        else if (change.Property == CenterOnSelectedNodeVersionProperty)
        {
            CenterSelectedNode();
        }
        else if (change.Property == ThemeKeyProperty)
        {
            _solidBrushCache.Clear();
            _penCache.Clear();
            _textCache.Clear();
            InvalidateVisual();
        }
        else if (change.Property == UiFontFamilyProperty
            || change.Property == CodeFontFamilyProperty
            || change.Property == SelectedNodeProperty)
        {
            _textCache.Clear();
            InvalidateVisual();
        }
    }

    public void CenterSelectedNode()
    {
        if (SelectedNode is null || Bounds.Width <= 1 || Bounds.Height <= 1)
        {
            return;
        }

        EnsureLayout();
        var target = FindGraphNode(SelectedNode);
        if (target is null)
        {
            return;
        }

        _fitRequested = false;
        _viewportInitialized = true;
        _pan = new Vector(
            Bounds.Width * 0.5 - target.Bounds.Center.X * _zoom,
            Bounds.Height * 0.5 - target.Bounds.Center.Y * _zoom);
        InvalidateVisual();
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (!_viewportInitialized)
        {
            _fitRequested = true;
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();

        var point = e.GetPosition(this);
        var properties = e.GetCurrentPoint(this).Properties;
        if (properties.IsLeftButtonPressed)
        {
            var hit = HitTestGraphNode(point);
            if (hit is not null)
            {
                SelectedNode = hit.Node;
                _dragNode = hit;
                _dragStartWorld = ScreenToWorld(point);
                _dragStartNode = hit.Bounds.Position;
                _manualPositions[hit.Key] = hit.Bounds.Position;
                e.Pointer.Capture(this);
                Cursor = HandCursor;
                e.Handled = true;
                InvalidateVisual();
                return;
            }

            _isPanning = true;
            _panStartScreen = point;
            _panStart = _pan;
            e.Pointer.Capture(this);
            Cursor = PanCursor;
            e.Handled = true;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var point = e.GetPosition(this);

        if (_dragNode is not null)
        {
            var world = ScreenToWorld(point);
            var delta = world - _dragStartWorld;
            var next = _dragStartNode + delta;
            _dragNode.Bounds = new Rect(next, _dragNode.Bounds.Size);
            _manualPositions[_dragNode.Key] = next;
            _contentBounds = CalculateContentBounds();
            RebuildEdgeRoutes();
            _viewportInitialized = true;
            e.Handled = true;
            InvalidateVisual();
            return;
        }

        if (_isPanning)
        {
            var delta = point - _panStartScreen;
            _pan = _panStart + delta;
            _viewportInitialized = true;
            e.Handled = true;
            InvalidateVisual();
            return;
        }

        var hovered = HitTestGraphNode(point);
        if (!ReferenceEquals(_hoveredNode, hovered))
        {
            _hoveredNode = hovered;
            Cursor = hovered is null ? ArrowCursor : HandCursor;
            InvalidateVisual();
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _dragNode = null;
        _isPanning = false;
        e.Pointer.Capture(null);
        Cursor = _hoveredNode is null ? ArrowCursor : HandCursor;
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (_dragNode is null && !_isPanning)
        {
            _hoveredNode = null;
            Cursor = ArrowCursor;
            InvalidateVisual();
        }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        if ((e.KeyModifiers & KeyModifiers.Control) != 0)
        {
            var factor = e.Delta.Y > 0 ? 1.12 : 0.89;
            ZoomAt(e.GetPosition(this), factor);
        }
        else if ((e.KeyModifiers & KeyModifiers.Shift) != 0)
        {
            _pan += new Vector(e.Delta.Y * WheelPanStep, 0);
        }
        else
        {
            _pan += new Vector(e.Delta.X * WheelPanStep, e.Delta.Y * WheelPanStep);
        }

        _viewportInitialized = true;
        e.Handled = true;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        EnsureLayout();
        FitViewportIfRequested();

        var viewport = new Rect(0, 0, Bounds.Width, Bounds.Height);
        var worldViewport = ScreenToWorldViewport(viewport, 100);
        var resources = ResolveRenderResources();
        context.DrawRectangle(resources.EditorSurface, null, viewport);

        if (_nodes.Count == 0)
        {
            DrawEmptyState(context, resources);
            return;
        }

        DrawGrid(context, viewport, resources);
        DrawRegions(context, worldViewport, resources);
        DrawEdges(context, worldViewport, resources);
        DrawNodes(context, worldViewport, resources);
    }

    private void AttachItems()
    {
        if (_itemsCollectionChanged is not null)
        {
            _itemsCollectionChanged.CollectionChanged -= OnItemsCollectionChanged;
            _itemsCollectionChanged = null;
        }

        if (Items is INotifyCollectionChanged collectionChanged)
        {
            _itemsCollectionChanged = collectionChanged;
            collectionChanged.CollectionChanged += OnItemsCollectionChanged;
        }
    }

    private void OnItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        _fitRequested = true;
        MarkLayoutDirty();
    }

    private void MarkLayoutDirty()
    {
        _layoutDirty = true;
        _textCache.Clear();
        InvalidateVisual();
    }

    private void EnsureLayout()
    {
        if (!_layoutDirty)
        {
            return;
        }

        _layoutDirty = false;
        _roots.Clear();
        _nodes.Clear();
        _edges.Clear();
        _edgeRoutes.Clear();
        _edgeRouteSegments.Clear();

        var remaining = MaxGraphNodes;
        var roots = Items;
        if (roots is not null)
        {
            foreach (var root in roots)
            {
                if (remaining <= 0)
                {
                    break;
                }

                var graphRoot = AddGraphNode(null, root, 0, ref remaining);
                if (graphRoot is not null)
                {
                    _roots.Add(graphRoot);
                }
            }
        }

        foreach (var root in _roots)
        {
            MeasureWeight(root);
        }

        foreach (var root in _roots)
        {
            MeasureTreeSize(root);
        }

        if (_roots.Count == 1)
        {
            AssignTreeLayout(_roots[0], new Point(LayoutMargin, LayoutMargin));
        }
        else
        {
            var plan = CreatePackPlan(_roots, node => node.TreeSize, TreeAspect, TreePackGap, TreeModuleGap, forceGrid: false);
            foreach (var item in plan.Items)
            {
                AssignTreeLayout(item.Node, new Point(LayoutMargin + item.Offset.X, LayoutMargin + item.Offset.Y));
            }
        }

        foreach (var node in _nodes)
        {
            if (_manualPositions.TryGetValue(node.Key, out var pinned))
            {
                node.Bounds = new Rect(pinned, node.Bounds.Size);
                node.IsPinned = true;
            }
            else
            {
                node.IsPinned = false;
            }
        }

        _contentBounds = CalculateContentBounds();
        RebuildEdgeRoutes();
    }

    private GraphNode? AddGraphNode(GraphNode? parent, ProjectNodeViewModel node, int depth, ref int remaining)
    {
        if (remaining <= 0)
        {
            return null;
        }

        remaining--;
        var graphNode = new GraphNode(node, parent, depth);
        AddGraphNode(parent, graphNode);

        var orderedChildren = new List<ProjectNodeViewModel>(node.Children);
        orderedChildren.Sort(CompareProjectGraphChildren);
        var shownChildren = 0;
        var omittedCount = 0;
        var omittedCapped = false;

        foreach (var child in orderedChildren)
        {
            if (shownChildren >= MaxChildrenPerParent || remaining <= 1)
            {
                var counted = CountSubtreeNodesCapped(child, OmittedCountCap - omittedCount);
                omittedCount += counted.Count;
                omittedCapped |= counted.Capped;
                if (omittedCount >= OmittedCountCap)
                {
                    omittedCount = OmittedCountCap;
                    omittedCapped = true;
                }

                continue;
            }

            if (AddGraphNode(graphNode, child, depth + 1, ref remaining) is not null)
            {
                shownChildren++;
            }
        }

        if (omittedCount > 0 && remaining > 0)
        {
            remaining--;
            AddGraphNode(graphNode, GraphNode.CreateAggregate(graphNode, depth + 1, omittedCount, omittedCapped));
        }

        return graphNode;
    }

    private void AddGraphNode(GraphNode? parent, GraphNode graphNode)
    {
        parent?.Children.Add(graphNode);
        _nodes.Add(graphNode);
        if (parent is not null)
        {
            _edges.Add(new GraphEdge(parent, graphNode));
        }
    }

    private static CountResult CountSubtreeNodesCapped(ProjectNodeViewModel node, int cap)
    {
        if (cap <= 0)
        {
            return new CountResult(0, true);
        }

        var count = 0;
        var stack = new Stack<ProjectNodeViewModel>();
        stack.Push(node);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            count++;
            if (count >= cap)
            {
                return new CountResult(cap, true);
            }

            foreach (var child in current.Children)
            {
                stack.Push(child);
            }
        }

        return new CountResult(count, false);
    }

    private static double MeasureWeight(GraphNode node)
    {
        if (node.Children.Count == 0)
        {
            node.Weight = 1.0;
            return node.Weight;
        }

        var weight = 0.0;
        foreach (var child in node.Children)
        {
            weight += MeasureWeight(child);
        }

        node.Weight = Math.Max(1.0, weight);
        return node.Weight;
    }

    private static int CompareProjectGraphChildren(ProjectNodeViewModel left, ProjectNodeViewModel right)
    {
        if (left.IsFolder != right.IsFolder)
        {
            return left.IsFolder ? -1 : 1;
        }

        return StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name);
    }

    private Size MeasureTreeSize(GraphNode node)
    {
        var nodeSize = MeasureNodeSize(node);
        if (node.Children.Count == 0)
        {
            node.TreePlan = null;
            node.TreeSize = nodeSize;
            return node.TreeSize;
        }

        foreach (var child in node.Children)
        {
            MeasureTreeSize(child);
        }

        var plan = node.Parent is null
            ? CreateRootPackPlan(node, child => child.TreeSize)
            : CreatePackPlan(node.Children, child => child.TreeSize, TreeAspect, TreePackGap, TreeModuleGap, ShouldUseTreeGrid(node.Children));
        node.TreePlan = plan;
        var labelOverlap = nodeSize.Height * 0.5;
        var regionWidth = node.Parent is null
            ? Math.Max(plan.Width, nodeSize.Width + TreeLabelSidePadding * 2.0)
            : Math.Max(
                plan.Width + TreeRegionPadding * 2.0,
                nodeSize.Width + TreeLabelSidePadding * 2.0);
        var regionHeight = node.Parent is null
            ? Math.Max(plan.Height, node.RootLabelOffsetY + nodeSize.Height)
            : labelOverlap + TreeChildGapY + plan.Height + TreeRegionPadding;
        node.TreeSize = new Size(
            regionWidth,
            node.Parent is null ? regionHeight : labelOverlap + regionHeight);
        return node.TreeSize;
    }

    private void AssignTreeLayout(GraphNode node, Point origin)
    {
        var nodeSize = MeasureNodeSize(node);
        if (node.Children.Count == 0)
        {
            node.RegionBounds = default;
            node.Bounds = new Rect(origin, nodeSize);
            return;
        }

        var plan = node.TreePlan ?? (node.Parent is null
            ? CreateRootPackPlan(node, child => child.TreeSize)
            : CreatePackPlan(node.Children, child => child.TreeSize, TreeAspect, TreePackGap, TreeModuleGap, ShouldUseTreeGrid(node.Children)));
        var labelOverlap = nodeSize.Height * 0.5;
        if (node.Parent is null)
        {
            node.RegionBounds = new Rect(origin, node.TreeSize);
            node.Bounds = new Rect(
                origin.X + Math.Max(0, (node.TreeSize.Width - nodeSize.Width) * 0.5),
                origin.Y + node.RootLabelOffsetY,
                nodeSize.Width,
                nodeSize.Height);

            var rootChildX = Math.Max(0, (node.TreeSize.Width - plan.Width) * 0.5);
            foreach (var item in plan.Items)
            {
                AssignTreeLayout(item.Node, new Point(origin.X + rootChildX + item.Offset.X, origin.Y + item.Offset.Y));
            }

            return;
        }

        node.RegionBounds = new Rect(
            origin.X,
            origin.Y + labelOverlap,
            node.TreeSize.Width,
            Math.Max(1.0, node.TreeSize.Height - labelOverlap));
        node.Bounds = new Rect(
            origin.X + Math.Max(0, (node.TreeSize.Width - nodeSize.Width) * 0.5),
            origin.Y,
            nodeSize.Width,
            nodeSize.Height);

        var childOrigin = new Point(
            node.RegionBounds.X + Math.Max(0, (node.RegionBounds.Width - plan.Width) * 0.5),
            node.RegionBounds.Y + labelOverlap + TreeChildGapY);
        foreach (var item in plan.Items)
        {
            AssignTreeLayout(item.Node, new Point(childOrigin.X + item.Offset.X, childOrigin.Y + item.Offset.Y));
        }
    }

    private GraphPackPlan CreatePackPlan(
        IReadOnlyList<GraphNode> children,
        Func<GraphNode, Size> getSize,
        double aspect,
        double gap,
        double moduleGap,
        bool forceGrid)
    {
        if (children.Count == 0)
        {
            return new GraphPackPlan([], 0, 0);
        }

        if (HasMixedPackKinds(children))
        {
            return CreateMixedPackPlan(children, getSize, aspect, gap, moduleGap);
        }

        return CreateUnmixedPackPlan(children, getSize, aspect, gap, moduleGap, forceGrid);
    }

    private GraphPackPlan CreateRootPackPlan(
        GraphNode root,
        Func<GraphNode, Size> getSize)
    {
        var children = root.Children;
        if (children.Count == 0)
        {
            root.RootLabelOffsetY = 0;
            return new GraphPackPlan([], 0, 0);
        }

        var rootSize = MeasureNodeSize(root);
        var modules = children.Where(IsPackModule).ToArray();
        var files = children.Where(child => !IsPackModule(child)).ToArray();
        var modulePlan = modules.Length == 0
            ? new GraphPackPlan([], 0, 0)
            : CreateSingleRowPackPlan(modules, getSize, TreeModuleGap);
        var filePlan = files.Length == 0
            ? new GraphPackPlan([], 0, 0)
            : CreateGridPackPlan(files, getSize, TreeAspect, TreePackGap);
        root.RootLabelOffsetY = filePlan.Height > 0
            ? filePlan.Height + RootFileBlockGap
            : 0;
        var moduleY = root.RootLabelOffsetY + rootSize.Height + TreeChildGapY;
        var width = Math.Max(Math.Max(modulePlan.Width, filePlan.Width), rootSize.Width + TreeLabelSidePadding * 2.0);
        var items = new List<GraphPackItem>(children.Count);
        var moduleX = Math.Max(0, (width - modulePlan.Width) * 0.5);
        var fileX = Math.Max(0, (width - filePlan.Width) * 0.5);
        foreach (var item in filePlan.Items)
        {
            items.Add(new GraphPackItem(item.Node, new Point(item.Offset.X + fileX, item.Offset.Y)));
        }

        foreach (var item in modulePlan.Items)
        {
            items.Add(new GraphPackItem(item.Node, new Point(item.Offset.X + moduleX, item.Offset.Y + moduleY)));
        }

        var height = Math.Max(
            root.RootLabelOffsetY + rootSize.Height,
            modulePlan.Height > 0 ? moduleY + modulePlan.Height : 0);
        return new GraphPackPlan(items, width, height);
    }

    private static GraphPackPlan CreateSingleRowPackPlan(
        IReadOnlyList<GraphNode> children,
        Func<GraphNode, Size> getSize,
        double gap)
    {
        if (children.Count == 0)
        {
            return new GraphPackPlan([], 0, 0);
        }

        var items = new List<GraphPackItem>(children.Count);
        var x = 0.0;
        var height = 0.0;
        for (var index = 0; index < children.Count; index++)
        {
            var child = children[index];
            if (index > 0)
            {
                x += gap;
            }

            items.Add(new GraphPackItem(child, new Point(x, 0)));
            var size = getSize(child);
            x += size.Width;
            height = Math.Max(height, size.Height);
        }

        return new GraphPackPlan(items, x, height);
    }

    private GraphPackPlan CreateUnmixedPackPlan(
        IReadOnlyList<GraphNode> children,
        Func<GraphNode, Size> getSize,
        double aspect,
        double gap,
        double moduleGap,
        bool forceGrid)
    {
        return forceGrid
            ? CreateGridPackPlan(children, getSize, aspect, gap)
            : CreateMasonryPackPlan(children, getSize, aspect, gap, moduleGap);
    }

    private GraphPackPlan CreateMixedPackPlan(
        IReadOnlyList<GraphNode> children,
        Func<GraphNode, Size> getSize,
        double aspect,
        double gap,
        double moduleGap)
    {
        var modules = children.Where(IsPackModule).ToArray();
        var files = children.Where(child => !IsPackModule(child)).ToArray();
        var modulePlan = modules.Length == 0
            ? new GraphPackPlan([], 0, 0)
            : CreateUnmixedPackPlan(modules, getSize, aspect, gap, moduleGap, forceGrid: false);
        var filePlan = files.Length == 0
            ? new GraphPackPlan([], 0, 0)
            : CreateUnmixedPackPlan(files, getSize, aspect, gap, moduleGap, ShouldUseFileGrid(files, getSize));
        var items = new List<GraphPackItem>(children.Count);

        foreach (var item in modulePlan.Items)
        {
            items.Add(item);
        }

        var fileY = modulePlan.Height > 0 && filePlan.Height > 0
            ? modulePlan.Height + moduleGap
            : modulePlan.Height;
        foreach (var item in filePlan.Items)
        {
            items.Add(new GraphPackItem(item.Node, new Point(item.Offset.X, item.Offset.Y + fileY)));
        }

        return new GraphPackPlan(
            items,
            Math.Max(modulePlan.Width, filePlan.Width),
            fileY + filePlan.Height);
    }

    private GraphPackPlan CreateGridPackPlan(
        IReadOnlyList<GraphNode> children,
        Func<GraphNode, Size> getSize,
        double aspect,
        double gap)
    {
        var cellWidth = children.Max(child => getSize(child).Width);
        var cellHeight = children.Max(child => getSize(child).Height);
        var columns = ChooseGridColumns(children.Count, cellWidth, cellHeight, aspect);
        var items = new List<GraphPackItem>(children.Count);

        for (var index = 0; index < children.Count; index++)
        {
            var row = index / columns;
            var column = index % columns;
            items.Add(new GraphPackItem(
                children[index],
                new Point(column * (cellWidth + gap), row * (cellHeight + gap))));
        }

        var rows = (int)Math.Ceiling(children.Count / (double)columns);
        return new GraphPackPlan(
            items,
            columns * cellWidth + Math.Max(0, columns - 1) * gap,
            rows * cellHeight + Math.Max(0, rows - 1) * gap);
    }

    private GraphPackPlan CreateMasonryPackPlan(
        IReadOnlyList<GraphNode> children,
        Func<GraphNode, Size> getSize,
        double aspect,
        double gap,
        double moduleGap)
    {
        var items = children
            .Select(node => new SizedGraphNode(node, getSize(node)))
            .ToArray();
        var maxWidth = items.Max(item => item.Size.Width);
        var totalWidth = MeasureInlinePackWidth(items, gap, moduleGap);
        var totalArea = items.Sum(item => item.Size.Width * item.Size.Height);
        var desiredWidth = Math.Sqrt(Math.Max(1.0, totalArea) * Math.Max(0.8, aspect));
        var minWidth = Math.Min(totalWidth, Math.Max(maxWidth, items.Average(item => item.Size.Width) * 1.35));
        var maxCandidateWidth = Math.Max(minWidth, Math.Min(totalWidth, desiredWidth * 1.85));
        var candidates = new[]
            {
                minWidth,
                desiredWidth * 0.72,
                desiredWidth * 0.9,
                desiredWidth,
                desiredWidth * 1.16,
                desiredWidth * 1.36,
                maxCandidateWidth
            }
            .Select(width => Math.Clamp(width, minWidth, totalWidth))
            .Distinct()
            .ToArray();

        GraphPackPlan? best = null;
        var bestScore = double.MaxValue;
        foreach (var targetWidth in candidates)
        {
            var plan = CreateShelfPackPlan(items, targetWidth, gap, moduleGap);
            if (plan.Width <= 0 || plan.Height <= 0)
            {
                continue;
            }

            var actualAspect = plan.Width / Math.Max(1.0, plan.Height);
            var aspectPenalty = Math.Abs(Math.Log(Math.Max(0.05, actualAspect) / Math.Max(0.05, aspect)));
            var score = plan.Width * plan.Height * (1.0 + aspectPenalty * 0.22);
            if (score < bestScore)
            {
                best = plan;
                bestScore = score;
            }
        }

        return best ?? CreateShelfPackPlan(items, Math.Max(minWidth, desiredWidth), gap, moduleGap);
    }

    private static GraphPackPlan CreateShelfPackPlan(
        IReadOnlyList<SizedGraphNode> items,
        double targetWidth,
        double gap,
        double moduleGap)
    {
        var placements = new List<GraphPackItem>(items.Count);
        var x = 0.0;
        var y = 0.0;
        var rowHeight = 0.0;
        var rowHasModule = false;
        var width = 0.0;
        SizedGraphNode? previous = null;

        foreach (var item in items)
        {
            var inlineGap = previous is null ? 0.0 : GapBetween(previous.Value.Node, item.Node, gap, moduleGap);
            if (x > 0 && x + inlineGap + item.Size.Width > targetWidth)
            {
                width = Math.Max(width, x);
                var rowGap = rowHasModule || IsPackModule(item.Node) ? moduleGap : gap;
                x = 0;
                y += rowHeight + rowGap;
                rowHeight = 0;
                rowHasModule = false;
                previous = null;
                inlineGap = 0;
            }

            x += inlineGap;
            placements.Add(new GraphPackItem(item.Node, new Point(x, y)));
            x += item.Size.Width;
            rowHeight = Math.Max(rowHeight, item.Size.Height);
            rowHasModule |= IsPackModule(item.Node);
            previous = item;
        }

        width = Math.Max(width, x);
        var height = y + rowHeight;
        return new GraphPackPlan(placements, width, height);
    }

    private static double MeasureInlinePackWidth(IReadOnlyList<SizedGraphNode> items, double gap, double moduleGap)
    {
        var width = 0.0;
        SizedGraphNode? previous = null;
        foreach (var item in items)
        {
            width += item.Size.Width;
            if (previous is not null)
            {
                width += GapBetween(previous.Value.Node, item.Node, gap, moduleGap);
            }

            previous = item;
        }

        return width;
    }

    private static double GapBetween(GraphNode previous, GraphNode next, double gap, double moduleGap)
    {
        return IsPackModule(previous) || IsPackModule(next) ? moduleGap : gap;
    }

    private static bool IsPackModule(GraphNode node)
    {
        return node.Children.Count > 0 || node.Node?.IsFolder == true;
    }

    private static bool HasMixedPackKinds(IReadOnlyList<GraphNode> children)
    {
        var hasModule = false;
        var hasFile = false;
        foreach (var child in children)
        {
            if (IsPackModule(child))
            {
                hasModule = true;
            }
            else
            {
                hasFile = true;
            }

            if (hasModule && hasFile)
            {
                return true;
            }
        }

        return false;
    }

    private static bool ShouldUseFileGrid(IReadOnlyList<GraphNode> children, Func<GraphNode, Size> getSize)
    {
        return children.Count >= 8 && HasUniformPackSize(children, getSize);
    }

    private bool ShouldUseTreeGrid(IReadOnlyList<GraphNode> children)
    {
        if (children.Count < 10)
        {
            return false;
        }

        var leafish = children.Count(child => child.Children.Count == 0);
        return leafish / (double)children.Count >= 0.90
            && HasUniformPackSize(children, child => child.TreeSize);
    }

    private static bool HasUniformPackSize(IReadOnlyList<GraphNode> children, Func<GraphNode, Size> getSize)
    {
        var minWidth = double.MaxValue;
        var minHeight = double.MaxValue;
        var maxWidth = 0.0;
        var maxHeight = 0.0;
        foreach (var child in children)
        {
            var size = getSize(child);
            minWidth = Math.Min(minWidth, Math.Max(1.0, size.Width));
            minHeight = Math.Min(minHeight, Math.Max(1.0, size.Height));
            maxWidth = Math.Max(maxWidth, size.Width);
            maxHeight = Math.Max(maxHeight, size.Height);
        }

        return maxWidth / minWidth <= 1.55
            && maxHeight / minHeight <= 1.45;
    }

    private static int ChooseGridColumns(int count, double cellWidth, double cellHeight, double aspect)
    {
        var cellAspect = Math.Max(0.1, cellWidth / Math.Max(1.0, cellHeight));
        var desired = Math.Sqrt(count * aspect / cellAspect);
        return Math.Clamp((int)Math.Ceiling(desired), 1, Math.Min(count, 42));
    }

    private static Size MeasureNodeSize(GraphNode node)
    {
        if (node.IsAggregate)
        {
            return new Size(88, 22);
        }

        if (node.Node?.IsFolder == true)
        {
            return node.Depth == 0 ? new Size(136, 34) : new Size(118, 28);
        }

        return new Size(112, 30);
    }

    private Rect CalculateContentBounds()
    {
        if (_nodes.Count == 0)
        {
            return new Rect(0, 0, 1, 1);
        }

        var first = true;
        var left = 0.0;
        var top = 0.0;
        var right = 0.0;
        var bottom = 0.0;

        foreach (var node in _nodes)
        {
            Include(node.Bounds);
            if (node.RegionBounds.Width > 0 && node.RegionBounds.Height > 0)
            {
                Include(node.RegionBounds);
            }
        }

        return new Rect(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top));

        void Include(Rect rect)
        {
            if (first)
            {
                left = rect.Left;
                top = rect.Top;
                right = rect.Right;
                bottom = rect.Bottom;
                first = false;
                return;
            }

            left = Math.Min(left, rect.Left);
            top = Math.Min(top, rect.Top);
            right = Math.Max(right, rect.Right);
            bottom = Math.Max(bottom, rect.Bottom);
        }
    }

    private void FitViewportIfRequested()
    {
        if (!_fitRequested || Bounds.Width <= 1 || Bounds.Height <= 1)
        {
            return;
        }

        _fitRequested = false;
        _viewportInitialized = true;

        var availableWidth = Math.Max(1.0, Bounds.Width - 44.0);
        var availableHeight = Math.Max(1.0, Bounds.Height - 44.0);
        var scale = Math.Min(availableWidth / _contentBounds.Width, availableHeight / _contentBounds.Height);
        _zoom = Math.Clamp(scale, MinZoom, 1.15);
        _pan = new Vector(
            22.0 - _contentBounds.Left * _zoom,
            22.0 - _contentBounds.Top * _zoom);
    }

    private void DrawEmptyState(DrawingContext context, RenderResources resources)
    {
        var text = GetFormattedText(
            "Open a project to draw its architecture graph.",
            resources.TextMuted,
            resources.UiFont,
            resources.UiFontKey,
            FontWeight.SemiBold,
            FontStyle.Normal,
            12);
        var point = new Point(
            Math.Max(12, (Bounds.Width - text.Width) * 0.5),
            Math.Max(12, (Bounds.Height - text.Height) * 0.5));
        context.DrawText(text, point);
    }

    private void DrawGrid(DrawingContext context, Rect viewport, RenderResources resources)
    {
        var pen = CachedPen(resources.PanelBorder, 0.55);
        var step = Math.Max(28, 56 * _zoom);
        var startX = _pan.X % step;
        var startY = _pan.Y % step;

        using (context.PushOpacity(resources.IsDark ? 0.20 : 0.35))
        {
            for (var x = startX; x < viewport.Width; x += step)
            {
                context.DrawLine(pen, new Point(x, 0), new Point(x, viewport.Height));
            }

            for (var y = startY; y < viewport.Height; y += step)
            {
                context.DrawLine(pen, new Point(0, y), new Point(viewport.Width, y));
            }
        }
    }

    private void DrawRegions(DrawingContext context, Rect worldViewport, RenderResources resources)
    {
        var activePen = CachedPen(resources.AccentBorder, Math.Clamp(1.2 * _zoom, 0.7, 1.45));

        foreach (var node in _nodes)
        {
            if (node.Parent is null
                || node.Children.Count == 0
                || node.RegionBounds.Width <= 0
                || node.RegionBounds.Height <= 0)
            {
                continue;
            }

            if (!RectMayIntersectWorld(node.RegionBounds, worldViewport))
            {
                continue;
            }

            var rect = ToScreen(node.RegionBounds);
            var selected = IsSelectedNode(node);
            var fillBrush = RegionFillBrush(node, resources);
            var borderPen = CachedPen(RegionBorderBrush(node, resources), Math.Clamp(0.95 * _zoom, 0.55, 1.2));
            var borderOpacity = selected ? 0.74 : 0.46;
            var radius = Math.Clamp(1.5 * _zoom, 0.5, 1.5);

            context.DrawRectangle(
                fillBrush,
                null,
                rect,
                radius,
                radius);

            using (context.PushOpacity(borderOpacity))
            {
                context.DrawRectangle(
                    null,
                    selected ? activePen : borderPen,
                    rect,
                    radius,
                    radius);
            }
        }
    }

    private void DrawEdges(DrawingContext context, Rect worldViewport, RenderResources resources)
    {
        if (_edgeRoutes.Count == 0)
        {
            return;
        }

        var edgePen = CachedPen(resources.MetricFile, Math.Clamp(1.02 * _zoom, 0.55, 1.25));
        var railPen = CachedPen(resources.MetricFile, Math.Clamp(1.18 * _zoom, 0.65, 1.45));
        var haloPen = CachedPen(resources.EditorSurface, Math.Clamp(2.8 * _zoom, 1.4, 3.2));

        using (context.PushOpacity(resources.IsDark ? 0.55 : 0.68))
        {
            foreach (var route in _edgeRoutes)
            {
                DrawEdgeRoute(context, route, haloPen, haloPen, worldViewport);
            }
        }

        using (context.PushOpacity(0.78))
        {
            foreach (var route in _edgeRoutes)
            {
                DrawEdgeRoute(context, route, railPen, edgePen, worldViewport);
            }
        }
    }

    private void DrawEdgeRoute(
        DrawingContext context,
        EdgeRoute route,
        Pen railPen,
        Pen branchPen,
        Rect worldViewport)
    {
        if (!RectMayIntersectWorld(route.Bounds, worldViewport))
        {
            return;
        }

        if (route.ClipBounds.Width > 0 && route.ClipBounds.Height > 0)
        {
            using (context.PushClip(ToScreen(route.ClipBounds)))
            {
                DrawEdgeRouteSegments(context, route, railPen, branchPen, worldViewport);
            }
            return;
        }

        DrawEdgeRouteSegments(context, route, railPen, branchPen, worldViewport);
    }

    private void DrawEdgeRouteSegments(
        DrawingContext context,
        EdgeRoute route,
        Pen railPen,
        Pen branchPen,
        Rect worldViewport)
    {
        var end = route.SegmentStart + route.SegmentCount;
        for (var index = route.SegmentStart; index < end; index++)
        {
            var segment = _edgeRouteSegments[index];
            if (!SegmentMayIntersectWorld(segment.Start, segment.End, worldViewport))
            {
                continue;
            }

            DrawWorldLine(context, segment.IsRail ? railPen : branchPen, segment.Start, segment.End);
        }
    }

    private void RebuildEdgeRoutes()
    {
        _edgeRoutes.Clear();
        _edgeRouteSegments.Clear();
        _isBuildingEdgeRoutes = true;

        try
        {
            foreach (var parent in _nodes)
            {
                if (parent.Children.Count == 0)
                {
                    continue;
                }

                var segmentStart = _edgeRouteSegments.Count;
                _currentEdgeRouteHasSegments = false;
                _currentEdgeRouteBounds = default;

                DrawFanout(null!, parent, parent.Children, EdgeRouteRailPen, EdgeRouteBranchPen);

                var segmentCount = _edgeRouteSegments.Count - segmentStart;
                if (segmentCount <= 0 || !_currentEdgeRouteHasSegments)
                {
                    continue;
                }

                var clipBounds = parent.Parent is null
                    ? default
                    : parent.RegionBounds.Width > 0 && parent.RegionBounds.Height > 0
                    ? parent.RegionBounds
                    : default;
                _edgeRoutes.Add(new EdgeRoute(parent, _currentEdgeRouteBounds, clipBounds, segmentStart, segmentCount));
            }
        }
        finally
        {
            _isBuildingEdgeRoutes = false;
            _currentEdgeRouteHasSegments = false;
            _currentEdgeRouteBounds = default;
        }
    }

    private void AddEdgeRouteSegment(Point start, Point end, bool isRail)
    {
        if (Math.Abs(start.X - end.X) <= 0.05 && Math.Abs(start.Y - end.Y) <= 0.05)
        {
            return;
        }

        _edgeRouteSegments.Add(new EdgeSegment(start, end, isRail));

        var left = Math.Min(start.X, end.X);
        var top = Math.Min(start.Y, end.Y);
        var right = Math.Max(start.X, end.X);
        var bottom = Math.Max(start.Y, end.Y);
        var segmentBounds = new Rect(left, top, Math.Max(0.5, right - left), Math.Max(0.5, bottom - top));

        if (!_currentEdgeRouteHasSegments)
        {
            _currentEdgeRouteBounds = segmentBounds;
            _currentEdgeRouteHasSegments = true;
            return;
        }

        var routeLeft = Math.Min(_currentEdgeRouteBounds.Left, segmentBounds.Left);
        var routeTop = Math.Min(_currentEdgeRouteBounds.Top, segmentBounds.Top);
        var routeRight = Math.Max(_currentEdgeRouteBounds.Right, segmentBounds.Right);
        var routeBottom = Math.Max(_currentEdgeRouteBounds.Bottom, segmentBounds.Bottom);
        _currentEdgeRouteBounds = new Rect(
            routeLeft,
            routeTop,
            Math.Max(0.5, routeRight - routeLeft),
            Math.Max(0.5, routeBottom - routeTop));
    }

    private void DrawClippedFanout(
        DrawingContext context,
        GraphNode parent,
        IReadOnlyList<GraphNode> children,
        Pen railPen,
        Pen branchPen)
    {
        if (parent.RegionBounds.Width <= 0 || parent.RegionBounds.Height <= 0)
        {
            DrawFanout(context, parent, children, railPen, branchPen);
            return;
        }

        using (context.PushClip(ToScreen(parent.RegionBounds)))
        {
            DrawFanout(context, parent, children, railPen, branchPen);
        }
    }

    private void DrawFanout(
        DrawingContext context,
        GraphNode parent,
        IReadOnlyList<GraphNode> children,
        Pen railPen,
        Pen branchPen)
    {
        if (children.Count == 0)
        {
            return;
        }

        if (parent.Parent is null)
        {
            DrawRootFanout(context, parent, children, railPen, branchPen);
            return;
        }

        DrawFanoutRows(context, parent, children, railPen, branchPen, new Point(parent.Bounds.Center.X, parent.Bounds.Bottom));
    }

    private void DrawRootFanout(
        DrawingContext context,
        GraphNode parent,
        IReadOnlyList<GraphNode> children,
        Pen railPen,
        Pen branchPen)
    {
        var upperChildren = children
            .Where(child => child.Bounds.Top < parent.Bounds.Top)
            .ToArray();
        var lowerChildren = children
            .Where(child => child.Bounds.Top >= parent.Bounds.Bottom)
            .ToArray();

        if (upperChildren.Length > 0)
        {
            DrawRootFileCapConnector(context, parent, upperChildren, railPen);
        }

        if (lowerChildren.Length > 0)
        {
            DrawFanoutRows(context, parent, lowerChildren, railPen, branchPen, new Point(parent.Bounds.Center.X, parent.Bounds.Bottom));
        }
    }

    private void DrawRootFileCapConnector(
        DrawingContext context,
        GraphNode parent,
        IReadOnlyList<GraphNode> upperChildren,
        Pen railPen)
    {
        var blockBottom = upperChildren[0].Bounds.Bottom;
        for (var index = 1; index < upperChildren.Count; index++)
        {
            blockBottom = Math.Max(blockBottom, upperChildren[index].Bounds.Bottom);
        }

        var x = parent.Bounds.Center.X;
        DrawWorldLine(context, railPen, new Point(x, parent.Bounds.Top), new Point(x, blockBottom));
    }

    private void DrawFanoutRows(
        DrawingContext context,
        GraphNode parent,
        IReadOnlyList<GraphNode> children,
        Pen railPen,
        Pen branchPen,
        Point parentAnchor)
    {
        var rows = BuildChildRouteRows(children);
        if (rows.Count == 0)
        {
            return;
        }

        if (rows.Count == 1)
        {
            DrawChildRouteRow(context, rows[0], railPen, branchPen, parentAnchor.X, parentAnchor, connectParent: true);
            return;
        }

        var trunkX = GetChildRouteTrunkX(parent);
        var firstBusY = rows[0].BusY;
        DrawWorldLine(context, railPen, parentAnchor, new Point(parentAnchor.X, firstBusY));
        DrawWorldLine(context, railPen, new Point(parentAnchor.X, firstBusY), new Point(trunkX, firstBusY));

        var previousBusY = firstBusY;
        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            if (rowIndex > 0)
            {
                DrawWorldLine(context, railPen, new Point(trunkX, previousBusY), new Point(trunkX, row.BusY));
            }

            DrawChildRouteRow(context, row, railPen, branchPen, trunkX, parentAnchor, connectParent: false);
            previousBusY = row.BusY;
        }
    }

    private void DrawChildRouteRow(
        DrawingContext context,
        ChildRouteRow row,
        Pen railPen,
        Pen branchPen,
        double entryX,
        Point parentAnchor,
        bool connectParent)
    {
        if (connectParent)
        {
            DrawWorldLine(context, railPen, parentAnchor, new Point(parentAnchor.X, row.BusY));
        }

        var railStart = Math.Min(entryX, row.Left);
        var railEnd = Math.Max(entryX, row.Right);
        DrawWorldLine(context, railPen, new Point(railStart, row.BusY), new Point(railEnd, row.BusY));

        foreach (var child in row.Children)
        {
            var childAnchor = new Point(child.Bounds.Center.X, child.Bounds.Top);
            DrawWorldLine(context, branchPen, new Point(childAnchor.X, row.BusY), childAnchor);
        }
    }

    private static List<ChildRouteRow> BuildChildRouteRows(IReadOnlyList<GraphNode> children)
    {
        var ordered = new List<GraphNode>(children);
        ordered.Sort(static (left, right) =>
        {
            var topCompare = left.Bounds.Top.CompareTo(right.Bounds.Top);
            return topCompare != 0
                ? topCompare
                : left.Bounds.Center.X.CompareTo(right.Bounds.Center.X);
        });

        var rows = new List<ChildRouteRow>();
        ChildRouteRow? current = null;
        foreach (var child in ordered)
        {
            if (current is null || Math.Abs(child.Bounds.Top - current.Top) > 1.0)
            {
                current = new ChildRouteRow(child.Bounds.Top);
                rows.Add(current);
            }

            current.Add(child);
        }

        return rows;
    }

    private static double GetChildRouteTrunkX(GraphNode parent)
    {
        if (parent.RegionBounds.Width > 0)
        {
            return parent.RegionBounds.Left + Math.Min(4.0, Math.Max(2.0, TreeRegionPadding * 0.5));
        }

        return parent.Bounds.Left - 4.0;
    }

    private void DrawWorldLine(DrawingContext context, Pen pen, Point start, Point end)
    {
        if (_isBuildingEdgeRoutes)
        {
            AddEdgeRouteSegment(start, end, ReferenceEquals(pen, EdgeRouteRailPen));
            return;
        }

        context.DrawLine(pen, ToScreen(start), ToScreen(end));
    }

    private void DrawNodes(DrawingContext context, Rect worldViewport, RenderResources resources)
    {
        foreach (var node in _nodes)
        {
            if (!RectMayIntersectWorld(node.Bounds, worldViewport))
            {
                continue;
            }

            var rect = ToScreen(node.Bounds);
            DrawNode(context, node, rect, resources);
        }
    }

    private void DrawNode(
        DrawingContext context,
        GraphNode node,
        Rect rect,
        RenderResources resources)
    {
        var isSelected = IsSelectedNode(node);
        var isHovered = ReferenceEquals(_hoveredNode, node);
        var isExternal = node.Node?.IsExternal == true;
        var background = NodeBackground(node, isSelected, isHovered, resources);
        var borderBrush = isSelected
            ? resources.AccentBorder
            : isExternal
                ? resources.ExternalText
                : node.Node?.IsFolder == true
                    ? FolderNodeBorderBrush(node, resources)
                    : resources.PanelBorder;
        var borderWidth = isSelected || isExternal
            ? 1.35
            : node.Node?.IsFolder == true
                ? 1.22
                : 1.0;

        context.DrawRectangle(
            background,
            CachedPen(borderBrush, Math.Clamp(borderWidth * _zoom, 0.75, 1.6)),
            rect,
            Math.Clamp(3 * _zoom, 0.75, 3),
            Math.Clamp(3 * _zoom, 0.75, 3));

        if (node.IsPinned && _zoom > 0.35)
        {
            var pinRadius = Math.Clamp(2.4 * _zoom, 1.2, 3.4);
            context.DrawEllipse(
                resources.Accent,
                null,
                new Point(rect.Right - 8 * _zoom, rect.Top + 8 * _zoom),
                pinRadius,
                pinRadius);
        }

        if (_zoom < 0.18 || rect.Width < 16 || rect.Height < 10)
        {
            return;
        }

        var padX = Math.Clamp(8 * _zoom, 3, 8);
        var padTop = Math.Clamp(4 * _zoom, 2, 4);
        var padBottom = Math.Clamp(3 * _zoom, 1.5, 3);
        var textWidth = Math.Max(0, rect.Width - 2 * padX);
        var titleY = rect.Y + padTop;

        FormattedText? metaText = null;
        var metaY = 0.0;
        if (_zoom >= 0.46 && !node.IsAggregate && node.Node is not null)
        {
            var meta = BuildNodeMeta(node.Node);
            if (!string.IsNullOrWhiteSpace(meta))
            {
                var metaBrush = node.Node.IsExternal
                    ? resources.ExternalText
                    : node.Node.IsFolder
                        ? resources.MetricFile
                        : resources.MetricLoc;
                metaText = GetFormattedText(meta, metaBrush, resources.CodeFont, resources.CodeFontKey, FontWeight.Black, FontStyle.Normal, Math.Clamp(7.7 * _zoom, 4.0, 8.2));
                metaY = rect.Bottom - padBottom - metaText.Height;
            }
        }

        var titleBrush = NodeTextBrush(node, resources);
        var titleWeight = node.Node?.IsFolder == true || node.IsAggregate ? FontWeight.ExtraBold : FontWeight.SemiBold;
        var titleStyle = isExternal ? FontStyle.Italic : FontStyle.Normal;
        var titleFont = node.IsAggregate ? resources.CodeFont : resources.UiFont;
        var titleFontKey = node.IsAggregate ? resources.CodeFontKey : resources.UiFontKey;
        var titleSize = Math.Clamp((node.Depth == 0 ? 11.5 : 10.0) * _zoom, 4.5, 12.0);
        var title = GetFormattedText(node.Title, titleBrush, titleFont, titleFontKey, titleWeight, titleStyle, titleSize);
        var titleBottom = metaText is null
            ? rect.Bottom - padBottom
            : Math.Max(titleY + 1, metaY - Math.Clamp(2 * _zoom, 0.8, 2));
        var titleClip = new Rect(rect.X + padX, titleY, textWidth, Math.Max(0, titleBottom - titleY));
        DrawClippedText(context, title, titleClip, new Point(titleClip.X, titleClip.Y));

        if (metaText is null || metaY <= titleY)
        {
            return;
        }

        var metaClip = new Rect(rect.X + padX, metaY, textWidth, Math.Max(0, rect.Bottom - padBottom - metaY));
        DrawClippedText(context, metaText, metaClip, new Point(metaClip.X, metaClip.Y));
    }

    private IBrush NodeBackground(GraphNode node, bool isSelected, bool isHovered, RenderResources resources)
    {
        if (isSelected)
        {
            return resources.DropdownSelected;
        }

        if (isHovered)
        {
            return resources.HistoryActive;
        }

        if (node.IsAggregate)
        {
            return resources.CommandBackground;
        }

        if (node.Node?.IsFolder == true)
        {
            return node.Children.Count == 0
                ? EmptyFolderBackgroundBrush(resources)
                : FolderNodeBackgroundBrush(node, resources);
        }

        return resources.CommandBackground;
    }

    private IBrush EmptyFolderBackgroundBrush(RenderResources resources)
    {
        return SolidBrush(resources.IsDark
            ? EmptyFolderDarkColor
            : EmptyFolderLightColor);
    }

    private IBrush FolderNodeBackgroundBrush(GraphNode node, RenderResources resources)
    {
        var palette = resources.IsDark ? DarkRegionPalette : LightRegionPalette;
        var color = palette[RegionPaletteIndex(node, palette.Length)];
        return SolidBrush(ScaleColor(color, resources.IsDark ? 0.48 : 0.68));
    }

    private IBrush FolderNodeBorderBrush(GraphNode node, RenderResources resources)
    {
        if (node.Children.Count == 0)
        {
            return SolidBrush(resources.IsDark
                ? EmptyFolderBorderDarkColor
                : EmptyFolderBorderLightColor);
        }

        var palette = resources.IsDark ? LightRegionPalette : DarkRegionPalette;
        var color = palette[RegionPaletteIndex(node, palette.Length)];
        return SolidBrush(ScaleColor(color, resources.IsDark ? 0.82 : 0.72));
    }

    private IBrush RegionFillBrush(GraphNode node, RenderResources resources)
    {
        var palette = resources.IsDark ? DarkRegionPalette : LightRegionPalette;
        return SolidBrush(palette[RegionPaletteIndex(node, palette.Length)]);
    }

    private IBrush RegionBorderBrush(GraphNode node, RenderResources resources)
    {
        var palette = resources.IsDark ? LightRegionPalette : DarkRegionPalette;
        return SolidBrush(palette[RegionPaletteIndex(node, palette.Length)]);
    }

    private IBrush SolidBrush(Color color)
    {
        var key = ((uint)color.A << 24)
            | ((uint)color.R << 16)
            | ((uint)color.G << 8)
            | color.B;
        if (_solidBrushCache.TryGetValue(key, out var brush))
        {
            return brush;
        }

        brush = new SolidColorBrush(color);
        _solidBrushCache[key] = brush;
        return brush;
    }

    private Pen CachedPen(IBrush brush, double thickness)
    {
        var key = new PenCacheKey(RuntimeHelpers.GetHashCode(brush), Math.Round(thickness * 64.0) / 64.0);
        if (_penCache.TryGetValue(key, out var pen))
        {
            return pen;
        }

        if (_penCache.Count > MaxPenCacheEntries)
        {
            _penCache.Clear();
        }

        pen = new Pen(brush, key.Thickness);
        _penCache[key] = pen;
        return pen;
    }

    private static int RegionPaletteIndex(GraphNode node, int paletteLength)
    {
        return Math.Abs(node.Depth) % paletteLength;
    }

    private static Color ScaleColor(Color color, double scale)
    {
        return Color.FromArgb(
            color.A,
            (byte)Math.Clamp((int)Math.Round(color.R * scale), 0, 255),
            (byte)Math.Clamp((int)Math.Round(color.G * scale), 0, 255),
            (byte)Math.Clamp((int)Math.Round(color.B * scale), 0, 255));
    }

    private IBrush NodeTextBrush(GraphNode node, RenderResources resources)
    {
        if (node.IsAggregate)
        {
            return resources.TextMuted;
        }

        if (node.Node?.IsExternal == true)
        {
            return resources.ExternalText;
        }

        return node.Node?.IsFolder == true
            ? resources.FolderText
            : resources.FileText;
    }

    private static string BuildNodeMeta(ProjectNodeViewModel node)
    {
        if (node.IsExternal)
        {
            return "skip";
        }

        if (node.IsFolder)
        {
            return string.IsNullOrWhiteSpace(node.DirectoryStatsLabel)
                ? node.FileCountLabel
                : node.DirectoryStatsLabel;
        }

        return string.IsNullOrWhiteSpace(node.LocMetricLabel)
            ? node.VersionLabel
            : $"{node.VersionLabel}  {node.LocMetricLabel}";
    }

    private GraphNode? HitTestGraphNode(Point screenPoint)
    {
        if (_layoutDirty)
        {
            EnsureLayout();
        }

        var world = ScreenToWorld(screenPoint);
        for (var index = _nodes.Count - 1; index >= 0; index--)
        {
            var node = _nodes[index];
            if (node.Bounds.Contains(world))
            {
                return node;
            }
        }

        return null;
    }

    private GraphNode? FindGraphNode(ProjectNodeViewModel node)
    {
        foreach (var graphNode in _nodes)
        {
            if (ReferenceEquals(graphNode.Node, node))
            {
                return graphNode;
            }
        }

        return null;
    }

    private bool IsSelectedNode(GraphNode node)
    {
        return node.Node is not null && ReferenceEquals(node.Node, SelectedNode);
    }

    private double TreeAspect
    {
        get
        {
            var viewportAspect = Bounds.Width > 1 && Bounds.Height > 1
                ? Bounds.Width / Bounds.Height
                : 1.75;
            return Math.Clamp(viewportAspect * 0.95, 1.05, 2.35);
        }
    }

    private Point ScreenToWorld(Point screen)
    {
        return new Point((screen.X - _pan.X) / _zoom, (screen.Y - _pan.Y) / _zoom);
    }

    private Rect ScreenToWorldViewport(Rect screen, double margin)
    {
        var left = (screen.Left - margin - _pan.X) / _zoom;
        var top = (screen.Top - margin - _pan.Y) / _zoom;
        var right = (screen.Right + margin - _pan.X) / _zoom;
        var bottom = (screen.Bottom + margin - _pan.Y) / _zoom;
        return new Rect(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top));
    }

    private Point ToScreen(Point world)
    {
        return new Point(world.X * _zoom + _pan.X, world.Y * _zoom + _pan.Y);
    }

    private Rect ToScreen(Rect world)
    {
        return new Rect(
            world.X * _zoom + _pan.X,
            world.Y * _zoom + _pan.Y,
            world.Width * _zoom,
            world.Height * _zoom);
    }

    private void ZoomAt(Point screenPoint, double factor)
    {
        var world = ScreenToWorld(screenPoint);
        var nextZoom = Math.Clamp(_zoom * factor, MinZoom, MaxZoom);
        _zoom = nextZoom;
        _pan = new Vector(screenPoint.X - world.X * _zoom, screenPoint.Y - world.Y * _zoom);
    }

    private static bool RectMayIntersectWorld(Rect rect, Rect worldViewport)
    {
        return rect.Right >= worldViewport.Left
            && rect.Left <= worldViewport.Right
            && rect.Bottom >= worldViewport.Top
            && rect.Top <= worldViewport.Bottom;
    }

    private static bool SegmentMayIntersectWorld(Point start, Point end, Rect worldViewport)
    {
        var left = Math.Min(start.X, end.X);
        var right = Math.Max(start.X, end.X);
        var top = Math.Min(start.Y, end.Y);
        var bottom = Math.Max(start.Y, end.Y);
        return right >= worldViewport.Left
            && left <= worldViewport.Right
            && bottom >= worldViewport.Top
            && top <= worldViewport.Bottom;
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

    private FormattedText GetFormattedText(
        string text,
        IBrush brush,
        FontFamily fontFamily,
        string fontKey,
        FontWeight weight,
        FontStyle style,
        double fontSize)
    {
        fontSize = Math.Round(fontSize * 16.0) / 16.0;
        var key = new TextCacheKey(
            text,
            RuntimeHelpers.GetHashCode(brush),
            fontKey,
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

    private RenderResources ResolveRenderResources()
    {
        var uiFont = ResolveFontFamily(UiFontFamily, Resource("UiFontFamily", DefaultUiFontFamily));
        var codeFont = ResolveFontFamily(CodeFontFamily, Resource("CodeFontFamily", DefaultCodeFontFamily));
        return new RenderResources(
            Resource("EditorSurfaceBrush", EditorSurfaceFallbackBrush),
            Resource("PanelBorderBrush", PanelBorderFallbackBrush),
            Resource("CommandBackgroundBrush", CommandBackgroundFallbackBrush),
            Resource("HistoryActiveBrush", HistoryActiveFallbackBrush),
            Resource("DropdownSelectedBrush", DropdownSelectedFallbackBrush),
            Resource("TextMutedBrush", TextMutedFallbackBrush),
            Resource("FolderTextBrush", FolderTextFallbackBrush),
            Resource("FileTextBrush", FileTextFallbackBrush),
            Resource("ExternalTextBrush", ExternalTextFallbackBrush),
            Resource("AccentBrush", AccentFallbackBrush),
            Resource("AccentBorderBrush", AccentBorderFallbackBrush),
            Resource("MetricFileBrush", MetricFileFallbackBrush),
            Resource("MetricLocBrush", MetricLocFallbackBrush),
            uiFont,
            uiFont.ToString(),
            codeFont,
            codeFont.ToString(),
            IsDarkTheme(ThemeKey));
    }

    private T Resource<T>(string key, T fallback)
    {
        for (var control = this as Control; control is not null; control = control.Parent as Control)
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
            || string.Equals(themeKey, "contrast", StringComparison.OrdinalIgnoreCase)
            || string.Equals(themeKey, "matrix", StringComparison.OrdinalIgnoreCase);
    }

    private readonly record struct GraphPackItem(GraphNode Node, Point Offset);

    private readonly record struct SizedGraphNode(GraphNode Node, Size Size);

    private readonly record struct EdgeRoute(
        GraphNode Parent,
        Rect Bounds,
        Rect ClipBounds,
        int SegmentStart,
        int SegmentCount);

    private readonly record struct EdgeSegment(Point Start, Point End, bool IsRail);

    private readonly record struct RenderResources(
        IBrush EditorSurface,
        IBrush PanelBorder,
        IBrush CommandBackground,
        IBrush HistoryActive,
        IBrush DropdownSelected,
        IBrush TextMuted,
        IBrush FolderText,
        IBrush FileText,
        IBrush ExternalText,
        IBrush Accent,
        IBrush AccentBorder,
        IBrush MetricFile,
        IBrush MetricLoc,
        FontFamily UiFont,
        string UiFontKey,
        FontFamily CodeFont,
        string CodeFontKey,
        bool IsDark);

    private sealed class GraphPackPlan(IReadOnlyList<GraphPackItem> items, double width, double height)
    {
        public IReadOnlyList<GraphPackItem> Items { get; } = items;
        public double Width { get; } = width;
        public double Height { get; } = height;
    }

    private sealed class ChildRouteRow(double top)
    {
        public double Top { get; } = top;
        public double BusY { get; private set; }
        public double Left { get; private set; } = double.MaxValue;
        public double Right { get; private set; } = double.MinValue;
        public List<GraphNode> Children { get; } = [];

        public void Add(GraphNode child)
        {
            Children.Add(child);
            Left = Math.Min(Left, child.Bounds.Center.X);
            Right = Math.Max(Right, child.Bounds.Center.X);
            BusY = Top - 5.0;
        }
    }

    private readonly record struct GraphEdge(GraphNode Parent, GraphNode Child);

    private readonly record struct CountResult(int Count, bool Capped);

    private readonly record struct TextCacheKey(
        string Text,
        int BrushId,
        string FontFamily,
        FontWeight Weight,
        FontStyle Style,
        double FontSize);

    private readonly record struct PenCacheKey(int BrushId, double Thickness);

    private sealed class GraphNode
    {
        public GraphNode(ProjectNodeViewModel node, GraphNode? parent, int depth)
        {
            Node = node;
            Parent = parent;
            Depth = depth;
            Key = BuildNodeKey(node, parent);
            Title = node.Depth == 0 && node.IsFolder ? node.Name : node.DisplayName;
        }

        private GraphNode(GraphNode parent, int depth, int omittedCount, bool omittedCapped)
        {
            Parent = parent;
            Depth = depth;
            Key = $"{parent.Key}|more|{parent.Children.Count}";
            IsAggregate = true;
            Title = omittedCapped
                ? $"+{omittedCount:N0}+ more"
                : $"+{omittedCount:N0} more";
        }

        public ProjectNodeViewModel? Node { get; }
        public GraphNode? Parent { get; }
        public List<GraphNode> Children { get; } = [];
        public int Depth { get; }
        public string Key { get; }
        public string Title { get; }
        public bool IsAggregate { get; }
        public bool IsPinned { get; set; }
        public double Weight { get; set; }
        public Rect Bounds { get; set; }
        public Rect RegionBounds { get; set; }
        public Size TreeSize { get; set; }
        public GraphPackPlan? TreePlan { get; set; }
        public double RootLabelOffsetY { get; set; }

        public static GraphNode CreateAggregate(GraphNode parent, int depth, int omittedCount, bool omittedCapped)
        {
            return new GraphNode(parent, depth, omittedCount, omittedCapped);
        }

        private static string BuildNodeKey(ProjectNodeViewModel node, GraphNode? parent)
        {
            if (!string.IsNullOrWhiteSpace(node.Path))
            {
                return $"{RootKey(parent)}|{node.Path}";
            }

            return $"{RootKey(parent)}|{node.Name}";
        }

        private static string RootKey(GraphNode? parent)
        {
            while (parent?.Parent is not null)
            {
                parent = parent.Parent;
            }

            return parent?.Node?.Name ?? "root";
        }
    }
}
