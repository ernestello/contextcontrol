using System.Globalization;
using System.Text;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TextMateSharp.Grammars;
using TextMateSharp.Registry;

namespace ContextControl.Workbench.Controls;

public sealed partial class CodeEditor : UserControl
{
    private const double EditorTopPadding = 10;
    private const double EditorBottomPadding = 14;
    private const double EditorLineHeight = 16;
    private const double MinimapWidth = 46;
    private const double ScrollbarReserve = 12;
    private const double MinimapIdleOpacity = 0.76;
    private const double MinimapHoverOpacity = 0.96;
    private const double BottomAnchorTolerance = 2;
    private const string AllSummaryFoldKinds = "namespace,class,struct,interface,enum,method,property,object,block,array,arguments";
    private const string MatrixConsoleSkinKey = "matrix-console";
    private static readonly string[] CommonGrammarExtensions = [".cs", ".xaml", ".xml", ".json", ".ps1", ".md", ".js", ".ts", ".css", ".html"];
    private static readonly object GrammarLock = new();
    private static readonly RegistryOptions SharedRegistryOptions = new(ThemeName.LightPlus);
    private static readonly Registry SharedRegistry = new(SharedRegistryOptions);
    private static readonly Dictionary<string, IGrammar?> SharedGrammarByExtension = new(StringComparer.OrdinalIgnoreCase);
    private static int _grammarPrewarmQueued;

    public static readonly StyledProperty<string> DocumentTextProperty =
        AvaloniaProperty.Register<CodeEditor, string>(nameof(DocumentText), "");

    public static readonly StyledProperty<string> DocumentPathProperty =
        AvaloniaProperty.Register<CodeEditor, string>(nameof(DocumentPath), "");

    public static readonly StyledProperty<IReadOnlyDictionary<int, string>?> LineChangesProperty =
        AvaloniaProperty.Register<CodeEditor, IReadOnlyDictionary<int, string>?>(nameof(LineChanges));

    public static readonly StyledProperty<string> ThemeKeyProperty =
        AvaloniaProperty.Register<CodeEditor, string>(nameof(ThemeKey), "empty");

    public static readonly StyledProperty<string> SyntaxThemeKeyProperty =
        AvaloniaProperty.Register<CodeEditor, string>(nameof(SyntaxThemeKey), "adaptive");

    public static readonly StyledProperty<string> CodeFontFamilyProperty =
        AvaloniaProperty.Register<CodeEditor, string>(nameof(CodeFontFamily), "avares://ContextControl.Workbench/Assets/Fonts#Cascadia Code, Consolas");

    public static readonly StyledProperty<string> SkinKeyProperty =
        AvaloniaProperty.Register<CodeEditor, string>(nameof(SkinKey), "default");

    public static readonly StyledProperty<bool> ShowMinimapProperty =
        AvaloniaProperty.Register<CodeEditor, bool>(nameof(ShowMinimap), true);

    public static readonly StyledProperty<bool> ShowFoldArrowsProperty =
        AvaloniaProperty.Register<CodeEditor, bool>(nameof(ShowFoldArrows), true);

    public static readonly StyledProperty<bool> ShowSummaryArrowBordersProperty =
        AvaloniaProperty.Register<CodeEditor, bool>(nameof(ShowSummaryArrowBorders), true);

    public static readonly StyledProperty<bool> FoldArrowsInCodeEditorProperty =
        AvaloniaProperty.Register<CodeEditor, bool>(nameof(FoldArrowsInCodeEditor), true);

    public static readonly StyledProperty<bool> UseParentChildArrowIndentationProperty =
        AvaloniaProperty.Register<CodeEditor, bool>(nameof(UseParentChildArrowIndentation), true);

    public static readonly StyledProperty<bool> ShowVerticalScopeLinesProperty =
        AvaloniaProperty.Register<CodeEditor, bool>(nameof(ShowVerticalScopeLines), true);

    public static readonly StyledProperty<string> SummaryFoldKindsProperty =
        AvaloniaProperty.Register<CodeEditor, string>(nameof(SummaryFoldKinds), AllSummaryFoldKinds);

    public static readonly StyledProperty<bool> UseColorfulFamiliesProperty =
        AvaloniaProperty.Register<CodeEditor, bool>(nameof(UseColorfulFamilies), true);

    public static readonly StyledProperty<bool> ShowFoldSummaryPreviewProperty =
        AvaloniaProperty.Register<CodeEditor, bool>(nameof(ShowFoldSummaryPreview), true);

    private readonly CodeTextSurface _surface;
    private readonly CodeMinimap _minimap;
    private readonly ScrollViewer _scroller;
    private readonly Grid _root;
    private readonly DispatcherTimer _skinAnimationTimer;
    private bool _isMinimapNavigating;
    private bool _hasMinimapDragMoved;
    private double _minimapDragStartY;
    private bool _isAnchoringBottomDuringResize;
    private bool _isAttachedToVisualTree;

    public CodeEditor()
    {
        Palette.UseFont(CodeFontFamily);
        Palette.Use(ThemeKey, SyntaxThemeKey);
        _surface = new CodeTextSurface();
        _minimap = new CodeMinimap
        {
            Width = MinimapWidth,
            Opacity = MinimapIdleOpacity,
            IsHitTestVisible = true,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Stretch,
            Margin = new Thickness(0, 0, ScrollbarReserve, ScrollbarReserve)
        };
        _minimap.PointerEntered += (_, _) => _minimap.Opacity = MinimapHoverOpacity;
        _minimap.PointerExited += (_, _) => _minimap.Opacity = MinimapIdleOpacity;
        _minimap.PointerPressed += OnMinimapPointerPressed;
        AddHandler(PointerMovedEvent, OnMinimapPointerMoved, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, OnMinimapPointerReleased, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);

        _skinAnimationTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(92)
        };
        _skinAnimationTimer.Tick += OnSkinAnimationTick;

        _scroller = new ScrollViewer
        {
            Background = Palette.Background,
            Content = _surface,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        _surface.SetScrollHost(_scroller);
        ScrollViewer.SetAllowAutoHide(_scroller, true);
        HoverScrollbarBehavior.SetIsEnabled(_scroller, true);
        HoverScrollbarBehavior.SetReserveRight(_scroller, ScrollbarReserve);
        HoverScrollbarBehavior.SetReserveBottom(_scroller, ScrollbarReserve);
        _scroller.ScrollChanged += OnScrollerScrollChanged;
        _scroller.SizeChanged += OnScrollerSizeChanged;

        _root = new Grid { Background = Palette.Background };
        _root.Children.Add(_scroller);
        _root.Children.Add(_minimap);
        Content = _root;

        ApplyDocument();
        ApplyEditorVisualSettings();
        ApplyChrome();
        ApplyTheme();
        ApplySkin();
        QueueGrammarPrewarm();
    }

    private static void QueueGrammarPrewarm()
    {
        if (Interlocked.Exchange(ref _grammarPrewarmQueued, 1) == 1)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            foreach (var extension in CommonGrammarExtensions)
            {
                _ = GetGrammarForExtension(extension);
            }
        }, DispatcherPriority.Background);
    }

    private static IGrammar? GetGrammarForExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return null;
        }

        lock (GrammarLock)
        {
            if (SharedGrammarByExtension.TryGetValue(extension, out var cached))
            {
                return cached;
            }

            IGrammar? grammar = null;
            try
            {
                var scopeName = SharedRegistryOptions.GetScopeByExtension(extension);
                if (!string.IsNullOrWhiteSpace(scopeName))
                {
                    grammar = SharedRegistry.LoadGrammar(scopeName);
                }
            }
            catch
            {
                grammar = null;
            }

            SharedGrammarByExtension[extension] = grammar;
            return grammar;
        }
    }

    public string DocumentText
    {
        get => GetValue(DocumentTextProperty);
        set => SetValue(DocumentTextProperty, value);
    }

    public string DocumentPath
    {
        get => GetValue(DocumentPathProperty);
        set => SetValue(DocumentPathProperty, value);
    }

    public IReadOnlyDictionary<int, string>? LineChanges
    {
        get => GetValue(LineChangesProperty);
        set => SetValue(LineChangesProperty, value);
    }

    public string ThemeKey
    {
        get => GetValue(ThemeKeyProperty);
        set => SetValue(ThemeKeyProperty, value);
    }

    public string SyntaxThemeKey
    {
        get => GetValue(SyntaxThemeKeyProperty);
        set => SetValue(SyntaxThemeKeyProperty, value);
    }

    public string CodeFontFamily
    {
        get => GetValue(CodeFontFamilyProperty);
        set => SetValue(CodeFontFamilyProperty, value);
    }

    public string SkinKey
    {
        get => GetValue(SkinKeyProperty);
        set => SetValue(SkinKeyProperty, value);
    }

    public bool ShowMinimap
    {
        get => GetValue(ShowMinimapProperty);
        set => SetValue(ShowMinimapProperty, value);
    }

    public bool ShowFoldArrows
    {
        get => GetValue(ShowFoldArrowsProperty);
        set => SetValue(ShowFoldArrowsProperty, value);
    }

    public bool ShowSummaryArrowBorders
    {
        get => GetValue(ShowSummaryArrowBordersProperty);
        set => SetValue(ShowSummaryArrowBordersProperty, value);
    }

    public bool FoldArrowsInCodeEditor
    {
        get => GetValue(FoldArrowsInCodeEditorProperty);
        set => SetValue(FoldArrowsInCodeEditorProperty, value);
    }

    public bool UseParentChildArrowIndentation
    {
        get => GetValue(UseParentChildArrowIndentationProperty);
        set => SetValue(UseParentChildArrowIndentationProperty, value);
    }

    public bool ShowVerticalScopeLines
    {
        get => GetValue(ShowVerticalScopeLinesProperty);
        set => SetValue(ShowVerticalScopeLinesProperty, value);
    }

    public string SummaryFoldKinds
    {
        get => GetValue(SummaryFoldKindsProperty);
        set => SetValue(SummaryFoldKindsProperty, value);
    }

    public bool UseColorfulFamilies
    {
        get => GetValue(UseColorfulFamiliesProperty);
        set => SetValue(UseColorfulFamiliesProperty, value);
    }

    public bool ShowFoldSummaryPreview
    {
        get => GetValue(ShowFoldSummaryPreviewProperty);
        set => SetValue(ShowFoldSummaryPreviewProperty, value);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _isAttachedToVisualTree = true;
        UpdateSkinAnimation();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _isAttachedToVisualTree = false;
        _skinAnimationTimer.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == DocumentTextProperty
            || change.Property == DocumentPathProperty
            || change.Property == LineChangesProperty)
        {
            ApplyDocument();
        }
        else if (change.Property == ThemeKeyProperty
            || change.Property == SyntaxThemeKeyProperty)
        {
            ApplyTheme();
        }
        else if (change.Property == CodeFontFamilyProperty)
        {
            ApplyCodeFont();
        }
        else if (change.Property == SkinKeyProperty)
        {
            ApplySkin();
        }
        else if (change.Property == ShowMinimapProperty)
        {
            ApplyChrome();
        }
        else if (change.Property == ShowFoldArrowsProperty
            || change.Property == ShowSummaryArrowBordersProperty
            || change.Property == FoldArrowsInCodeEditorProperty
            || change.Property == UseParentChildArrowIndentationProperty
            || change.Property == ShowVerticalScopeLinesProperty
            || change.Property == SummaryFoldKindsProperty
            || change.Property == UseColorfulFamiliesProperty
            || change.Property == ShowFoldSummaryPreviewProperty)
        {
            ApplyEditorVisualSettings();
        }
    }

    private void ApplyTheme()
    {
        Palette.Use(ThemeKey, SyntaxThemeKey);
        Background = Palette.Background;
        _root.Background = Palette.Background;
        _scroller.Background = Palette.Background;
        _surface.ApplyTheme();
        _minimap.ApplyTheme();
    }

    private void ApplyCodeFont()
    {
        Palette.UseFont(CodeFontFamily);
        _surface.InvalidateMeasure();
        _surface.InvalidateVisual();
        _minimap.InvalidateVisual();
    }

    private void ApplySkin()
    {
        var normalizedSkin = NormalizeSkinKey(SkinKey);
        _surface.SetSkin(normalizedSkin);
        _minimap.SetSkin(normalizedSkin);
        _root.ClipToBounds = IsMatrixConsoleSkin(normalizedSkin);
        UpdateSkinAnimation();
    }

    private void UpdateSkinAnimation()
    {
        if (_isAttachedToVisualTree && IsMatrixConsoleSkin(SkinKey))
        {
            if (!_skinAnimationTimer.IsEnabled)
            {
                _skinAnimationTimer.Start();
            }

            return;
        }

        _skinAnimationTimer.Stop();
    }

    private void OnSkinAnimationTick(object? sender, EventArgs e)
    {
        var phase = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _surface.SetAnimationPhase(phase);
        _minimap.SetAnimationPhase(phase);
    }

    private void ApplyChrome()
    {
        _minimap.IsVisible = ShowMinimap;
        _minimap.IsHitTestVisible = ShowMinimap;
        _minimap.Margin = new Thickness(0, 0, ShowMinimap ? ScrollbarReserve : 0, ShowMinimap ? ScrollbarReserve : 0);
        HoverScrollbarBehavior.SetReserveRight(_scroller, ShowMinimap ? ScrollbarReserve : 0);
        HoverScrollbarBehavior.SetReserveBottom(_scroller, ShowMinimap ? ScrollbarReserve : 0);
        UpdateMinimapViewport();
    }

    private void ApplyEditorVisualSettings()
    {
        _surface.SetVisualOptions(ShowFoldArrows, ShowSummaryArrowBorders, FoldArrowsInCodeEditor, UseParentChildArrowIndentation, ShowVerticalScopeLines, SummaryFoldKinds, UseColorfulFamilies, ShowFoldSummaryPreview);
    }

    private void ApplyDocument()
    {
        var text = DocumentText ?? "";
        var path = DocumentPath ?? "";
        _surface.SetDocument(text, path, LineChanges);
        _minimap.SetDocument(text, path, LineChanges);
        UpdateMinimapViewport();
    }

    private void UpdateMinimapViewport()
    {
        _surface.SetViewport(_scroller.Offset.Y, _scroller.Viewport.Height);
        _minimap.SetViewport(_scroller.Offset.Y, _scroller.Viewport.Height, _scroller.Extent.Height);
    }

    private static string NormalizeSkinKey(string? skinKey)
    {
        return string.IsNullOrWhiteSpace(skinKey) ? "default" : skinKey.Trim().ToLowerInvariant();
    }

    private static bool IsMatrixConsoleSkin(string? skinKey)
    {
        return string.Equals(NormalizeSkinKey(skinKey), MatrixConsoleSkinKey, StringComparison.OrdinalIgnoreCase);
    }

    private void OnScrollerScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        UpdateMinimapViewport();
    }

    private void OnScrollerSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (_isAnchoringBottomDuringResize)
        {
            UpdateMinimapViewport();
            return;
        }

        if (!e.HeightChanged)
        {
            UpdateMinimapViewport();
            return;
        }

        var extentHeight = Math.Max(0, _scroller.Extent.Height);
        var previousViewportHeight = Math.Max(0, e.PreviousSize.Height);
        var currentViewportHeight = Math.Max(0, _scroller.Viewport.Height);
        var previousMaxOffset = Math.Max(0, extentHeight - previousViewportHeight);
        var currentMaxOffset = Math.Max(0, extentHeight - currentViewportHeight);
        var wasAtBottom = _scroller.Offset.Y >= previousMaxOffset - BottomAnchorTolerance;

        if (wasAtBottom && currentMaxOffset > 0)
        {
            _isAnchoringBottomDuringResize = true;
            _scroller.Offset = new Vector(_scroller.Offset.X, currentMaxOffset);
            _isAnchoringBottomDuringResize = false;
        }

        UpdateMinimapViewport();
    }

    private void OnMinimapPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(_minimap);
        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        var targetOffset = _minimap.GetEditorOffsetForPoint(point.Position);
        ScrollToEditorOffset(targetOffset);
        _isMinimapNavigating = true;
        _hasMinimapDragMoved = false;
        _minimapDragStartY = point.Position.Y;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    private void OnMinimapPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isMinimapNavigating)
        {
            return;
        }

        var point = e.GetCurrentPoint(_minimap);
        if (!_hasMinimapDragMoved && Math.Abs(point.Position.Y - _minimapDragStartY) < 2)
        {
            return;
        }

        _hasMinimapDragMoved = true;
        ScrollToEditorOffset(_minimap.GetEditorOffsetForTrackPoint(point.Position));
        e.Handled = true;
    }

    private void OnMinimapPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isMinimapNavigating)
        {
            return;
        }

        _isMinimapNavigating = false;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void ScrollToEditorOffset(double offset)
    {
        var maxOffset = Math.Max(0, _scroller.Extent.Height - _scroller.Viewport.Height);
        _scroller.Offset = new Vector(_scroller.Offset.X, Math.Clamp(offset, 0, maxOffset));
    }

    private sealed class CodeTextSurface : Control
    {
        private const double TopPadding = EditorTopPadding;
        private const double BottomPadding = EditorBottomPadding;
        private const double MinGutterWidth = 0;
        private const double TextStartPadding = 8;
        private const double LineHeight = EditorLineHeight;
        private const double FontSize = 12;
        private const double LineNumberFontSize = 11;
        private const double CharWidth = 7.15;
        private const double FoldSlotWidth = CharWidth;
        private const double FoldArrowHitRadius = 6;
        private const double FoldButtonSize = 11.5;
        private const double FoldButtonCornerRadius = 3;
        private const double FoldArrowStroke = 1.1;
        private const double FoldArrowHalfWidth = 2.7;
        private const double FoldArrowHalfHeight = 1.75;
        private const double FoldArrowCollapsedHalfWidth = 1.9;
        private const double FoldArrowCollapsedHalfHeight = 2.8;
        private const double LineNumberLeftPadding = CharWidth;
        private const double NumberAreaRightPadding = CharWidth;
        private const double GutterArrowColumnWidth = (FoldArrowHitRadius * 2) + 2;
        private const byte ScopeGuideAlpha = 78;
        private const byte ActiveScopeGuideAlpha = 158;
        private const byte UnifiedScopeGuideAlpha = 124;
        private const byte ActiveUnifiedScopeGuideAlpha = 190;
        private const double ScopeGuideThickness = 1.35;
        private const double ActiveScopeGuideThickness = 1.75;
        private const double ScopeGuideTopInset = 0;
        private const double ScopeGuideBottomInset = 0;
        private const byte ScopeLineFillAlpha = 14;
        private const byte ActiveScopeLineFillAlpha = 26;
        private const double SelectionEdgeScrollZone = 44;
        private const double SelectionMinScrollStep = 6;
        private const double SelectionMaxScrollStep = 56;
        private static readonly IBrush CrtTintBrush = Brush(0, 255, 102, 18);
        private static readonly IBrush CrtScanlineBrush = Brush(0, 0, 0, 72);
        private static readonly IBrush CrtFineScanlineBrush = Brush(0, 255, 102, 22);
        private static readonly IBrush CrtSweepBrush = Brush(183, 255, 210, 44);
        private static readonly IBrush CrtEdgeShadowBrush = Brush(0, 0, 0, 112);
        private static readonly IBrush MatrixGlyphHaloBrush = Brush(0, 255, 102, 120);
        private static readonly IBrush MatrixHotGlyphBrush = Brush(221, 255, 233, 252);
        private static readonly char[] MatrixCodeGlyphs =
        [
            '0', '1', '3', '5', '7', '9', 'Z', ':', '.', '"', '=', '*', '+', '-', '<', '>',
            '\uFF71', '\uFF72', '\uFF73', '\uFF74', '\uFF75', '\uFF76', '\uFF77', '\uFF78',
            '\uFF79', '\uFF7A', '\uFF7B', '\uFF7C', '\uFF7D', '\uFF7E', '\uFF7F', '\uFF80',
            '\uFF81', '\uFF82', '\uFF83', '\uFF84', '\uFF85', '\uFF86', '\uFF87', '\uFF88',
            '\uFF89', '\uFF8A', '\uFF8B', '\uFF8C', '\uFF8D', '\uFF8E', '\uFF8F', '\uFF90',
            '\uFF91', '\uFF92', '\uFF93', '\uFF94', '\uFF95', '\uFF96', '\uFF97', '\uFF98',
            '\uFF99', '\uFF9A', '\uFF9B', '\uFF9C'
        ];

        private readonly DispatcherTimer _selectionAutoScrollTimer;
        private readonly HashSet<int> _collapsedStartLines = [];
        private Dictionary<int, FoldRegion> _foldsByStartLine = [];
        private int[][] _bracketColorIndexes = [];
        private readonly Dictionary<int, List<TokenSpan>> _textMateSpanCache = [];
        private readonly Dictionary<int, RenderSegment[]> _lineSegmentCache = [];
        private readonly Dictionary<int, Pen> _scopeGuidePens = [];
        private readonly Dictionary<int, Pen> _activeScopeGuidePens = [];
        private readonly Dictionary<int, IBrush> _scopeLineFills = [];
        private readonly Dictionary<int, IBrush> _activeScopeLineFills = [];
        private HashSet<string> _summaryFoldKinds = ParseSummaryFoldKinds(AllSummaryFoldKinds);
        private Pen? _unifiedScopeGuidePen;
        private Pen? _activeUnifiedScopeGuidePen;
        private IReadOnlyDictionary<int, string> _lineChanges = new Dictionary<int, string>();
        private string[] _lines = [""];
        private VisibleRow[] _visibleRows = [VisibleRow.ForCodeLine(0)];
        private FoldRegion[] _foldRegions = [];
        private readonly Dictionary<int, FoldRegion[]> _scopesByLineCache = [];
        private string _extension = "";
        private IGrammar? _grammar;
        private int _maxLineLength;
        private double _viewportOffset;
        private double _viewportHeight = 640;
        private int _visibleLineDigits;
        private int _visibleGuideDepth = -1;
        private int _activeLineIndex = -1;
        private bool _showFoldArrows = true;
        private bool _showSummaryArrowBorders = true;
        private bool _foldArrowsInCodeEditor = true;
        private bool _useParentChildArrowIndentation = true;
        private bool _showVerticalScopeLines = true;
        private bool _useColorfulFamilies = true;
        private bool _showFoldSummaryPreview = true;
        private bool _layoutShowFoldArrows = true;
        private bool _layoutFoldArrowsInCodeEditor = true;
        private bool _layoutUseParentChildArrowIndentation = true;
        private bool _layoutShowVerticalScopeLines = true;
        private string _skinKey = "default";
        private long _animationPhase;
        private double _gutterWidth = MinGutterWidth;
        private double _foldColumnStart = MinGutterWidth + TextStartPadding;
        private double _textStart = MinGutterWidth + (TextStartPadding * 2) + FoldSlotWidth;
        private double _guideBaseX = MinGutterWidth + TextStartPadding + (FoldSlotWidth / 2);
        private double _arrowBaseX = MinGutterWidth + TextStartPadding + (FoldSlotWidth / 2);
        private double _lineNumberRight = LineNumberLeftPadding + CharWidth;
        private ScrollViewer? _scrollHost;
        private string _documentText = "";
        private string _lineBreak = "\n";
        private TextPosition? _selectionAnchor;
        private TextPosition? _selectionCaret;
        private bool _isSelecting;
        private bool _hasSelectionViewportPoint;
        private Point _lastSelectionViewportPoint;
        private double _selectionAutoScrollDeltaY;

        public CodeTextSurface()
        {
            Focusable = true;
            _selectionAutoScrollTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(16)
            };
            _selectionAutoScrollTimer.Tick += OnSelectionAutoScrollTick;
        }

        public void SetScrollHost(ScrollViewer scrollHost)
        {
            _scrollHost = scrollHost;
        }

        public void SetDocument(string text, string path, IReadOnlyDictionary<int, string>? lineChanges)
        {
            _documentText = text ?? "";
            _lineBreak = DetectLineBreak(_documentText);
            _extension = Path.GetExtension(path).ToLowerInvariant();
            _lines = NormalizeLines(_documentText);
            _lineChanges = lineChanges ?? new Dictionary<int, string>();
            _collapsedStartLines.Clear();
            ClearSelectionState();
            _textMateSpanCache.Clear();
            _lineSegmentCache.Clear();
            _scopeGuidePens.Clear();
            _activeScopeGuidePens.Clear();
            _scopeLineFills.Clear();
            _activeScopeLineFills.Clear();
            _scopesByLineCache.Clear();
            _activeLineIndex = -1;
            ConfigureGrammar();
            _foldsByStartLine = BuildFoldRegions(_lines);
            _foldRegions = _foldsByStartLine.Values
                .OrderBy(fold => fold.StartLine)
                .ThenByDescending(fold => fold.EndLine)
                .ToArray();
            _bracketColorIndexes = BuildBracketColors(_lines);
            _maxLineLength = _lines.Length == 0 ? 0 : _lines.Max(line => line.Length);
            RebuildVisibleLines();
            InvalidateMeasure();
            InvalidateVisual();
        }

        public void ApplyTheme()
        {
            _textMateSpanCache.Clear();
            _lineSegmentCache.Clear();
            _scopeGuidePens.Clear();
            _activeScopeGuidePens.Clear();
            _scopeLineFills.Clear();
            _activeScopeLineFills.Clear();
            _unifiedScopeGuidePen = null;
            _activeUnifiedScopeGuidePen = null;
            InvalidateVisual();
        }

        public void SetSkin(string? skinKey)
        {
            var normalizedSkin = NormalizeSkinKey(skinKey);
            if (string.Equals(_skinKey, normalizedSkin, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _skinKey = normalizedSkin;
            InvalidateVisual();
        }

        public void SetAnimationPhase(long phase)
        {
            _animationPhase = phase;
            if (IsMatrixConsoleSkin(_skinKey))
            {
                InvalidateVisual();
            }
        }

        public void SetVisualOptions(
            bool showFoldArrows,
            bool showSummaryArrowBorders,
            bool foldArrowsInCodeEditor,
            bool useParentChildArrowIndentation,
            bool showVerticalScopeLines,
            string summaryFoldKinds,
            bool useColorfulFamilies,
            bool showFoldSummaryPreview)
        {
            var normalizedSummaryFoldKinds = ParseSummaryFoldKinds(summaryFoldKinds);
            var layoutMayChange = _showFoldArrows != showFoldArrows
                || _foldArrowsInCodeEditor != foldArrowsInCodeEditor
                || _useParentChildArrowIndentation != useParentChildArrowIndentation
                || _showVerticalScopeLines != showVerticalScopeLines;
            var colorfulFamiliesChanged = _useColorfulFamilies != useColorfulFamilies;
            var showFoldArrowsChanged = _showFoldArrows != showFoldArrows;
            var summaryArrowBordersChanged = _showSummaryArrowBorders != showSummaryArrowBorders;
            var summaryFoldKindsChanged = !_summaryFoldKinds.SetEquals(normalizedSummaryFoldKinds);
            var foldSummaryPreviewChanged = _showFoldSummaryPreview != showFoldSummaryPreview;
            if (!layoutMayChange && !colorfulFamiliesChanged && !summaryArrowBordersChanged && !summaryFoldKindsChanged && !foldSummaryPreviewChanged)
            {
                return;
            }

            _showFoldArrows = showFoldArrows;
            _showSummaryArrowBorders = showSummaryArrowBorders;
            _foldArrowsInCodeEditor = foldArrowsInCodeEditor;
            _useParentChildArrowIndentation = useParentChildArrowIndentation;
            _showVerticalScopeLines = showVerticalScopeLines;
            _summaryFoldKinds = normalizedSummaryFoldKinds;
            _useColorfulFamilies = useColorfulFamilies;
            _showFoldSummaryPreview = showFoldSummaryPreview;

            if (!_useColorfulFamilies)
            {
                _activeLineIndex = -1;
            }

            if (colorfulFamiliesChanged)
            {
                _scopeGuidePens.Clear();
                _activeScopeGuidePens.Clear();
                _scopeLineFills.Clear();
                _activeScopeLineFills.Clear();
                _unifiedScopeGuidePen = null;
                _activeUnifiedScopeGuidePen = null;
            }

            if ((layoutMayChange || summaryFoldKindsChanged || foldSummaryPreviewChanged) && UpdateGutterLayout())
            {
                InvalidateMeasure();
            }

            if ((showFoldArrowsChanged || summaryFoldKindsChanged) && PruneCollapsedSummaryFolds())
            {
                RebuildVisibleLines();
                InvalidateMeasure();
            }
            else if (foldSummaryPreviewChanged)
            {
                RebuildVisibleLines();
            }

            InvalidateVisual();
        }

        public void SetViewport(double offset, double height)
        {
            _viewportOffset = Math.Max(0, offset);
            _viewportHeight = Math.Max(LineHeight, height);
            if (UpdateGutterLayout())
            {
                InvalidateMeasure();
            }

            UpdateSelectionFromViewportPoint();
            InvalidateVisual();
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            var contentWidth = _textStart + (_maxLineLength * CharWidth) + 160;
            var width = double.IsInfinity(availableSize.Width)
                ? contentWidth
                : Math.Max(availableSize.Width, contentWidth);
            var height = TopPadding + (_visibleRows.Length * LineHeight) + BottomPadding;
            return new Size(width, height);
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);

            var viewportTop = Math.Max(0, _viewportOffset - LineHeight);
            var viewportBottom = Math.Min(Bounds.Height, _viewportOffset + _viewportHeight + LineHeight);
            var viewportRect = new Rect(0, viewportTop, Bounds.Width, Math.Max(LineHeight, viewportBottom - viewportTop));
            var useMatrixSkin = IsMatrixConsoleSkin(_skinKey);
            context.DrawRectangle(Palette.Background, null, viewportRect);
            if (useMatrixSkin)
            {
                DrawCrtBackdrop(context, viewportRect);
            }

            context.DrawRectangle(Palette.GutterBackground, null, new Rect(0, viewportTop, _gutterWidth, viewportRect.Height));
            context.DrawLine(new Pen(Palette.GutterRule, 1), new Point(_gutterWidth - 0.5, viewportTop), new Point(_gutterWidth - 0.5, viewportBottom));

            var firstVisibleIndex = Math.Max(0, (int)Math.Floor((_viewportOffset - TopPadding) / LineHeight) - 2);
            var lastVisibleIndex = Math.Min(
                _visibleRows.Length - 1,
                (int)Math.Ceiling((_viewportOffset + _viewportHeight - TopPadding) / LineHeight) + 2);
            var activeScopes = _useColorfulFamilies || _showVerticalScopeLines
                ? BuildActiveScopeSet()
                : new HashSet<int>();

            for (var visibleIndex = firstVisibleIndex; visibleIndex <= lastVisibleIndex; visibleIndex++)
            {
                var row = _visibleRows[visibleIndex];
                var y = TopPadding + (visibleIndex * LineHeight);
                if (row.IsFoldSummary)
                {
                    var summaryScopeLine = GetSummaryScopeProbeLine(row.Fold);
                    if (_useColorfulFamilies)
                    {
                        DrawScopeLineBackground(context, summaryScopeLine, y, activeScopes, treatLineChangeAsTransparent: true);
                    }

                    if (_showVerticalScopeLines)
                    {
                        DrawScopeGuides(context, summaryScopeLine, y, activeScopes);
                    }

                    DrawSelection(context, row, y);
                    DrawLineMarker(context, $"{row.Fold.OpenDelimiter}{row.Fold.CloseDelimiter}", y);
                    if (_showFoldSummaryPreview)
                    {
                        DrawCollapsedSummary(context, row.Fold, y);
                    }

                    continue;
                }

                var lineIndex = row.LineIndex;
                if (_useColorfulFamilies)
                {
                    DrawScopeLineBackground(context, lineIndex, y, activeScopes, treatLineChangeAsTransparent: false);
                }

                if (_showVerticalScopeLines)
                {
                    DrawScopeGuides(context, lineIndex, y, activeScopes);
                }

                DrawLineChangeBackground(context, lineIndex, y);
                DrawSelection(context, row, y);
                DrawLineNumber(context, lineIndex + 1, y);

                if (_showFoldArrows
                    && _foldsByStartLine.TryGetValue(lineIndex, out var fold)
                    && IsSummaryFoldEnabled(fold))
                {
                    DrawFoldArrow(context, y, fold, _collapsedStartLines.Contains(fold.StartLine));
                }

                DrawCodeLine(context, lineIndex, _lines[lineIndex], y);
            }

            if (useMatrixSkin)
            {
                DrawCrtOverlay(context, viewportRect);
            }
        }

        private void DrawCrtBackdrop(DrawingContext context, Rect viewportRect)
        {
            context.DrawRectangle(CrtTintBrush, null, viewportRect);
            var sweepOffset = PositiveModulo(_animationPhase / 22, Math.Max(1, (int)Math.Ceiling(viewportRect.Height)));
            var sweepY = viewportRect.Y + sweepOffset;
            context.DrawRectangle(CrtSweepBrush, null, new Rect(viewportRect.X, sweepY, viewportRect.Width, 2));
        }

        private void DrawCrtOverlay(DrawingContext context, Rect viewportRect)
        {
            var width = Math.Max(0, viewportRect.Width);
            var firstScanline = viewportRect.Y + PositiveModulo(_animationPhase / 48, 4);
            for (var y = firstScanline; y < viewportRect.Bottom; y += 4)
            {
                context.DrawRectangle(CrtScanlineBrush, null, new Rect(viewportRect.X, Math.Floor(y), width, 1));
            }

            var firstFineLine = viewportRect.Y + PositiveModulo(_animationPhase / 85, 11);
            for (var y = firstFineLine; y < viewportRect.Bottom; y += 11)
            {
                context.DrawRectangle(CrtFineScanlineBrush, null, new Rect(viewportRect.X, Math.Floor(y), width, 1));
            }

            var edge = Math.Min(34, Math.Max(12, viewportRect.Height * 0.08));
            context.DrawRectangle(CrtEdgeShadowBrush, null, new Rect(viewportRect.X, viewportRect.Y, width, edge));
            context.DrawRectangle(CrtEdgeShadowBrush, null, new Rect(viewportRect.X, viewportRect.Bottom - edge, width, edge));
            context.DrawRectangle(CrtEdgeShadowBrush, null, new Rect(viewportRect.X, viewportRect.Y, 12, viewportRect.Height));
            context.DrawRectangle(CrtEdgeShadowBrush, null, new Rect(viewportRect.Right - 12, viewportRect.Y, 12, viewportRect.Height));
        }

        private void DrawLineChangeBackground(DrawingContext context, int lineIndex, double y)
        {
            if (!_lineChanges.TryGetValue(lineIndex, out var change))
            {
                return;
            }

            var brush = change == "delete" ? Palette.DeleteLineBackground : Palette.AddLineBackground;
            var stripe = change == "delete" ? Palette.DeleteStripe : Palette.AddStripe;
            context.DrawRectangle(brush, null, new Rect(_gutterWidth, y, Math.Max(0, Bounds.Width - _gutterWidth), LineHeight));
            context.DrawRectangle(stripe, null, new Rect(_gutterWidth, y + 2, 2, LineHeight - 4));
        }

        private HashSet<int> BuildActiveScopeSet()
        {
            var active = new HashSet<int>();
            if (_activeLineIndex < 0)
            {
                return active;
            }

            foreach (var scope in GetScopesForLine(_activeLineIndex))
            {
                active.Add(scope.StartLine);
            }

            return active;
        }

        private void DrawScopeLineBackground(
            DrawingContext context,
            int lineIndex,
            double y,
            HashSet<int> activeScopeStarts,
            bool treatLineChangeAsTransparent)
        {
            if (!treatLineChangeAsTransparent && _lineChanges.ContainsKey(lineIndex))
            {
                return;
            }

            var scopes = GetScopesForLine(lineIndex);
            if (scopes.Count == 0)
            {
                return;
            }

            var lineWidth = Math.Max(0, Bounds.Width - _gutterWidth);
            if (lineWidth <= 0)
            {
                return;
            }

            var right = _gutterWidth + lineWidth;
            if (!_useParentChildArrowIndentation)
            {
                var laneStartX = GetUnifiedScopeGuideX();
                if (laneStartX >= right)
                {
                    return;
                }

                var dominantScope = GetDominantScope(scopes, activeScopeStarts);
                var active = activeScopeStarts.Contains(dominantScope.StartLine);
                context.DrawRectangle(
                    GetScopeLineFill(dominantScope.NestingLevel, active),
                    null,
                    new Rect(laneStartX, y, right - laneStartX, LineHeight));
                return;
            }

            for (var index = 0; index < scopes.Count; index++)
            {
                var scope = scopes[index];
                var laneStartX = GetScopeGuideX(scope);
                if (laneStartX >= right)
                {
                    continue;
                }

                var laneEndX = index + 1 < scopes.Count
                    ? Math.Min(right, GetScopeGuideX(scopes[index + 1]))
                    : right;
                if (laneEndX <= laneStartX)
                {
                    laneEndX = Math.Min(right, laneStartX + FoldSlotWidth);
                }

                var active = activeScopeStarts.Contains(scope.StartLine);
                var brush = GetScopeLineFill(scope.NestingLevel, active);
                context.DrawRectangle(
                    brush,
                    null,
                    new Rect(laneStartX, y, laneEndX - laneStartX, LineHeight));
            }
        }

        private void DrawScopeGuides(DrawingContext context, int lineIndex, double y, HashSet<int> activeScopeStarts)
        {
            var scopes = GetScopesForLine(lineIndex);
            if (scopes.Count == 0)
            {
                return;
            }

            var top = y + ScopeGuideTopInset;
            var bottom = y + LineHeight - ScopeGuideBottomInset;
            if (!_useParentChildArrowIndentation)
            {
                var active = scopes.Any(scope => activeScopeStarts.Contains(scope.StartLine));
                var guideX = GetUnifiedScopeGuideX();
                var guideTop = GetGuideTopAfterFoldButton(lineIndex, top, bottom);
                DrawVerticalGuide(context, GetUnifiedScopeGuidePen(active), guideX, guideTop, bottom);
                return;
            }

            for (var index = 0; index < scopes.Count; index++)
            {
                var scope = scopes[index];
                var active = activeScopeStarts.Contains(scope.StartLine);
                var guideX = GetScopeGuideX(scope);
                var guideTop = scope.StartLine == lineIndex
                    ? GetGuideTopAfterFoldButton(lineIndex, top, bottom)
                    : top;
                DrawVerticalGuide(context, GetScopeGuidePen(scope.NestingLevel, active), guideX, guideTop, bottom);
            }
        }

        private IReadOnlyList<FoldRegion> GetScopesForLine(int lineIndex)
        {
            if (lineIndex < 0 || lineIndex >= _lines.Length || _foldRegions.Length == 0)
            {
                return [];
            }

            if (_scopesByLineCache.TryGetValue(lineIndex, out var cached))
            {
                return cached;
            }

            var scopes = new List<FoldRegion>();
            for (var index = 0; index < _foldRegions.Length; index++)
            {
                var fold = _foldRegions[index];
                if (fold.StartLine > lineIndex)
                {
                    break;
                }

                if (fold.EndLine >= lineIndex)
                {
                    scopes.Add(fold);
                }
            }

            cached = scopes.Count == 0
                ? []
                : scopes
                    .OrderBy(fold => fold.NestingLevel)
                    .ThenBy(fold => fold.StartLine)
                    .ToArray();
            _scopesByLineCache[lineIndex] = cached;
            return cached;
        }

        private static FoldRegion GetDominantScope(IReadOnlyList<FoldRegion> scopes, HashSet<int> activeScopeStarts)
        {
            for (var index = scopes.Count - 1; index >= 0; index--)
            {
                if (activeScopeStarts.Contains(scopes[index].StartLine))
                {
                    return scopes[index];
                }
            }

            return scopes[^1];
        }

        private IBrush GetScopeLineFill(int level, bool active)
        {
            var cache = active ? _activeScopeLineFills : _scopeLineFills;
            if (cache.TryGetValue(level, out var brush))
            {
                return brush;
            }

            brush = ApplyAlpha(Palette.Brackets[level % Palette.Brackets.Length], active ? ActiveScopeLineFillAlpha : ScopeLineFillAlpha);
            cache[level] = brush;
            return brush;
        }

        private static int GetSummaryScopeProbeLine(FoldRegion fold)
        {
            if (fold.EndLine > fold.StartLine)
            {
                return fold.StartLine + 1;
            }

            return fold.StartLine;
        }

        private Pen GetScopeGuidePen(int level, bool active)
        {
            var cache = active ? _activeScopeGuidePens : _scopeGuidePens;
            if (cache.TryGetValue(level, out var pen))
            {
                return pen;
            }

            var baseBrush = _useColorfulFamilies
                ? Palette.Brackets[level % Palette.Brackets.Length]
                : Palette.FoldArrow;
            var brush = ApplyAlpha(baseBrush, active ? ActiveScopeGuideAlpha : ScopeGuideAlpha);
            pen = new Pen(brush, active ? ActiveScopeGuideThickness : ScopeGuideThickness);
            cache[level] = pen;
            return pen;
        }

        private Pen GetUnifiedScopeGuidePen(bool active)
        {
            if (active)
            {
                return _activeUnifiedScopeGuidePen ??= new Pen(
                    ApplyAlpha(Palette.FoldArrow, ActiveUnifiedScopeGuideAlpha),
                    ActiveScopeGuideThickness);
            }

            return _unifiedScopeGuidePen ??= new Pen(
                ApplyAlpha(Palette.FoldArrow, UnifiedScopeGuideAlpha),
                ScopeGuideThickness);
        }

        private double GetUnifiedScopeGuideX()
        {
            return Math.Floor(_guideBaseX) + 0.5;
        }

        private double GetGuideTopAfterFoldButton(int lineIndex, double top, double bottom)
        {
            if (!_showFoldArrows
                || !_foldArrowsInCodeEditor
                || !_foldsByStartLine.TryGetValue(lineIndex, out var fold)
                || !IsSummaryFoldEnabled(fold))
            {
                return top;
            }

            var centerY = top + (LineHeight / 2) + 0.5;
            var buttonBottom = centerY + (FoldButtonSize / 2);
            return Math.Min(bottom, Math.Max(top, buttonBottom));
        }

        private static void DrawVerticalGuide(DrawingContext context, Pen pen, double x, double top, double bottom)
        {
            if (bottom <= top)
            {
                return;
            }

            context.DrawLine(pen, new Point(x, top), new Point(x, bottom));
        }

        private static IBrush ApplyAlpha(IBrush brush, byte alpha)
        {
            if (brush is not ISolidColorBrush solid)
            {
                return brush;
            }

            var color = solid.Color;
            return new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);

            var point = e.GetCurrentPoint(this);
            if (!point.Properties.IsLeftButtonPressed)
            {
                return;
            }

            Focus();
            var position = e.GetPosition(this);
            if (position.Y < TopPadding)
            {
                return;
            }

            var visibleRowIndex = (int)((position.Y - TopPadding) / LineHeight);
            if (visibleRowIndex < 0 || visibleRowIndex >= _visibleRows.Length)
            {
                return;
            }

            var row = _visibleRows[visibleRowIndex];
            if (row.IsFoldSummary)
            {
                return;
            }

            var lineIndex = row.LineIndex;
            SetActiveLine(lineIndex);

            if (_showFoldArrows
                && _foldsByStartLine.TryGetValue(lineIndex, out var fold)
                && IsSummaryFoldEnabled(fold)
                && IsInFoldArrowHitTarget(position, fold))
            {
                ToggleFold(lineIndex);
                e.Handled = true;
                return;
            }

            BeginSelection(position, e.Pointer);
            e.Handled = true;
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            if (_isSelecting)
            {
                UpdateSelection(e.GetPosition(this));
                e.Handled = true;
                return;
            }

            SetActiveLine(_useColorfulFamilies || _showVerticalScopeLines ? GetCodeLineIndexFromPoint(e.GetPosition(this)) : -1);
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            if (!_isSelecting)
            {
                return;
            }

            UpdateSelection(e.GetPosition(this));
            EndSelection(e.Pointer);
            e.Handled = true;
        }

        protected override void OnPointerExited(PointerEventArgs e)
        {
            base.OnPointerExited(e);
            if (!_isSelecting)
            {
                SetActiveLine(-1);
            }
        }

        protected override void OnLostFocus(RoutedEventArgs e)
        {
            base.OnLostFocus(e);
            EndSelection(null);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Handled)
            {
                return;
            }

            if (IsControlShortcut(e, Key.A))
            {
                SelectAll();
                e.Handled = true;
                return;
            }

            if (IsControlShortcut(e, Key.C) || IsControlShortcut(e, Key.Insert))
            {
                _ = CopySelectionAsync();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Escape && HasSelection)
            {
                ClearSelectionState();
                InvalidateVisual();
                e.Handled = true;
            }
        }

        private bool HasSelection =>
            _selectionAnchor is { } anchor
            && _selectionCaret is { } caret
            && CompareTextPositions(anchor, caret) != 0;

        private bool IsInFoldArrowHitTarget(Point position, FoldRegion fold)
        {
            var arrowCenterX = GetFoldArrowCenterX(fold);
            return position.X >= arrowCenterX - FoldArrowHitRadius
                && position.X <= arrowCenterX + FoldArrowHitRadius;
        }

        private void ToggleFold(int lineIndex)
        {
            if (!_collapsedStartLines.Add(lineIndex))
            {
                _collapsedStartLines.Remove(lineIndex);
            }

            RebuildVisibleLines();
            InvalidateMeasure();
            InvalidateVisual();
        }

        private void BeginSelection(Point position, IPointer pointer)
        {
            var textPosition = GetTextPositionFromPoint(position);
            _selectionAnchor = textPosition;
            _selectionCaret = textPosition;
            _isSelecting = true;
            RecordSelectionViewportPoint(position);
            UpdateSelectionAutoScroll(position);
            pointer.Capture(this);
            InvalidateVisual();
        }

        private void UpdateSelection(Point position, bool updateAutoScroll = true)
        {
            if (!_isSelecting && updateAutoScroll)
            {
                return;
            }

            var textPosition = GetTextPositionFromPoint(position);
            var changed = _selectionCaret != textPosition;
            _selectionCaret = textPosition;
            SetActiveLine(textPosition.Line);
            RecordSelectionViewportPoint(position);

            if (updateAutoScroll)
            {
                UpdateSelectionAutoScroll(position);
            }

            if (changed)
            {
                InvalidateVisual();
            }
        }

        private void EndSelection(IPointer? pointer)
        {
            if (!_isSelecting)
            {
                return;
            }

            _isSelecting = false;
            _hasSelectionViewportPoint = false;
            _selectionAutoScrollTimer.Stop();
            pointer?.Capture(null);
            if (!HasSelection)
            {
                ClearSelectionState();
            }

            InvalidateVisual();
        }

        private void ClearSelectionState()
        {
            _selectionAnchor = null;
            _selectionCaret = null;
            _isSelecting = false;
            _hasSelectionViewportPoint = false;
            _selectionAutoScrollDeltaY = 0;
            _selectionAutoScrollTimer.Stop();
        }

        private void SelectAll()
        {
            var lastLineIndex = Math.Max(0, _lines.Length - 1);
            _selectionAnchor = new TextPosition(0, 0);
            _selectionCaret = new TextPosition(lastLineIndex, SafeLineLength(lastLineIndex));
            _isSelecting = false;
            _hasSelectionViewportPoint = false;
            _selectionAutoScrollTimer.Stop();
            Focus();
            InvalidateVisual();
        }

        private async Task CopySelectionAsync()
        {
            if (!TryGetOrderedSelection(out var start, out var end))
            {
                return;
            }

            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is null)
            {
                return;
            }

            try
            {
                await clipboard.SetTextAsync(GetSelectedText(start, end));
            }
            catch
            {
                // Clipboard can be unavailable if another process has it open.
            }
        }

        private string GetSelectedText(TextPosition start, TextPosition end)
        {
            if (IsEntireDocumentSelected(start, end))
            {
                return _documentText;
            }

            if (start.Line == end.Line)
            {
                var line = GetLine(start.Line);
                var startColumn = Math.Clamp(start.Column, 0, line.Length);
                var endColumn = Math.Clamp(end.Column, startColumn, line.Length);
                return line[startColumn..endColumn];
            }

            var builder = new StringBuilder();
            var firstLine = GetLine(start.Line);
            var firstColumn = Math.Clamp(start.Column, 0, firstLine.Length);
            builder.Append(firstLine[firstColumn..]);
            builder.Append(_lineBreak);

            for (var lineIndex = start.Line + 1; lineIndex < end.Line; lineIndex++)
            {
                builder.Append(GetLine(lineIndex));
                builder.Append(_lineBreak);
            }

            var lastLine = GetLine(end.Line);
            var lastColumn = Math.Clamp(end.Column, 0, lastLine.Length);
            builder.Append(lastLine[..lastColumn]);
            return builder.ToString();
        }

        private bool IsEntireDocumentSelected(TextPosition start, TextPosition end)
        {
            var lastLineIndex = Math.Max(0, _lines.Length - 1);
            return start.Line == 0
                && start.Column == 0
                && end.Line == lastLineIndex
                && end.Column == SafeLineLength(lastLineIndex);
        }

        private bool TryGetOrderedSelection(out TextPosition start, out TextPosition end)
        {
            start = default;
            end = default;
            if (_selectionAnchor is not { } anchor || _selectionCaret is not { } caret)
            {
                return false;
            }

            if (CompareTextPositions(anchor, caret) <= 0)
            {
                start = anchor;
                end = caret;
            }
            else
            {
                start = caret;
                end = anchor;
            }

            return CompareTextPositions(start, end) != 0;
        }

        private TextPosition GetTextPositionFromPoint(Point position)
        {
            if (_visibleRows.Length == 0)
            {
                return new TextPosition(0, 0);
            }

            var visibleRowIndex = (int)Math.Floor((position.Y - TopPadding) / LineHeight);
            visibleRowIndex = Math.Clamp(visibleRowIndex, 0, _visibleRows.Length - 1);
            var row = _visibleRows[visibleRowIndex];
            if (row.IsFoldSummary)
            {
                return GetFoldSummaryTextPosition(row, position);
            }

            var lineIndex = Math.Clamp(row.LineIndex, 0, Math.Max(0, _lines.Length - 1));
            return new TextPosition(lineIndex, GetColumnFromPoint(lineIndex, position.X));
        }

        private TextPosition GetFoldSummaryTextPosition(VisibleRow row, Point position)
        {
            var foldStart = Math.Clamp(row.Fold.StartLine, 0, Math.Max(0, _lines.Length - 1));
            var foldEnd = Math.Clamp(row.Fold.EndLine, foldStart, Math.Max(0, _lines.Length - 1));
            var midpoint = _textStart + (GetSummaryStartColumn(row.Fold) * CharWidth);
            return position.X < midpoint
                ? new TextPosition(foldStart, 0)
                : new TextPosition(foldEnd, SafeLineLength(foldEnd));
        }

        private int GetColumnFromPoint(int lineIndex, double x)
        {
            var lineLength = SafeLineLength(lineIndex);
            if (x <= _textStart)
            {
                return 0;
            }

            var column = (int)Math.Round((x - _textStart) / CharWidth, MidpointRounding.AwayFromZero);
            return Math.Clamp(column, 0, lineLength);
        }

        private void DrawSelection(DrawingContext context, VisibleRow row, double y)
        {
            if (!TryGetOrderedSelection(out var start, out var end))
            {
                return;
            }

            if (row.IsFoldSummary)
            {
                DrawFoldSummarySelection(context, row, y, start, end);
                return;
            }

            DrawLineSelection(context, row.LineIndex, y, start, end);
        }

        private void DrawLineSelection(DrawingContext context, int lineIndex, double y, TextPosition start, TextPosition end)
        {
            if (lineIndex < start.Line || lineIndex > end.Line)
            {
                return;
            }

            var lineLength = SafeLineLength(lineIndex);
            var startColumn = lineIndex == start.Line ? Math.Clamp(start.Column, 0, lineLength) : 0;
            var endColumn = lineIndex == end.Line ? Math.Clamp(end.Column, 0, lineLength) : lineLength + 1;
            var widthColumns = endColumn - startColumn;
            if (widthColumns <= 0)
            {
                return;
            }

            var x = _textStart + (startColumn * CharWidth);
            var availableWidth = Math.Max(0, Bounds.Width - x);
            if (availableWidth <= 0)
            {
                return;
            }

            var width = Math.Min(availableWidth, Math.Max(CharWidth * 0.65, widthColumns * CharWidth));
            context.DrawRectangle(Palette.SelectionBackground, null, new Rect(x, y, width, LineHeight));
        }

        private void DrawFoldSummarySelection(DrawingContext context, VisibleRow row, double y, TextPosition start, TextPosition end)
        {
            var foldStart = new TextPosition(row.Fold.StartLine, 0);
            var foldEnd = new TextPosition(row.Fold.EndLine, SafeLineLength(row.Fold.EndLine));
            if (CompareTextPositions(end, foldStart) <= 0 || CompareTextPositions(start, foldEnd) >= 0)
            {
                return;
            }

            var x = _textStart + (GetSummaryStartColumn(row.Fold) * CharWidth);
            var availableWidth = Math.Max(0, Bounds.Width - x);
            if (availableWidth <= 0)
            {
                return;
            }

            var width = Math.Min(availableWidth, Math.Max(CharWidth, BuildCollapsedSummaryText(row.Fold).Length * CharWidth));
            context.DrawRectangle(Palette.SelectionBackground, null, new Rect(x, y, width, LineHeight));
        }

        private void RecordSelectionViewportPoint(Point contentPoint)
        {
            var offset = _scrollHost?.Offset ?? new Vector(0, _viewportOffset);
            _lastSelectionViewportPoint = new Point(contentPoint.X - offset.X, contentPoint.Y - offset.Y);
            _hasSelectionViewportPoint = true;
        }

        private void UpdateSelectionFromViewportPoint()
        {
            if (!_isSelecting || !_hasSelectionViewportPoint)
            {
                return;
            }

            var offset = _scrollHost?.Offset ?? new Vector(0, _viewportOffset);
            UpdateSelection(new Point(offset.X + _lastSelectionViewportPoint.X, offset.Y + _lastSelectionViewportPoint.Y), updateAutoScroll: false);
        }

        private void UpdateSelectionAutoScroll(Point contentPoint)
        {
            if (_scrollHost is null || _viewportHeight <= 0)
            {
                _selectionAutoScrollTimer.Stop();
                return;
            }

            var viewportY = contentPoint.Y - _scrollHost.Offset.Y;
            var delta = 0.0;
            if (viewportY < SelectionEdgeScrollZone)
            {
                delta = -ComputeSelectionScrollStep(SelectionEdgeScrollZone - viewportY);
            }
            else if (viewportY > _viewportHeight - SelectionEdgeScrollZone)
            {
                delta = ComputeSelectionScrollStep(viewportY - (_viewportHeight - SelectionEdgeScrollZone));
            }

            _selectionAutoScrollDeltaY = delta;
            if (Math.Abs(delta) < 0.1)
            {
                _selectionAutoScrollTimer.Stop();
            }
            else if (!_selectionAutoScrollTimer.IsEnabled)
            {
                _selectionAutoScrollTimer.Start();
            }
        }

        private void OnSelectionAutoScrollTick(object? sender, EventArgs e)
        {
            if (!_isSelecting || _scrollHost is null)
            {
                _selectionAutoScrollTimer.Stop();
                return;
            }

            var maxOffset = Math.Max(0, _scrollHost.Extent.Height - _scrollHost.Viewport.Height);
            var nextY = Math.Clamp(_scrollHost.Offset.Y + _selectionAutoScrollDeltaY, 0, maxOffset);
            if (Math.Abs(nextY - _scrollHost.Offset.Y) >= 0.1)
            {
                _scrollHost.Offset = new Vector(_scrollHost.Offset.X, nextY);
            }

            UpdateSelectionFromViewportPoint();
        }

        private static double ComputeSelectionScrollStep(double distance)
        {
            var ratio = Math.Clamp(distance / SelectionEdgeScrollZone, 0, 4);
            return Math.Clamp(SelectionMinScrollStep + (ratio * 18), SelectionMinScrollStep, SelectionMaxScrollStep);
        }

        private static bool IsControlShortcut(KeyEventArgs e, Key key)
        {
            return e.Key == key
                && (e.KeyModifiers & KeyModifiers.Control) == KeyModifiers.Control
                && (e.KeyModifiers & KeyModifiers.Alt) == 0;
        }

        private string GetLine(int lineIndex)
        {
            if (lineIndex < 0 || lineIndex >= _lines.Length)
            {
                return string.Empty;
            }

            return _lines[lineIndex];
        }

        private int SafeLineLength(int lineIndex)
        {
            return lineIndex >= 0 && lineIndex < _lines.Length ? _lines[lineIndex].Length : 0;
        }

        private static int CompareTextPositions(TextPosition left, TextPosition right)
        {
            var lineComparison = left.Line.CompareTo(right.Line);
            return lineComparison != 0 ? lineComparison : left.Column.CompareTo(right.Column);
        }

        private int GetCodeLineIndexFromPoint(Point position)
        {
            if (position.Y < TopPadding)
            {
                return -1;
            }

            var visibleRowIndex = (int)((position.Y - TopPadding) / LineHeight);
            if (visibleRowIndex < 0 || visibleRowIndex >= _visibleRows.Length)
            {
                return -1;
            }

            var row = _visibleRows[visibleRowIndex];
            return row.IsFoldSummary ? row.Fold.StartLine : row.LineIndex;
        }

        private void SetActiveLine(int lineIndex)
        {
            if (!_useColorfulFamilies && !_showVerticalScopeLines)
            {
                lineIndex = -1;
            }

            if (_activeLineIndex == lineIndex)
            {
                return;
            }

            _activeLineIndex = lineIndex;
            InvalidateVisual();
        }

        private void DrawLineNumber(DrawingContext context, int number, double y)
        {
            var text = number.ToString(CultureInfo.InvariantCulture);
            DrawLineMarker(context, text, y);
        }

        private void DrawLineMarker(DrawingContext context, string text, double y)
        {
            var x = _lineNumberRight - (text.Length * CharWidth);
            DrawText(context, text, Palette.LineNumber, new Point(Math.Max(LineNumberLeftPadding, x), y + 1), LineNumberFontSize);
        }

        private void DrawFoldArrow(DrawingContext context, double y, FoldRegion fold, bool collapsed)
        {
            var arrowCenterX = GetFoldArrowCenterX(fold);
            var centerY = y + (LineHeight / 2) + 0.5;
            var buttonRect = new Rect(
                arrowCenterX - (FoldButtonSize / 2),
                centerY - (FoldButtonSize / 2),
                FoldButtonSize,
                FoldButtonSize);
            var buttonBorder = _showSummaryArrowBorders
                ? new Pen(GetFoldButtonBorderBrush(fold), 1)
                : null;
            context.DrawRectangle(
                null,
                buttonBorder,
                buttonRect,
                FoldButtonCornerRadius,
                FoldButtonCornerRadius);

            var pen = new Pen(Palette.FoldArrow, FoldArrowStroke);

            if (collapsed)
            {
                context.DrawLine(
                    pen,
                    new Point(arrowCenterX - FoldArrowCollapsedHalfWidth, centerY - FoldArrowCollapsedHalfHeight),
                    new Point(arrowCenterX + FoldArrowCollapsedHalfWidth, centerY));
                context.DrawLine(
                    pen,
                    new Point(arrowCenterX + FoldArrowCollapsedHalfWidth, centerY),
                    new Point(arrowCenterX - FoldArrowCollapsedHalfWidth, centerY + FoldArrowCollapsedHalfHeight));
                return;
            }

            context.DrawLine(
                pen,
                new Point(arrowCenterX - FoldArrowHalfWidth, centerY - FoldArrowHalfHeight),
                new Point(arrowCenterX, centerY + FoldArrowHalfHeight));
            context.DrawLine(
                pen,
                new Point(arrowCenterX, centerY + FoldArrowHalfHeight),
                new Point(arrowCenterX + FoldArrowHalfWidth, centerY - FoldArrowHalfHeight));
        }

        private IBrush GetFoldButtonBorderBrush(FoldRegion fold)
        {
            if (!_useColorfulFamilies)
            {
                return Palette.FoldButtonBorder;
            }

            return ApplyAlpha(Palette.Brackets[fold.NestingLevel % Palette.Brackets.Length], 220);
        }

        private void DrawCollapsedSummary(DrawingContext context, FoldRegion fold, double y)
        {
            var summaryStartColumn = GetSummaryStartColumn(fold);
            var summaryStartX = _textStart + Math.Max(0, summaryStartColumn * CharWidth);
            DrawText(context, BuildCollapsedSummaryText(fold), Palette.LineNumber, new Point(summaryStartX, y + 1), LineNumberFontSize);
        }

        private string BuildCollapsedSummaryText(FoldRegion fold)
        {
            var ownerText = fold.StartLine >= 0 && fold.StartLine < _lines.Length
                ? _lines[fold.StartLine].Trim()
                : string.Empty;
            var hiddenLoc = Math.Max(1, fold.EndLine - fold.StartLine);
            var itemCount = TryCountFoldItems(fold);
            var continuationCount = itemCount.HasValue
                ? (itemCount.Value == 1 ? "1 item" : $"{itemCount.Value} items")
                : $"{hiddenLoc} LOC";
            var type = GetFoldSummaryKindKey(fold, ownerText);
            var prefix = BuildCollapsedPrefix(fold, ownerText);
            var terminator = ownerText.EndsWith(';') ? ";" : string.Empty;

            return $"{type}: {prefix} … {continuationCount} {fold.CloseDelimiter}{terminator}";
        }

        private bool IsSummaryFoldEnabled(FoldRegion fold)
        {
            if (!_showFoldArrows)
            {
                return false;
            }

            var ownerText = fold.StartLine >= 0 && fold.StartLine < _lines.Length
                ? _lines[fold.StartLine].Trim()
                : string.Empty;
            return _summaryFoldKinds.Contains(GetFoldSummaryKindKey(fold, ownerText));
        }

        private double GetFoldArrowCenterX(FoldRegion fold)
        {
            if (!_foldArrowsInCodeEditor)
            {
                return _arrowBaseX;
            }

            var levelOffset = _useParentChildArrowIndentation
                ? fold.NestingLevel * FoldSlotWidth
                : 0;
            return _arrowBaseX + levelOffset;
        }

        private double GetScopeGuideX(FoldRegion fold)
        {
            var levelOffset = _useParentChildArrowIndentation
                ? fold.NestingLevel * FoldSlotWidth
                : 0;
            return Math.Floor(_guideBaseX + levelOffset) + 0.5;
        }

        private static string GetFoldSummaryKindKey(FoldRegion fold, string ownerText)
        {
            var normalized = ownerText.ToLowerInvariant();
            if (fold.OpenDelimiter == '[')
            {
                return "array";
            }

            if (fold.OpenDelimiter == '(')
            {
                return "arguments";
            }

            if (normalized.Contains(" class ", StringComparison.Ordinal) || normalized.StartsWith("class ", StringComparison.Ordinal))
            {
                return "class";
            }

            if (normalized.Contains(" struct ", StringComparison.Ordinal) || normalized.StartsWith("struct ", StringComparison.Ordinal))
            {
                return "struct";
            }

            if (normalized.Contains(" interface ", StringComparison.Ordinal) || normalized.StartsWith("interface ", StringComparison.Ordinal))
            {
                return "interface";
            }

            if (normalized.Contains(" enum ", StringComparison.Ordinal) || normalized.StartsWith("enum ", StringComparison.Ordinal))
            {
                return "enum";
            }

            if (normalized.Contains(" namespace ", StringComparison.Ordinal) || normalized.StartsWith("namespace ", StringComparison.Ordinal))
            {
                return "namespace";
            }

            if (normalized.Contains(" get ", StringComparison.Ordinal)
                || normalized.Contains(" set ", StringComparison.Ordinal)
                || normalized.StartsWith("get ", StringComparison.Ordinal)
                || normalized.StartsWith("set ", StringComparison.Ordinal))
            {
                return "property";
            }

            if (ownerText.Contains('(') && ownerText.Contains(')'))
            {
                return "method";
            }

            if (ownerText.Contains('='))
            {
                return "object";
            }

            return "block";
        }

        private static HashSet<string> ParseSummaryFoldKinds(string? value)
        {
            var allowed = AllSummaryFoldKinds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var source = value is null ? AllSummaryFoldKinds : value;
            var selected = source.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(allowed.Contains)
                .Select(item => item.ToLowerInvariant())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            return selected;
        }

        private static string BuildCollapsedPrefix(FoldRegion fold, string ownerText)
        {
            if (string.IsNullOrWhiteSpace(ownerText))
            {
                return fold.OpenDelimiter.ToString();
            }

            var openIndex = ownerText.IndexOf(fold.OpenDelimiter);
            if (openIndex >= 0)
            {
                return CompactWhitespace(TruncateCollapsedPreview(ownerText[..(openIndex + 1)]));
            }

            return CompactWhitespace(TruncateCollapsedPreview($"{ownerText} {fold.OpenDelimiter}"));
        }

        private static string CompactWhitespace(string value)
        {
            return string.Join(" ", value.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries));
        }

        private static string TruncateCollapsedPreview(string value)
        {
            const int maxLength = 74;
            if (value.Length <= maxLength)
            {
                return value;
            }

            var head = value[..50].TrimEnd();
            var tail = value[^18..].TrimStart();
            return $"{head} … {tail}";
        }

        private int GetSummaryStartColumn(FoldRegion fold)
        {
            if (fold.StartLine < 0 || fold.StartLine >= _lines.Length)
            {
                return Math.Max(0, fold.OpenColumn);
            }

            var ownerLine = _lines[fold.StartLine];
            for (var index = 0; index < ownerLine.Length; index++)
            {
                if (!char.IsWhiteSpace(ownerLine[index]))
                {
                    return index;
                }
            }

            return Math.Max(0, fold.OpenColumn);
        }

        private int? TryCountFoldItems(FoldRegion fold)
        {
            if (fold.OpenLine < 0
                || fold.OpenLine >= _lines.Length
                || fold.EndLine < fold.OpenLine
                || fold.OpenColumn < 0
                || fold.CloseColumn < 0)
            {
                return null;
            }

            var commaCount = 0;
            var hasValueToken = false;
            var nestedDepth = 0;
            var inSingleQuote = false;
            var inDoubleQuote = false;
            var isEscaped = false;

            for (var lineIndex = fold.OpenLine; lineIndex <= fold.EndLine; lineIndex++)
            {
                var line = _lines[lineIndex];
                var startColumn = lineIndex == fold.OpenLine
                    ? Math.Min(line.Length, fold.OpenColumn + 1)
                    : 0;
                var endColumnExclusive = lineIndex == fold.EndLine
                    ? Math.Min(line.Length, fold.CloseColumn)
                    : line.Length;

                for (var column = startColumn; column < endColumnExclusive; column++)
                {
                    var current = line[column];

                    if (inSingleQuote || inDoubleQuote)
                    {
                        if (isEscaped)
                        {
                            isEscaped = false;
                            continue;
                        }

                        if (current == '\\')
                        {
                            isEscaped = true;
                            continue;
                        }

                        if (inSingleQuote && current == '\'')
                        {
                            inSingleQuote = false;
                        }
                        else if (inDoubleQuote && current == '"')
                        {
                            inDoubleQuote = false;
                        }

                        continue;
                    }

                    if (current == '\'')
                    {
                        inSingleQuote = true;
                        continue;
                    }

                    if (current == '"')
                    {
                        inDoubleQuote = true;
                        continue;
                    }

                    if (TryGetOpenDelimiter(current, out _))
                    {
                        nestedDepth++;
                        continue;
                    }

                    if (IsCloseDelimiter(current))
                    {
                        if (nestedDepth > 0)
                        {
                            nestedDepth--;
                        }

                        continue;
                    }

                    if (nestedDepth != 0)
                    {
                        continue;
                    }

                    if (current == ',')
                    {
                        commaCount++;
                        continue;
                    }

                    if (!char.IsWhiteSpace(current))
                    {
                        hasValueToken = true;
                    }
                }
            }

            if (!hasValueToken)
            {
                return 0;
            }

            if (commaCount == 0)
            {
                return null;
            }

            return commaCount + 1;
        }

        private void DrawCodeLine(DrawingContext context, int lineIndex, string line, double y)
        {
            if (line.Length == 0)
            {
                return;
            }

            foreach (var segment in GetLineSegments(line, lineIndex))
            {
                DrawSegment(context, lineIndex, line, segment.Start, segment.Length, segment.Brush, y);
            }
        }

        private void DrawSegment(DrawingContext context, int lineIndex, string line, int start, int length, IBrush brush, double y)
        {
            if (length <= 0)
            {
                return;
            }

            var text = line.Substring(start, length);
            var x = _textStart + (start * CharWidth);
            if (IsMatrixConsoleSkin(_skinKey))
            {
                DrawMatrixSegment(context, text, lineIndex, start, brush, x, y);
                return;
            }

            DrawText(context, text, brush, new Point(x, y), FontSize);
        }

        private void DrawMatrixSegment(DrawingContext context, string text, int lineIndex, int startColumn, IBrush brush, double x, double y)
        {
            DrawText(context, text, BrushWithAlpha(brush, 78), new Point(x + 0.8, y), FontSize);
            DrawText(context, text, brush, new Point(x, y), FontSize);

            var tick = _animationPhase / 76;
            for (var index = 0; index < text.Length; index++)
            {
                var current = text[index];
                if (char.IsWhiteSpace(current))
                {
                    continue;
                }

                var column = startColumn + index;
                var shimmer = PositiveModulo(tick + (lineIndex * 17L) + (column * 7L), 41);
                if (shimmer > 2)
                {
                    continue;
                }

                var glyph = MatrixGlyphAt(lineIndex, column, tick).ToString();
                var jitterX = shimmer == 0 ? 0.62 : -0.34;
                var glyphX = x + (index * CharWidth);
                var glyphBrush = shimmer == 0 ? MatrixHotGlyphBrush : BrushWithAlpha(brush, 220);
                DrawText(context, glyph, MatrixGlyphHaloBrush, new Point(glyphX - 0.7, y - 0.2), FontSize);
                DrawText(context, glyph, glyphBrush, new Point(glyphX + jitterX, y), FontSize);
            }
        }

        private static char MatrixGlyphAt(int lineIndex, int column, long tick)
        {
            var index = PositiveModulo((lineIndex * 31L) + (column * 17L) + tick, MatrixCodeGlyphs.Length);
            return MatrixCodeGlyphs[index];
        }

        private RenderSegment[] GetLineSegments(string line, int lineIndex)
        {
            if (_lineSegmentCache.TryGetValue(lineIndex, out var cached))
            {
                return cached;
            }

            var brushes = BuildLineBrushes(line, lineIndex);
            var segments = new List<RenderSegment>(Math.Min(16, line.Length));
            var start = 0;
            var current = brushes[0];

            for (var index = 1; index < line.Length; index++)
            {
                if (ReferenceEquals(brushes[index], current))
                {
                    continue;
                }

                segments.Add(new RenderSegment(start, index - start, current));
                start = index;
                current = brushes[index];
            }

            segments.Add(new RenderSegment(start, line.Length - start, current));
            cached = segments.ToArray();
            _lineSegmentCache[lineIndex] = cached;
            return cached;
        }

        private IBrush[] BuildLineBrushes(string line, int lineIndex)
        {
            var brushes = new IBrush[line.Length];
            Array.Fill(brushes, Palette.Code);

            if (line.Length == 0)
            {
                return brushes;
            }

            var textMateSpans = GetTextMateSpans(line, lineIndex);
            if (textMateSpans.Count > 0)
            {
                foreach (var span in textMateSpans)
                {
                    Fill(brushes, span.Start, span.Length, span.Brush);
                }
            }
            else if (IsMarkupExtension(_extension))
            {
                HighlightMarkup(line, brushes);
            }
            else
            {
                HighlightStringsAndComments(line, brushes);
                HighlightWords(line, brushes);
            }

            if (lineIndex < _bracketColorIndexes.Length)
            {
                var bracketLine = _bracketColorIndexes[lineIndex];
                for (var index = 0; index < Math.Min(line.Length, bracketLine.Length); index++)
                {
                    var colorIndex = bracketLine[index];
                    if (colorIndex >= 0)
                    {
                        brushes[index] = Palette.Brackets[colorIndex % Palette.Brackets.Length];
                    }
                }
            }

            return brushes;
        }

        private void ConfigureGrammar()
        {
            _grammar = GetGrammarForExtension(_extension);
        }

        private List<TokenSpan> GetTextMateSpans(string line, int lineIndex)
        {
            if (_textMateSpanCache.TryGetValue(lineIndex, out var cached))
            {
                return cached;
            }

            var spans = new List<TokenSpan>();
            _textMateSpanCache[lineIndex] = spans;
            if (_grammar is null || line.Length == 0)
            {
                return spans;
            }

            try
            {
                var result = _grammar.TokenizeLine(line);
                foreach (var token in result.Tokens)
                {
                    var start = Math.Clamp(token.StartIndex, 0, line.Length);
                    var end = Math.Clamp(token.EndIndex, start, line.Length);
                    if (end <= start)
                    {
                        continue;
                    }

                    var brush = BrushFromScopes(token.Scopes);
                    if (!ReferenceEquals(brush, Palette.Code))
                    {
                        spans.Add(new TokenSpan(start, end - start, brush));
                    }
                }
            }
            catch
            {
                spans.Clear();
            }

            return spans;
        }

        private void HighlightStringsAndComments(string line, IBrush[] brushes)
        {
            for (var index = 0; index < line.Length; index++)
            {
                if (IsLineCommentStart(line, index))
                {
                    Fill(brushes, index, line.Length - index, Palette.Comment);
                    return;
                }

                if (IsBlockCommentStart(line, index))
                {
                    Fill(brushes, index, line.Length - index, Palette.Comment);
                    return;
                }

                var current = line[index];
                if (current is '"' or '\'')
                {
                    var end = FindStringEnd(line, index, current);
                    Fill(brushes, index, end - index + 1, Palette.String);
                    index = end;
                }
            }
        }

        private void HighlightWords(string line, IBrush[] brushes)
        {
            for (var index = 0; index < line.Length;)
            {
                if (!ReferenceEquals(brushes[index], Palette.Code))
                {
                    index++;
                    continue;
                }

                if (line[index] == '$' && _extension is ".ps1" or ".psm1" or ".psd1")
                {
                    var end = index + 1;
                    while (end < line.Length && IsIdentifierPart(line[end]))
                    {
                        end++;
                    }

                    Fill(brushes, index, end - index, Palette.Variable);
                    index = end;
                    continue;
                }

                if (char.IsDigit(line[index]))
                {
                    var end = index + 1;
                    while (end < line.Length && (char.IsDigit(line[end]) || line[end] == '.'))
                    {
                        end++;
                    }

                    Fill(brushes, index, end - index, Palette.Number);
                    index = end;
                    continue;
                }

                if (!IsIdentifierStart(line[index]))
                {
                    index++;
                    continue;
                }

                var start = index;
                index++;
                while (index < line.Length && IsIdentifierPart(line[index]))
                {
                    index++;
                }

                var word = line[start..index];
                if (IsKeyword(word))
                {
                    Fill(brushes, start, index - start, Palette.Keyword);
                }
                else if (IsTypeLike(word))
                {
                    Fill(brushes, start, index - start, Palette.Type);
                }
            }
        }

        private static void HighlightMarkup(string line, IBrush[] brushes)
        {
            if (line.TrimStart().StartsWith("<!--", StringComparison.Ordinal))
            {
                Fill(brushes, 0, line.Length, Palette.Comment);
                return;
            }

            var inTag = false;
            for (var index = 0; index < line.Length; index++)
            {
                var current = line[index];
                if (current == '<')
                {
                    inTag = true;
                    brushes[index] = Palette.Keyword;
                    continue;
                }

                if (current == '>')
                {
                    brushes[index] = Palette.Keyword;
                    inTag = false;
                    continue;
                }

                if (!inTag)
                {
                    continue;
                }

                if (current is '"' or '\'')
                {
                    var end = FindStringEnd(line, index, current);
                    Fill(brushes, index, end - index + 1, Palette.String);
                    index = end;
                    continue;
                }

                brushes[index] = char.IsLetterOrDigit(current) || current is '/' or '-' or ':' ? Palette.Type : Palette.Keyword;
            }
        }

        private bool IsLineCommentStart(string line, int index)
        {
            return _extension is ".ps1" or ".psm1" or ".psd1" or ".py" or ".sh" or ".yaml" or ".yml"
                ? line[index] == '#'
                : index + 1 < line.Length && line[index] == '/' && line[index + 1] == '/';
        }

        private bool IsBlockCommentStart(string line, int index)
        {
            return _extension is ".css" or ".js" or ".ts" or ".cs" or ".cpp" or ".c" or ".h" or ".hpp"
                && index + 1 < line.Length
                && line[index] == '/'
                && line[index + 1] == '*';
        }

        private bool IsKeyword(string word)
        {
            return _extension switch
            {
                ".ps1" or ".psm1" or ".psd1" => PowerShellKeywords.Contains(word),
                ".cs" => CSharpKeywords.Contains(word),
                ".js" or ".ts" or ".tsx" or ".jsx" => JavaScriptKeywords.Contains(word),
                ".py" => PythonKeywords.Contains(word),
                ".json" => JsonKeywords.Contains(word),
                ".css" => CssKeywords.Contains(word),
                _ => CommonKeywords.Contains(word)
            };
        }

        private static bool IsTypeLike(string word)
        {
            return word.Length > 1 && char.IsUpper(word[0]) && word.Skip(1).Any(char.IsLower);
        }

        private void RebuildVisibleLines()
        {
            var visible = new List<VisibleRow>(_lines.Length);
            for (var index = 0; index < _lines.Length; index++)
            {
                visible.Add(VisibleRow.ForCodeLine(index));
                if (_collapsedStartLines.Contains(index)
                    && _foldsByStartLine.TryGetValue(index, out var fold)
                    && IsSummaryFoldEnabled(fold))
                {
                    if (_showFoldSummaryPreview)
                    {
                        visible.Add(VisibleRow.ForFoldSummary(fold));
                    }

                    index = Math.Min(fold.EndLine, _lines.Length - 1);
                }
            }

            _visibleRows = visible.Count == 0 ? [VisibleRow.ForCodeLine(0)] : visible.ToArray();
            UpdateGutterLayout();
        }

        private bool PruneCollapsedSummaryFolds()
        {
            if (_collapsedStartLines.Count == 0)
            {
                return false;
            }

            var removed = false;
            foreach (var startLine in _collapsedStartLines.ToArray())
            {
                if (!_foldsByStartLine.TryGetValue(startLine, out var fold) || !IsSummaryFoldEnabled(fold))
                {
                    _collapsedStartLines.Remove(startLine);
                    removed = true;
                }
            }

            return removed;
        }

        private bool UpdateGutterLayout()
        {
            var maxVisibleLineNumber = GetMaxVisibleLineNumber();
            var visibleDigits = maxVisibleLineNumber.ToString(CultureInfo.InvariantCulture).Length;
            var visibleGuideDepth = GetMaxVisibleGuideDepth();
            if (visibleDigits == _visibleLineDigits
                && visibleGuideDepth == _visibleGuideDepth
                && _layoutShowFoldArrows == _showFoldArrows
                && _layoutFoldArrowsInCodeEditor == _foldArrowsInCodeEditor
                && _layoutUseParentChildArrowIndentation == _useParentChildArrowIndentation
                && _layoutShowVerticalScopeLines == _showVerticalScopeLines)
            {
                return false;
            }

            _visibleLineDigits = visibleDigits;
            _visibleGuideDepth = visibleGuideDepth;
            _layoutShowFoldArrows = _showFoldArrows;
            _layoutFoldArrowsInCodeEditor = _foldArrowsInCodeEditor;
            _layoutUseParentChildArrowIndentation = _useParentChildArrowIndentation;
            _layoutShowVerticalScopeLines = _showVerticalScopeLines;
            var requiredNumberWidth = Math.Max(_visibleLineDigits * CharWidth, 2 * CharWidth);
            var gutterArrowReserve = _showFoldArrows && !_foldArrowsInCodeEditor ? GutterArrowColumnWidth : 0;
            _lineNumberRight = LineNumberLeftPadding + requiredNumberWidth;
            _gutterWidth = Math.Max(MinGutterWidth, _lineNumberRight + NumberAreaRightPadding + gutterArrowReserve);
            _foldColumnStart = _gutterWidth + TextStartPadding;
            _guideBaseX = _foldColumnStart + (FoldSlotWidth / 2);
            _arrowBaseX = _foldArrowsInCodeEditor
                ? _guideBaseX
                : ((_lineNumberRight + _gutterWidth) / 2);
            var guideSlots = _showVerticalScopeLines && _useParentChildArrowIndentation
                ? Math.Max(1, _visibleGuideDepth + 1)
                : 1;
            _textStart = _foldColumnStart + (guideSlots * FoldSlotWidth) + TextStartPadding;
            return true;
        }

        private int GetMaxVisibleLineNumber()
        {
            if (_visibleRows.Length == 0)
            {
                return 1;
            }

            var firstVisibleIndex = Math.Max(0, (int)Math.Floor((_viewportOffset - TopPadding) / LineHeight));
            var lastVisibleIndex = Math.Min(
                _visibleRows.Length - 1,
                (int)Math.Ceiling((_viewportOffset + _viewportHeight - TopPadding) / LineHeight));

            if (lastVisibleIndex < firstVisibleIndex)
            {
                lastVisibleIndex = firstVisibleIndex;
            }

            var maxVisible = 1;
            for (var index = firstVisibleIndex; index <= lastVisibleIndex; index++)
            {
                var row = _visibleRows[index];
                if (row.IsFoldSummary)
                {
                    continue;
                }

                maxVisible = Math.Max(maxVisible, row.LineIndex + 1);
            }

            return maxVisible;
        }

        private int GetMaxVisibleGuideDepth()
        {
            if (_visibleRows.Length == 0)
            {
                return 0;
            }

            var firstVisibleIndex = Math.Max(0, (int)Math.Floor((_viewportOffset - TopPadding) / LineHeight));
            var lastVisibleIndex = Math.Min(
                _visibleRows.Length - 1,
                (int)Math.Ceiling((_viewportOffset + _viewportHeight - TopPadding) / LineHeight));

            if (lastVisibleIndex < firstVisibleIndex)
            {
                lastVisibleIndex = firstVisibleIndex;
            }

            var depth = 0;
            for (var index = firstVisibleIndex; index <= lastVisibleIndex; index++)
            {
                var row = _visibleRows[index];
                var lineIndex = row.IsFoldSummary ? row.Fold.StartLine : row.LineIndex;
                var scopes = GetScopesForLine(lineIndex);
                for (var scopeIndex = 0; scopeIndex < scopes.Count; scopeIndex++)
                {
                    depth = Math.Max(depth, scopes[scopeIndex].NestingLevel);
                }

                if (!row.IsFoldSummary && _foldsByStartLine.TryGetValue(row.LineIndex, out var fold))
                {
                    depth = Math.Max(depth, fold.NestingLevel);
                }
            }

            return depth;
        }

        private static string[] NormalizeLines(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return [""];
            }

            return text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        }

        private static string DetectLineBreak(string text)
        {
            if (text.Contains("\r\n", StringComparison.Ordinal))
            {
                return "\r\n";
            }

            if (text.Contains('\n'))
            {
                return "\n";
            }

            if (text.Contains('\r'))
            {
                return "\r";
            }

            return Environment.NewLine;
        }

        private static Dictionary<int, FoldRegion> BuildFoldRegions(IReadOnlyList<string> lines)
        {
            var result = new Dictionary<int, FoldRegion>();
            var stack = new Stack<(int OpenLine, int OwnerLine, int OpenColumn, char OpenDelimiter, char CloseDelimiter)>();
            var inBlockComment = false;
            var inSingleQuote = false;
            var inDoubleQuote = false;
            var inVerbatimString = false;
            var escapeStringCharacter = false;

            for (var lineIndex = 0; lineIndex < lines.Count; lineIndex++)
            {
                var line = lines[lineIndex];
                for (var column = 0; column < line.Length; column++)
                {
                    var current = line[column];
                    var next = column + 1 < line.Length ? line[column + 1] : '\0';

                    if (inBlockComment)
                    {
                        if (current == '*' && next == '/')
                        {
                            inBlockComment = false;
                            column++;
                        }

                        continue;
                    }

                    if (inSingleQuote)
                    {
                        if (escapeStringCharacter)
                        {
                            escapeStringCharacter = false;
                            continue;
                        }

                        if (current == '\\')
                        {
                            escapeStringCharacter = true;
                            continue;
                        }

                        if (current == '\'')
                        {
                            inSingleQuote = false;
                        }

                        continue;
                    }

                    if (inDoubleQuote)
                    {
                        if (inVerbatimString)
                        {
                            if (current == '"' && next == '"')
                            {
                                column++;
                                continue;
                            }

                            if (current == '"')
                            {
                                inDoubleQuote = false;
                                inVerbatimString = false;
                            }
                        }
                        else
                        {
                            if (escapeStringCharacter)
                            {
                                escapeStringCharacter = false;
                                continue;
                            }

                            if (current == '\\')
                            {
                                escapeStringCharacter = true;
                                continue;
                            }

                            if (current == '"')
                            {
                                inDoubleQuote = false;
                            }
                        }

                        continue;
                    }

                    if (current == '/' && next == '/')
                    {
                        break;
                    }

                    if (current == '/' && next == '*')
                    {
                        inBlockComment = true;
                        column++;
                        continue;
                    }

                    if (current == '#')
                    {
                        var leading = line[..column];
                        if (string.IsNullOrWhiteSpace(leading))
                        {
                            break;
                        }
                    }

                    if (current == '\'')
                    {
                        inSingleQuote = true;
                        escapeStringCharacter = false;
                        continue;
                    }

                    if (current == '"')
                    {
                        var previous = column > 0 ? line[column - 1] : '\0';
                        var beforePrevious = column > 1 ? line[column - 2] : '\0';
                        inDoubleQuote = true;
                        inVerbatimString = previous == '@'
                            || (previous == '$' && beforePrevious == '@')
                            || (previous == '@' && beforePrevious == '$');
                        escapeStringCharacter = false;
                        continue;
                    }

                    if (TryGetOpenDelimiter(current, out var closeDelimiter))
                    {
                        stack.Push((lineIndex, FindFoldOwnerLine(lines, lineIndex, column, current), column, current, closeDelimiter));
                    }
                    else if (IsCloseDelimiter(current))
                    {
                        while (stack.Count > 0 && stack.Peek().CloseDelimiter != current)
                        {
                            stack.Pop();
                        }

                        if (stack.Count == 0)
                        {
                            continue;
                        }

                        var open = stack.Pop();
                        if (lineIndex > open.OwnerLine
                            && lineIndex > open.OpenLine
                            && (!result.TryGetValue(open.OwnerLine, out var existing) || lineIndex > existing.EndLine))
                        {
                            result[open.OwnerLine] = new FoldRegion(
                                open.OwnerLine,
                                lineIndex,
                                open.OpenLine,
                                open.OpenColumn,
                                column,
                                open.OpenDelimiter,
                                open.CloseDelimiter,
                                0);
                        }
                    }
                }

                if (inSingleQuote)
                {
                    inSingleQuote = false;
                    escapeStringCharacter = false;
                }

                if (inDoubleQuote && !inVerbatimString)
                {
                    inDoubleQuote = false;
                    escapeStringCharacter = false;
                }
            }

            return ApplyFoldNestingLevels(result);
        }

        private static Dictionary<int, FoldRegion> ApplyFoldNestingLevels(Dictionary<int, FoldRegion> foldsByStartLine)
        {
            if (foldsByStartLine.Count == 0)
            {
                return foldsByStartLine;
            }

            var ordered = foldsByStartLine.Values
                .OrderBy(fold => fold.StartLine)
                .ThenByDescending(fold => fold.EndLine)
                .ToList();
            var parentStack = new Stack<FoldRegion>();
            var result = new Dictionary<int, FoldRegion>(foldsByStartLine.Count);

            foreach (var fold in ordered)
            {
                while (parentStack.Count > 0 && !IsNestedFold(parentStack.Peek(), fold))
                {
                    parentStack.Pop();
                }

                var foldWithLevel = fold with { NestingLevel = parentStack.Count };
                result[fold.StartLine] = foldWithLevel;
                parentStack.Push(foldWithLevel);
            }

            return result;
        }

        private static bool IsNestedFold(FoldRegion parent, FoldRegion child)
        {
            if (child.StartLine <= parent.StartLine)
            {
                return false;
            }

            if (child.EndLine > parent.EndLine)
            {
                return false;
            }

            if (child.EndLine < child.StartLine)
            {
                return false;
            }

            if (child.StartLine == parent.EndLine)
            {
                return false;
            }

            return true;
        }

        private static bool TryGetOpenDelimiter(char value, out char closeDelimiter)
        {
            switch (value)
            {
                case '{':
                    closeDelimiter = '}';
                    return true;
                case '[':
                    closeDelimiter = ']';
                    return true;
                case '(':
                    closeDelimiter = ')';
                    return true;
                default:
                    closeDelimiter = '\0';
                    return false;
            }
        }

        private static bool IsCloseDelimiter(char value)
        {
            return value is '}' or ']' or ')';
        }

        private static int FindFoldOwnerLine(IReadOnlyList<string> lines, int braceLine, int braceColumn, char openDelimiter)
        {
            if (braceColumn > 0 && lines[braceLine][..braceColumn].Trim().Length > 0)
            {
                return braceLine;
            }

            for (var lineIndex = braceLine - 1; lineIndex >= 0; lineIndex--)
            {
                var text = lines[lineIndex].Trim();
                if (text.Length == 0)
                {
                    continue;
                }

                if (openDelimiter == '[' && !ShouldAttachArrayFoldToPreviousLine(text))
                {
                    return braceLine;
                }

                return lineIndex;
            }

            return braceLine;
        }

        private static bool ShouldAttachArrayFoldToPreviousLine(string previousLine)
        {
            var text = previousLine.TrimEnd();
            if (text.Length == 0)
            {
                return false;
            }

            if (text.EndsWith(",", StringComparison.Ordinal)
                || text.EndsWith("[", StringComparison.Ordinal)
                || text.EndsWith("{", StringComparison.Ordinal)
                || text.EndsWith("(", StringComparison.Ordinal))
            {
                return false;
            }

            if (text.EndsWith("=", StringComparison.Ordinal)
                || text.EndsWith(":", StringComparison.Ordinal)
                || text.EndsWith("=>", StringComparison.Ordinal))
            {
                return true;
            }

            var last = text[^1];
            return char.IsLetterOrDigit(last) || last is ')' or ']' or '"' or '\'';
        }

        private static int[][] BuildBracketColors(IReadOnlyList<string> lines)
        {
            var result = new int[lines.Count][];
            var stack = new Stack<int>();
            var depth = 0;

            for (var lineIndex = 0; lineIndex < lines.Count; lineIndex++)
            {
                var line = lines[lineIndex];
                var colors = new int[line.Length];
                Array.Fill(colors, -1);

                for (var column = 0; column < line.Length; column++)
                {
                    var current = line[column];
                    if (current is '{' or '[' or '(')
                    {
                        var color = depth % Palette.Brackets.Length;
                        colors[column] = color;
                        stack.Push(color);
                        depth++;
                    }
                    else if (current is '}' or ']' or ')')
                    {
                        depth = Math.Max(0, depth - 1);
                        colors[column] = stack.Count > 0 ? stack.Pop() : depth % Palette.Brackets.Length;
                    }
                }

                result[lineIndex] = colors;
            }

            return result;
        }
    }

    private sealed class CodeMinimap : Control
    {
        private const double HorizontalPadding = 2;
        private const double TopPadding = EditorTopPadding;
        private const double BottomPadding = EditorBottomPadding;
        private const double MiniLineHeight = 2.45;
        private const double MiniFontSize = 1.95;
        private const double MiniCharWidth = 0.92;
        private static readonly IBrush MiniCrtScanlineBrush = Brush(0, 0, 0, 76);
        private static readonly IBrush MiniCrtTintBrush = Brush(0, 255, 102, 24);

        private readonly Dictionary<int, List<TokenSpan>> _textMateSpanCache = [];
        private readonly Dictionary<int, RenderSegment[]> _lineSegmentCache = [];
        private IReadOnlyDictionary<int, string> _lineChanges = new Dictionary<int, string>();
        private string[] _lines = [""];
        private string _extension = "";
        private IGrammar? _grammar;
        private int[][] _bracketColorIndexes = [];
        private double _viewportOffset;
        private double _viewportHeight;
        private double _extentHeight;
        private string _skinKey = "default";
        private long _animationPhase;

        public CodeMinimap()
        {
            ClipToBounds = true;
        }

        public void SetDocument(string text, string path, IReadOnlyDictionary<int, string>? lineChanges)
        {
            _extension = Path.GetExtension(path).ToLowerInvariant();
            _lineChanges = lineChanges ?? new Dictionary<int, string>();
            _lines = string.IsNullOrEmpty(text)
                ? [""]
                : text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');

            _textMateSpanCache.Clear();
            _lineSegmentCache.Clear();
            ConfigureGrammar();
            _bracketColorIndexes = BuildMiniBracketColors(_lines);
            InvalidateMeasure();
            InvalidateVisual();
        }

        public void SetViewport(double offset, double viewportHeight, double extentHeight)
        {
            _viewportOffset = Math.Max(0, offset);
            _viewportHeight = Math.Max(EditorLineHeight, viewportHeight);
            _extentHeight = Math.Max(0, extentHeight);
            InvalidateVisual();
        }

        public void ApplyTheme()
        {
            _textMateSpanCache.Clear();
            _lineSegmentCache.Clear();
            InvalidateVisual();
        }

        public void SetSkin(string? skinKey)
        {
            var normalizedSkin = NormalizeSkinKey(skinKey);
            if (string.Equals(_skinKey, normalizedSkin, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _skinKey = normalizedSkin;
            InvalidateVisual();
        }

        public void SetAnimationPhase(long phase)
        {
            _animationPhase = phase;
            if (IsMatrixConsoleSkin(_skinKey))
            {
                InvalidateVisual();
            }
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            var height = double.IsInfinity(availableSize.Height) ? 0 : availableSize.Height;
            return new Size(MinimapWidth, height);
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);

            var bounds = Bounds;
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                return;
            }

            var content = new Rect(
                HorizontalPadding,
                0,
                Math.Max(0, bounds.Width - (HorizontalPadding * 2)),
                bounds.Height);

            context.DrawRectangle(Palette.Background, null, bounds);
            var miniScrollOffset = GetMiniScrollOffset(bounds.Height);
            using (context.PushClip(content))
            {
                DrawDocument(context, content, miniScrollOffset);
            }

            DrawViewport(context, content, miniScrollOffset);
            if (IsMatrixConsoleSkin(_skinKey))
            {
                DrawMiniCrtOverlay(context, bounds);
            }
        }

        private void DrawMiniCrtOverlay(DrawingContext context, Rect bounds)
        {
            context.DrawRectangle(MiniCrtTintBrush, null, bounds);
            var firstScanline = PositiveModulo(_animationPhase / 52, 5);
            for (var y = firstScanline; y < bounds.Height; y += 5)
            {
                context.DrawRectangle(MiniCrtScanlineBrush, null, new Rect(0, y, bounds.Width, 1));
            }
        }

        private double GetMiniContentHeight()
        {
            return TopPadding + (_lines.Length * MiniLineHeight) + BottomPadding;
        }

        private double GetMiniScrollOffset(double height)
        {
            var contentHeight = GetMiniContentHeight();
            var maxMiniOffset = Math.Max(0, contentHeight - height);
            var maxEditorOffset = Math.Max(0, _extentHeight - _viewportHeight);
            if (maxMiniOffset <= 0 || maxEditorOffset <= 0)
            {
                return 0;
            }

            return Math.Clamp(_viewportOffset / maxEditorOffset, 0, 1) * maxMiniOffset;
        }

        private void DrawDocument(DrawingContext context, Rect content, double miniScrollOffset)
        {
            var firstLine = Math.Max(0, (int)Math.Floor((miniScrollOffset - TopPadding) / MiniLineHeight) - 2);
            var lastLine = Math.Min(
                _lines.Length - 1,
                (int)Math.Ceiling((miniScrollOffset + content.Height - TopPadding) / MiniLineHeight) + 2);

            for (var lineIndex = firstLine; lineIndex <= lastLine; lineIndex++)
            {
                var y = TopPadding + (lineIndex * MiniLineHeight) - miniScrollOffset;
                DrawMiniLine(context, content, lineIndex, y);
            }
        }

        private void DrawMiniLine(DrawingContext context, Rect content, int lineIndex, double y)
        {
            if (lineIndex < 0 || lineIndex >= _lines.Length || y > content.Bottom)
            {
                return;
            }

            if (_lineChanges.TryGetValue(lineIndex, out var change))
            {
                var brush = change == "delete" ? Palette.MinimapDelete : Palette.MinimapAdd;
                context.DrawRectangle(brush, null, new Rect(content.X, y, content.Width, Math.Max(1.4, MiniLineHeight)));
            }

            var line = _lines[lineIndex];
            if (line.Length == 0)
            {
                return;
            }

            var visibleColumns = Math.Min(line.Length, (int)Math.Ceiling(content.Width / MiniCharWidth) + 2);
            foreach (var segment in GetLineSegments(line, lineIndex))
            {
                var start = Math.Max(0, segment.Start);
                var end = Math.Min(visibleColumns, segment.Start + segment.Length);
                if (end <= start)
                {
                    continue;
                }

                var text = line.Substring(start, end - start);
                var x = content.X + (start * MiniCharWidth);
                DrawText(context, text, segment.Brush, new Point(x, y), MiniFontSize);
            }
        }

        private void DrawViewport(DrawingContext context, Rect content, double miniScrollOffset)
        {
            if (_extentHeight <= 0 || _viewportHeight <= 0 || _lines.Length == 0)
            {
                return;
            }

            var lineOffset = Math.Max(0, (_viewportOffset - TopPadding) / EditorLineHeight);
            var top = TopPadding + (lineOffset * MiniLineHeight) - miniScrollOffset;
            var height = ClampViewportHeight(
                (_viewportHeight / EditorLineHeight) * MiniLineHeight,
                7,
                Math.Min(content.Height, GetMiniContentHeight()));
            if (top + height > content.Bottom)
            {
                top = content.Bottom - height;
            }

            var rect = new Rect(content.X - 1, Math.Max(0, top), content.Width + 2, Math.Min(content.Height, height));
            context.DrawRectangle(Palette.MinimapViewport, new Pen(Palette.MinimapViewportBorder, 1), rect, 2, 2);
        }

        public double GetEditorOffsetForPoint(Point point)
        {
            if (_lines.Length == 0)
            {
                return 0;
            }

            var miniScrollOffset = GetMiniScrollOffset(Bounds.Height);
            var lineIndex = (miniScrollOffset + point.Y - TopPadding) / MiniLineHeight;

            var targetLine = Math.Clamp((int)Math.Floor(lineIndex), 0, Math.Max(0, _lines.Length - 1));
            return TopPadding + (targetLine * EditorLineHeight);
        }

        public double GetEditorOffsetForTrackPoint(Point point)
        {
            var maxEditorOffset = Math.Max(0, _extentHeight - _viewportHeight);
            var height = Bounds.Height;
            if (height <= 0 || maxEditorOffset <= 0)
            {
                return 0;
            }

            var ratio = Math.Clamp(point.Y / height, 0, 1);
            return ratio * maxEditorOffset;
        }

        private static double ClampViewportHeight(double value, double minimum, double maximum)
        {
            var usableMaximum = Math.Max(0, maximum);
            if (usableMaximum <= minimum)
            {
                return usableMaximum;
            }

            return Math.Clamp(value, minimum, usableMaximum);
        }

        private RenderSegment[] GetLineSegments(string line, int lineIndex)
        {
            if (_lineSegmentCache.TryGetValue(lineIndex, out var cached))
            {
                return cached;
            }

            if (line.Length == 0)
            {
                return [];
            }

            var brushes = BuildLineBrushes(line, lineIndex);
            var segments = new List<RenderSegment>(Math.Min(16, line.Length));
            var start = 0;
            var current = brushes[0];

            for (var index = 1; index < line.Length; index++)
            {
                if (ReferenceEquals(brushes[index], current))
                {
                    continue;
                }

                segments.Add(new RenderSegment(start, index - start, current));
                start = index;
                current = brushes[index];
            }

            segments.Add(new RenderSegment(start, line.Length - start, current));
            cached = segments.ToArray();
            _lineSegmentCache[lineIndex] = cached;
            return cached;
        }

        private IBrush[] BuildLineBrushes(string line, int lineIndex)
        {
            var brushes = new IBrush[line.Length];
            Array.Fill(brushes, Palette.MinimapCode);

            var textMateSpans = GetTextMateSpans(line, lineIndex);
            if (textMateSpans.Count > 0)
            {
                foreach (var span in textMateSpans)
                {
                    Fill(brushes, span.Start, span.Length, span.Brush);
                }
            }
            else if (IsMarkupExtension(_extension))
            {
                HighlightMarkup(line, brushes);
            }
            else
            {
                HighlightStringsAndComments(line, brushes);
                HighlightWords(line, brushes);
            }

            if (lineIndex < _bracketColorIndexes.Length)
            {
                var bracketLine = _bracketColorIndexes[lineIndex];
                for (var index = 0; index < Math.Min(line.Length, bracketLine.Length); index++)
                {
                    var colorIndex = bracketLine[index];
                    if (colorIndex >= 0)
                    {
                        brushes[index] = Palette.Brackets[colorIndex % Palette.Brackets.Length];
                    }
                }
            }

            return brushes;
        }

        private void ConfigureGrammar()
        {
            _grammar = GetGrammarForExtension(_extension);
        }

        private List<TokenSpan> GetTextMateSpans(string line, int lineIndex)
        {
            if (_textMateSpanCache.TryGetValue(lineIndex, out var cached))
            {
                return cached;
            }

            var spans = new List<TokenSpan>();
            _textMateSpanCache[lineIndex] = spans;
            if (_grammar is null || line.Length == 0)
            {
                return spans;
            }

            try
            {
                var result = _grammar.TokenizeLine(line);
                foreach (var token in result.Tokens)
                {
                    var start = Math.Clamp(token.StartIndex, 0, line.Length);
                    var end = Math.Clamp(token.EndIndex, start, line.Length);
                    if (end <= start)
                    {
                        continue;
                    }

                    var brush = MiniBrushFromScopes(token.Scopes);
                    if (!ReferenceEquals(brush, Palette.MinimapCode))
                    {
                        spans.Add(new TokenSpan(start, end - start, brush));
                    }
                }
            }
            catch
            {
                spans.Clear();
            }

            return spans;
        }

        private static IBrush MiniBrushFromScopes(IReadOnlyList<string> scopes)
        {
            var brush = BrushFromScopes(scopes);
            if (ReferenceEquals(brush, Palette.Code))
            {
                return Palette.MinimapCode;
            }

            if (ReferenceEquals(brush, Palette.Comment))
            {
                return Palette.MinimapComment;
            }

            return ReferenceEquals(brush, Palette.Keyword) ? Palette.MinimapKeyword : brush;
        }

        private void HighlightStringsAndComments(string line, IBrush[] brushes)
        {
            for (var index = 0; index < line.Length; index++)
            {
                if (IsLineCommentStart(line, index))
                {
                    Fill(brushes, index, line.Length - index, Palette.MinimapComment);
                    return;
                }

                if (IsBlockCommentStart(line, index))
                {
                    Fill(brushes, index, line.Length - index, Palette.MinimapComment);
                    return;
                }

                var current = line[index];
                if (current is '"' or '\'')
                {
                    var end = FindStringEnd(line, index, current);
                    Fill(brushes, index, end - index + 1, Palette.String);
                    index = end;
                }
            }
        }

        private void HighlightWords(string line, IBrush[] brushes)
        {
            for (var index = 0; index < line.Length;)
            {
                if (!ReferenceEquals(brushes[index], Palette.MinimapCode))
                {
                    index++;
                    continue;
                }

                if (line[index] == '$' && _extension is ".ps1" or ".psm1" or ".psd1")
                {
                    var end = index + 1;
                    while (end < line.Length && IsIdentifierPart(line[end]))
                    {
                        end++;
                    }

                    Fill(brushes, index, end - index, Palette.Variable);
                    index = end;
                    continue;
                }

                if (char.IsDigit(line[index]))
                {
                    var end = index + 1;
                    while (end < line.Length && (char.IsDigit(line[end]) || line[end] == '.'))
                    {
                        end++;
                    }

                    Fill(brushes, index, end - index, Palette.Number);
                    index = end;
                    continue;
                }

                if (!IsIdentifierStart(line[index]))
                {
                    index++;
                    continue;
                }

                var start = index;
                index++;
                while (index < line.Length && IsIdentifierPart(line[index]))
                {
                    index++;
                }

                var word = line[start..index];
                if (IsKeyword(word))
                {
                    Fill(brushes, start, index - start, Palette.Keyword);
                }
                else if (IsTypeLike(word))
                {
                    Fill(brushes, start, index - start, Palette.Type);
                }
            }
        }

        private static void HighlightMarkup(string line, IBrush[] brushes)
        {
            if (line.TrimStart().StartsWith("<!--", StringComparison.Ordinal))
            {
                Fill(brushes, 0, line.Length, Palette.MinimapComment);
                return;
            }

            var inTag = false;
            for (var index = 0; index < line.Length; index++)
            {
                var current = line[index];
                if (current == '<')
                {
                    inTag = true;
                    brushes[index] = Palette.Keyword;
                    continue;
                }

                if (current == '>')
                {
                    brushes[index] = Palette.Keyword;
                    inTag = false;
                    continue;
                }

                if (!inTag)
                {
                    continue;
                }

                if (current is '"' or '\'')
                {
                    var end = FindStringEnd(line, index, current);
                    Fill(brushes, index, end - index + 1, Palette.String);
                    index = end;
                    continue;
                }

                brushes[index] = char.IsLetterOrDigit(current) || current is '/' or '-' or ':' ? Palette.Type : Palette.Keyword;
            }
        }

        private bool IsLineCommentStart(string line, int index)
        {
            return _extension is ".ps1" or ".psm1" or ".psd1" or ".py" or ".sh" or ".yaml" or ".yml"
                ? line[index] == '#'
                : index + 1 < line.Length && line[index] == '/' && line[index + 1] == '/';
        }

        private bool IsBlockCommentStart(string line, int index)
        {
            return _extension is ".css" or ".js" or ".ts" or ".cs" or ".cpp" or ".c" or ".h" or ".hpp"
                && index + 1 < line.Length
                && line[index] == '/'
                && line[index + 1] == '*';
        }

        private bool IsKeyword(string word)
        {
            return _extension switch
            {
                ".ps1" or ".psm1" or ".psd1" => PowerShellKeywords.Contains(word),
                ".cs" => CSharpKeywords.Contains(word),
                ".js" or ".ts" or ".tsx" or ".jsx" => JavaScriptKeywords.Contains(word),
                ".py" => PythonKeywords.Contains(word),
                ".json" => JsonKeywords.Contains(word),
                ".css" => CssKeywords.Contains(word),
                _ => CommonKeywords.Contains(word)
            };
        }

        private static bool IsTypeLike(string word)
        {
            return word.Length > 1 && char.IsUpper(word[0]) && word.Skip(1).Any(char.IsLower);
        }

        private static int[][] BuildMiniBracketColors(IReadOnlyList<string> lines)
        {
            var result = new int[lines.Count][];
            var stack = new Stack<int>();
            var depth = 0;

            for (var lineIndex = 0; lineIndex < lines.Count; lineIndex++)
            {
                var line = lines[lineIndex];
                var colors = new int[line.Length];
                Array.Fill(colors, -1);

                for (var column = 0; column < line.Length; column++)
                {
                    var current = line[column];
                    if (current is '{' or '[' or '(')
                    {
                        var color = depth % Palette.Brackets.Length;
                        colors[column] = color;
                        stack.Push(color);
                        depth++;
                    }
                    else if (current is '}' or ']' or ')')
                    {
                        depth = Math.Max(0, depth - 1);
                        colors[column] = stack.Count > 0 ? stack.Pop() : depth % Palette.Brackets.Length;
                    }
                }

                result[lineIndex] = colors;
            }

            return result;
        }
    }

    private readonly record struct VisibleRow(int LineIndex, bool IsFoldSummary, FoldRegion Fold)
    {
        public static VisibleRow ForCodeLine(int lineIndex)
        {
            return new VisibleRow(lineIndex, false, default);
        }

        public static VisibleRow ForFoldSummary(FoldRegion fold)
        {
            return new VisibleRow(fold.StartLine, true, fold);
        }
    }

    private readonly record struct FoldRegion(
        int StartLine,
        int EndLine,
        int OpenLine,
        int OpenColumn,
        int CloseColumn,
        char OpenDelimiter,
        char CloseDelimiter,
        int NestingLevel);

    private readonly record struct TokenSpan(int Start, int Length, IBrush Brush);

    private readonly record struct RenderSegment(int Start, int Length, IBrush Brush);

    private readonly record struct TextPosition(int Line, int Column);

    private static IBrush BrushFromScopes(IReadOnlyList<string> scopes)
    {
        for (var index = scopes.Count - 1; index >= 0; index--)
        {
            var scope = scopes[index];
            if (scope.Contains("comment", StringComparison.Ordinal))
            {
                return Palette.Comment;
            }

            if (scope.Contains("string", StringComparison.Ordinal))
            {
                return Palette.String;
            }

            if (scope.Contains("constant.numeric", StringComparison.Ordinal)
                || scope.Contains("constant.character", StringComparison.Ordinal))
            {
                return Palette.Number;
            }

            if (scope.Contains("constant.language", StringComparison.Ordinal)
                || scope.Contains("keyword", StringComparison.Ordinal)
                || scope.Contains("storage.modifier", StringComparison.Ordinal))
            {
                return Palette.Keyword;
            }

            if (scope.Contains("entity.name.function", StringComparison.Ordinal)
                || scope.Contains("support.function", StringComparison.Ordinal))
            {
                return Palette.Function;
            }

            if (scope.Contains("entity.name.type", StringComparison.Ordinal)
                || scope.Contains("support.type", StringComparison.Ordinal)
                || scope.Contains("storage.type", StringComparison.Ordinal)
                || scope.Contains("entity.name.tag", StringComparison.Ordinal))
            {
                return Palette.Type;
            }

            if (scope.Contains("variable", StringComparison.Ordinal)
                || scope.Contains("entity.name.variable", StringComparison.Ordinal))
            {
                return Palette.Variable;
            }

            if (scope.Contains("markup.heading", StringComparison.Ordinal)
                || scope.Contains("markup.bold", StringComparison.Ordinal))
            {
                return Palette.Keyword;
            }
        }

        return Palette.Code;
    }

    private static int PositiveModulo(long value, int divisor)
    {
        if (divisor <= 0)
        {
            return 0;
        }

        var result = value % divisor;
        return (int)(result < 0 ? result + divisor : result);
    }

    private static SolidColorBrush Brush(byte red, byte green, byte blue, byte alpha = 255)
    {
        return new SolidColorBrush(Color.FromArgb(alpha, red, green, blue));
    }

    private static SolidColorBrush BrushWithAlpha(IBrush brush, byte alpha)
    {
        var color = brush is ISolidColorBrush solid ? solid.Color : Colors.White;
        return new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
    }

    private static void DrawText(DrawingContext context, string text, IBrush brush, Point point, double fontSize)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var formatted = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            Palette.CodeTypeface,
            fontSize,
            brush);

        context.DrawText(formatted, point);
    }

    private static void Fill(IBrush[] brushes, int start, int length, IBrush brush)
    {
        var end = Math.Min(brushes.Length, start + length);
        for (var index = Math.Max(0, start); index < end; index++)
        {
            brushes[index] = brush;
        }
    }

    private static int FindStringEnd(string line, int start, char quote)
    {
        for (var index = start + 1; index < line.Length; index++)
        {
            if (line[index] == quote && (index == 0 || line[index - 1] != '\\'))
            {
                return index;
            }
        }

        return line.Length - 1;
    }

    private static bool IsIdentifierStart(char value)
    {
        return char.IsLetter(value) || value == '_';
    }

    private static bool IsIdentifierPart(char value)
    {
        return char.IsLetterOrDigit(value) || value is '_' or '-';
    }

    private static bool IsMarkupExtension(string extension)
    {
        return extension is ".xml" or ".xaml" or ".axaml" or ".html" or ".htm";
    }

    private static readonly HashSet<string> CommonKeywords = new(StringComparer.Ordinal)
    {
        "break", "case", "catch", "continue", "default", "do", "else", "false", "finally", "for", "foreach",
        "if", "in", "new", "null", "return", "switch", "this", "throw", "true", "try", "while"
    };

    private static readonly HashSet<string> CSharpKeywords = new(StringComparer.Ordinal)
    {
        "abstract", "as", "async", "await", "base", "bool", "break", "case", "catch", "class", "const",
        "continue", "decimal", "default", "do", "double", "else", "enum", "event", "false", "finally",
        "fixed", "float", "for", "foreach", "get", "global", "if", "in", "int", "interface", "internal",
        "is", "lock", "long", "namespace", "new", "not", "null", "object", "operator", "out", "override",
        "params", "private", "protected", "public", "readonly", "record", "ref", "return", "sealed", "set",
        "short", "static", "string", "struct", "switch", "this", "throw", "true", "try", "typeof", "using",
        "var", "virtual", "void", "volatile", "when", "while", "with", "yield"
    };

    private static readonly HashSet<string> PowerShellKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "begin", "break", "catch", "class", "continue", "data", "do", "dynamicparam", "else", "elseif", "end",
        "enum", "exit", "filter", "finally", "for", "foreach", "from", "function", "if", "in", "param",
        "process", "return", "switch", "throw", "trap", "try", "until", "using", "var", "while", "workflow"
    };

    private static readonly HashSet<string> JavaScriptKeywords = new(StringComparer.Ordinal)
    {
        "async", "await", "break", "case", "catch", "class", "const", "continue", "debugger", "default",
        "delete", "do", "else", "export", "extends", "false", "finally", "for", "from", "function", "if",
        "import", "in", "instanceof", "let", "new", "null", "return", "static", "super", "switch", "this",
        "throw", "true", "try", "typeof", "undefined", "var", "void", "while", "yield"
    };

    private static readonly HashSet<string> PythonKeywords = new(StringComparer.Ordinal)
    {
        "and", "as", "assert", "async", "await", "break", "class", "continue", "def", "del", "elif", "else",
        "except", "False", "finally", "for", "from", "global", "if", "import", "in", "is", "lambda", "None",
        "nonlocal", "not", "or", "pass", "raise", "return", "True", "try", "while", "with", "yield"
    };

    private static readonly HashSet<string> JsonKeywords = new(StringComparer.Ordinal)
    {
        "false", "null", "true"
    };

    private static readonly HashSet<string> CssKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "auto", "block", "flex", "grid", "important", "inherit", "inline", "none", "relative", "absolute",
        "fixed", "sticky", "solid", "transparent"
    };
}
