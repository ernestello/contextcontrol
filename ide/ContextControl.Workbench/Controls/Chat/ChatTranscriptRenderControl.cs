// CC-DESC: Draws the shared chat/image-generation transcript as one cached virtualized surface.

using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ContextControl.Workbench.ViewModels;

namespace ContextControl.Workbench.Controls;

public enum ChatTranscriptHitKind
{
    None,
    OpenAttachment,
    OpenImagePreview,
    DownloadAttachment,
    CopyMessage,
    CreateProject,
    ToggleSnippet,
    CopySnippet,
    SaveSnippet,
    UseSnippet,
    PreviewSnippet,
    ToggleThinking
}

public sealed class ChatTranscriptRenderControl : Control
{
    public static readonly StyledProperty<IReadOnlyList<LocalLlmChatMessageViewModel>?> ItemsProperty =
        AvaloniaProperty.Register<ChatTranscriptRenderControl, IReadOnlyList<LocalLlmChatMessageViewModel>?>(nameof(Items));

    public static readonly StyledProperty<ICommand?> OpenAttachmentCommandProperty =
        AvaloniaProperty.Register<ChatTranscriptRenderControl, ICommand?>(nameof(OpenAttachmentCommand));

    public static readonly StyledProperty<ICommand?> CopyChatTextCommandProperty =
        AvaloniaProperty.Register<ChatTranscriptRenderControl, ICommand?>(nameof(CopyChatTextCommand));

    public static readonly StyledProperty<ICommand?> CreateProjectFromMessageCommandProperty =
        AvaloniaProperty.Register<ChatTranscriptRenderControl, ICommand?>(nameof(CreateProjectFromMessageCommand));

    public static readonly StyledProperty<ICommand?> ToggleSnippetCommandProperty =
        AvaloniaProperty.Register<ChatTranscriptRenderControl, ICommand?>(nameof(ToggleSnippetCommand));

    public static readonly StyledProperty<ICommand?> CopySnippetCommandProperty =
        AvaloniaProperty.Register<ChatTranscriptRenderControl, ICommand?>(nameof(CopySnippetCommand));

    public static readonly StyledProperty<ICommand?> SaveSnippetAsCommandProperty =
        AvaloniaProperty.Register<ChatTranscriptRenderControl, ICommand?>(nameof(SaveSnippetAsCommand));

    public static readonly StyledProperty<ICommand?> UseSnippetForCcCommandProperty =
        AvaloniaProperty.Register<ChatTranscriptRenderControl, ICommand?>(nameof(UseSnippetForCcCommand));

    public static readonly StyledProperty<ICommand?> PreviewSnippetCommandProperty =
        AvaloniaProperty.Register<ChatTranscriptRenderControl, ICommand?>(nameof(PreviewSnippetCommand));

    public static readonly StyledProperty<ICommand?> ToggleThinkingCommandProperty =
        AvaloniaProperty.Register<ChatTranscriptRenderControl, ICommand?>(nameof(ToggleThinkingCommand));

    public static readonly StyledProperty<string> ThemeKeyProperty =
        AvaloniaProperty.Register<ChatTranscriptRenderControl, string>(nameof(ThemeKey), "empty");

    public static readonly StyledProperty<string> UiFontFamilyProperty =
        AvaloniaProperty.Register<ChatTranscriptRenderControl, string>(nameof(UiFontFamily), "fonts:Inter, Segoe UI");

    public static readonly StyledProperty<string> CodeFontFamilyProperty =
        AvaloniaProperty.Register<ChatTranscriptRenderControl, string>(nameof(CodeFontFamily), "avares://ContextControl.Workbench/Assets/Fonts#Cascadia Code, Consolas");

    private const double ContentTop = 2.0;
    private const double ContentBottom = 8.0;
    private const double HorizontalInset = 2.0;
    private const double MessageGap = 6.0;
    private const double CardPaddingX = 7.0;
    private const double CardPaddingY = 5.0;
    private const double HeaderHeight = 27.0;
    private const double TextFontSize = 10.0;
    private const double TextLineHeight = 13.5;
    private const double CodeFontSize = 10.5;
    private const double CodeLineHeight = 15.0;
    private const double MetaFontSize = 9.0;
    private const double ButtonHeight = 18.0;
    private const double AttachmentHeight = 18.0;
    private const double AttachmentGap = 5.0;
    private const double ImagePreviewMaxHeight = 320.0;
    private const double ImagePreviewFallbackHeight = 220.0;
    private const double ImagePreviewInset = 4.0;
    private const double SnippetHeaderHeight = 22.0;
    private const double SnippetPadding = 4.0;
    private const double ViewportOverscan = 96.0;
    private const double EstimatedMessageHeight = 96.0;
    private const int MaxTextCacheEntries = 8192;

    private static readonly FontFamily DefaultCodeFontFamily = new("Consolas");
    private static readonly FontFamily DefaultUiFontFamily = new("Segoe UI");
    private static readonly Cursor HandCursor = new(StandardCursorType.Hand);
    private static readonly Cursor ArrowCursor = new(StandardCursorType.Arrow);
    private static readonly IBrush TransparentBrush = Brushes.Transparent;
    private static readonly IBrush PanelBorderFallbackBrush = Brush.Parse("#D8D3C7");
    private static readonly IBrush CommandBackgroundFallbackBrush = Brush.Parse("#EEE9DE");
    private static readonly IBrush CommandBorderFallbackBrush = Brush.Parse("#C9D0D2");
    private static readonly IBrush CommandPrimaryBackgroundFallbackBrush = Brush.Parse("#DDF1E7");
    private static readonly IBrush HistoryHoverFallbackBrush = Brush.Parse("#E8ECE8");
    private static readonly IBrush HistoryActiveFallbackBrush = Brush.Parse("#DDE7E6");
    private static readonly IBrush AccentFallbackBrush = Brush.Parse("#227A58");
    private static readonly IBrush AccentBorderFallbackBrush = Brush.Parse("#79BDA0");
    private static readonly IBrush EditorSurfaceFallbackBrush = Brush.Parse("#F7F3EA");
    private static readonly IBrush TextPrimaryFallbackBrush = Brush.Parse("#26383D");
    private static readonly IBrush TextMutedFallbackBrush = Brush.Parse("#6F7F85");
    private static readonly IBrush TextSelectionFallbackBrush = new SolidColorBrush(Color.FromArgb(86, 34, 122, 88));

    private readonly Dictionary<INotifyPropertyChanged, int> _subscribedItems = [];
    private readonly Dictionary<TextCacheKey, FormattedText> _textCache = new();
    private readonly Dictionary<string, ImageCacheEntry> _imageCache = [];
    private readonly HeightDeltaIndex _heightDeltas = new();
    private readonly Dictionary<int, MessageLayout> _layoutCache = [];
    private readonly Dictionary<int, double> _rowHeights = [];
    private int _itemCount;
    private double _layoutWidth = -1.0;
    private INotifyCollectionChanged? _itemsCollectionChanged;
    private ScrollViewer? _scrollViewer;
    private IDisposable? _offsetSubscription;
    private IDisposable? _viewportSubscription;
    private HitRegion? _hoveredHit;
    private TextPosition? _selectionAnchor;
    private TextPosition? _selectionActive;
    private bool _isSelectingText;
    private bool _measureInvalidationQueued;

    public IReadOnlyList<LocalLlmChatMessageViewModel>? Items
    {
        get => GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    public ICommand? OpenAttachmentCommand
    {
        get => GetValue(OpenAttachmentCommandProperty);
        set => SetValue(OpenAttachmentCommandProperty, value);
    }

    public ICommand? CopyChatTextCommand
    {
        get => GetValue(CopyChatTextCommandProperty);
        set => SetValue(CopyChatTextCommandProperty, value);
    }

    public ICommand? CreateProjectFromMessageCommand
    {
        get => GetValue(CreateProjectFromMessageCommandProperty);
        set => SetValue(CreateProjectFromMessageCommandProperty, value);
    }

    public ICommand? ToggleSnippetCommand
    {
        get => GetValue(ToggleSnippetCommandProperty);
        set => SetValue(ToggleSnippetCommandProperty, value);
    }

    public ICommand? CopySnippetCommand
    {
        get => GetValue(CopySnippetCommandProperty);
        set => SetValue(CopySnippetCommandProperty, value);
    }

    public ICommand? SaveSnippetAsCommand
    {
        get => GetValue(SaveSnippetAsCommandProperty);
        set => SetValue(SaveSnippetAsCommandProperty, value);
    }

    public ICommand? UseSnippetForCcCommand
    {
        get => GetValue(UseSnippetForCcCommandProperty);
        set => SetValue(UseSnippetForCcCommandProperty, value);
    }

    public ICommand? PreviewSnippetCommand
    {
        get => GetValue(PreviewSnippetCommandProperty);
        set => SetValue(PreviewSnippetCommandProperty, value);
    }

    public ICommand? ToggleThinkingCommand
    {
        get => GetValue(ToggleThinkingCommandProperty);
        set => SetValue(ToggleThinkingCommandProperty, value);
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

    public ChatTranscriptRenderControl()
    {
        Focusable = true;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        AttachItems();
        ResetLayoutCache(ResolveLayoutWidth(Bounds.Width), Items?.Count ?? 0);
        InvalidateTranscript();
        AttachToScrollViewer(this.GetVisualAncestors().OfType<ScrollViewer>().FirstOrDefault());
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        AttachToScrollViewer(null);
        DetachItems();
        ClearImageCache();
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ItemsProperty)
        {
            AttachItems();
            ResetLayoutCache(ResolveLayoutWidth(Bounds.Width), Items?.Count ?? 0);
            InvalidateTranscript();
        }
        else if (change.Property == ThemeKeyProperty
            || change.Property == UiFontFamilyProperty
            || change.Property == CodeFontFamilyProperty)
        {
            _textCache.Clear();
            ResetLayoutCache(ResolveLayoutWidth(Bounds.Width), Items?.Count ?? 0);
            InvalidateTranscript();
        }
        else if (change.Property == OpenAttachmentCommandProperty
            || change.Property == CopyChatTextCommandProperty
            || change.Property == CreateProjectFromMessageCommandProperty
            || change.Property == ToggleSnippetCommandProperty
            || change.Property == CopySnippetCommandProperty
            || change.Property == SaveSnippetAsCommandProperty
            || change.Property == UseSnippetForCcCommandProperty
            || change.Property == PreviewSnippetCommandProperty
            || change.Property == ToggleThinkingCommandProperty)
        {
            InvalidateVisual();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled)
        {
            return;
        }

        if (e.Key == Key.Escape && HasTextSelection)
        {
            ClearTextSelection();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.C
            && e.KeyModifiers.HasFlag(KeyModifiers.Control)
            && HasTextSelection)
        {
            _ = CopySelectedTextAsync();
            e.Handled = true;
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = ResolveLayoutWidth(availableSize.Width);
        EnsureLayoutCache(width);
        return new Size(Math.Max(1.0, width), GetTotalHeight());
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        EnsureLayoutCache(Bounds.Width);

        var viewportTop = Math.Max(0.0, _scrollViewer?.Offset.Y ?? 0.0);
        var viewportHeight = _scrollViewer?.Viewport.Height ?? Math.Min(Bounds.Height, 900.0);
        if (!double.IsFinite(viewportHeight) || viewportHeight <= 0.0)
        {
            viewportHeight = Math.Min(Bounds.Height, 900.0);
        }

        context.DrawRectangle(TransparentBrush, null, new Rect(0, viewportTop, Bounds.Width, viewportHeight));

        var items = Items;
        if (items is null || _itemCount == 0)
        {
            DrawEmptyState(context, viewportTop, viewportHeight);
            return;
        }

        var startY = Math.Max(0.0, viewportTop - ViewportOverscan);
        var endY = Math.Min(GetTotalHeight(), viewportTop + viewportHeight + ViewportOverscan);
        var startIndex = FindRowIndexAtOrAfter(startY);
        if (startIndex < 0)
        {
            return;
        }

        var rowTop = GetRowTop(startIndex);
        for (var index = startIndex; index < _itemCount; index++)
        {
            if (rowTop > endY)
            {
                break;
            }

            var layout = GetOrBuildLayout(index);
            if (layout is null)
            {
                break;
            }

            var rowBottom = rowTop + GetRowHeight(index);
            if (rowBottom < startY)
            {
                rowTop = rowBottom;
                continue;
            }

            DrawMessage(context, layout, index, rowTop, viewportTop, viewportTop + viewportHeight);
            rowTop = rowBottom;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var point = e.GetPosition(this);
        if (_isSelectingText)
        {
            if (TryGetTextPosition(point, out var position))
            {
                _selectionActive = position;
                InvalidateVisual();
            }

            e.Handled = true;
            return;
        }

        var hit = HitTest(point);
        SetHoveredHit(hit);
        if (hit is null && TryGetTextPosition(point, out _))
        {
            Cursor = new Cursor(StandardCursorType.Ibeam);
        }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (_isSelectingText)
        {
            return;
        }

        SetHoveredHit(null);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        Focus();
        var point = e.GetPosition(this);
        var hit = HitTest(point);
        if (hit is not null)
        {
            ExecuteHit(hit);
            e.Handled = true;
            return;
        }

        if (TryGetTextPosition(point, out var position))
        {
            _selectionAnchor = position;
            _selectionActive = position;
            _isSelectingText = true;
            e.Pointer.Capture(this);
            Cursor = new Cursor(StandardCursorType.Ibeam);
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        ClearTextSelection();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_isSelectingText)
        {
            _isSelectingText = false;
            e.Pointer.Capture(null);
            e.Handled = true;
        }
    }

    private void AttachItems()
    {
        DetachItems();
        if (Items is INotifyCollectionChanged collectionChanged)
        {
            _itemsCollectionChanged = collectionChanged;
            collectionChanged.CollectionChanged += OnItemsCollectionChanged;
        }
    }

    private void DetachItems()
    {
        if (_itemsCollectionChanged is not null)
        {
            _itemsCollectionChanged.CollectionChanged -= OnItemsCollectionChanged;
            _itemsCollectionChanged = null;
        }

        foreach (var item in _subscribedItems.Keys)
        {
            item.PropertyChanged -= OnItemPropertyChanged;
        }

        _subscribedItems.Clear();
    }

    private void SubscribeMessage(LocalLlmChatMessageViewModel message, int rowIndex)
    {
        Subscribe(message, rowIndex);
        foreach (var snippet in message.Snippets)
        {
            Subscribe(snippet, rowIndex);
        }
    }

    private void Subscribe(INotifyPropertyChanged item, int rowIndex)
    {
        if (_subscribedItems.ContainsKey(item))
        {
            _subscribedItems[item] = rowIndex;
            return;
        }

        item.PropertyChanged += OnItemPropertyChanged;
        _subscribedItems[item] = rowIndex;
    }

    private void OnItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        var newCount = Items?.Count ?? 0;
        if (e.Action == NotifyCollectionChangedAction.Add
            && e.NewItems is not null
            && e.NewStartingIndex == _itemCount)
        {
            ResizeLayoutCache(newCount);
            InvalidateTranscript();
            return;
        }

        DetachItems();
        if (Items is INotifyCollectionChanged collectionChanged)
        {
            _itemsCollectionChanged = collectionChanged;
            collectionChanged.CollectionChanged += OnItemsCollectionChanged;
        }

        ResetLayoutCache(ResolveLayoutWidth(Bounds.Width), newCount);
        InvalidateTranscript();
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is LocalLlmChatMessageViewModel message && _subscribedItems.TryGetValue(message, out var messageIndex))
        {
            foreach (var snippet in message.Snippets)
            {
                Subscribe(snippet, messageIndex);
            }
        }

        if (sender is INotifyPropertyChanged item && _subscribedItems.TryGetValue(item, out var rowIndex))
        {
            InvalidateRow(rowIndex);
            return;
        }

        ResetLayoutCache(ResolveLayoutWidth(Bounds.Width), Items?.Count ?? 0);
        InvalidateTranscript();
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
            .Subscribe(new ValueObserver<Size>(OnScrollViewerViewportChanged));
    }

    private void OnScrollViewerViewportChanged(Size viewport)
    {
        var width = double.IsFinite(viewport.Width) && viewport.Width > 0.0
            ? viewport.Width
            : ResolveLayoutWidth(Bounds.Width);
        if (Math.Abs(width - _layoutWidth) >= 0.5)
        {
            ResetLayoutCache(width, Items?.Count ?? 0);
            QueueMeasureInvalidation();
        }

        InvalidateVisual();
    }

    private void InvalidateTranscript()
    {
        _hoveredHit = null;
        QueueMeasureInvalidation();
        InvalidateVisual();
    }

    private void InvalidateRow(int index)
    {
        if (index < 0 || index >= _itemCount)
        {
            ResetLayoutCache(ResolveLayoutWidth(Bounds.Width), Items?.Count ?? 0);
            InvalidateTranscript();
            return;
        }

        _hoveredHit = null;
        _layoutCache.Remove(index);
        if (_rowHeights.Remove(index))
        {
            _heightDeltas.SetDelta(index, 0.0);
            QueueMeasureInvalidation();
        }

        InvalidateVisual();
    }

    private void EnsureLayoutCache(double width)
    {
        width = ResolveLayoutWidth(width);
        var count = Items?.Count ?? 0;
        if (Math.Abs(width - _layoutWidth) < 0.5 && count == _itemCount)
        {
            return;
        }

        ResetLayoutCache(width, count);
    }

    private void ResetLayoutCache(double width, int count)
    {
        _layoutWidth = Math.Max(1.0, width);
        _itemCount = Math.Max(0, count);
        _layoutCache.Clear();
        _rowHeights.Clear();
        _heightDeltas.Reset(_itemCount);
        _hoveredHit = null;
        ClearTextSelection();
    }

    private void ResizeLayoutCache(int count)
    {
        count = Math.Max(0, count);
        if (count == _itemCount)
        {
            return;
        }

        if (count < _itemCount)
        {
            ResetLayoutCache(ResolveLayoutWidth(Bounds.Width), count);
            return;
        }

        _heightDeltas.Resize(count);
        _itemCount = count;
        _hoveredHit = null;
    }

    private MessageLayout? GetOrBuildLayout(int index)
    {
        var items = Items;
        if (items is null || index < 0 || index >= _itemCount || index >= items.Count)
        {
            return null;
        }

        if (_layoutCache.TryGetValue(index, out var layout))
        {
            return layout;
        }

        SubscribeMessage(items[index], index);
        layout = BuildMessageLayout(items[index], _layoutWidth);
        _layoutCache[index] = layout;
        SetMeasuredHeight(index, layout.Height);
        return layout;
    }

    private void SetMeasuredHeight(int index, double height)
    {
        if (index < 0 || index >= _itemCount || !double.IsFinite(height) || height <= 0.0)
        {
            return;
        }

        var wasMeasured = _rowHeights.TryGetValue(index, out var measuredHeight);
        var previousHeight = wasMeasured ? measuredHeight : EstimatedMessageHeight;
        if (Math.Abs(previousHeight - height) < 0.5 && wasMeasured)
        {
            return;
        }

        _rowHeights[index] = height;
        _heightDeltas.SetDelta(index, height - EstimatedMessageHeight);
        if (Math.Abs(previousHeight - height) >= 0.5)
        {
            QueueMeasureInvalidation();
        }
    }

    private void QueueMeasureInvalidation()
    {
        if (_measureInvalidationQueued)
        {
            return;
        }

        _measureInvalidationQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _measureInvalidationQueued = false;
            InvalidateMeasure();
        }, DispatcherPriority.Background);
    }

    private double GetRowTop(int index)
    {
        index = Math.Clamp(index, 0, _itemCount);
        return ContentTop + (index * EstimatedMessageHeight) + _heightDeltas.PrefixSum(index);
    }

    private double GetRowHeight(int index)
    {
        return _rowHeights.TryGetValue(index, out var height) && height > 0.0
            ? height
            : EstimatedMessageHeight;
    }

    private double GetTotalHeight()
    {
        return ContentTop + ContentBottom + (_itemCount * EstimatedMessageHeight) + _heightDeltas.PrefixSum(_itemCount);
    }

    private double ResolveLayoutWidth(double width)
    {
        var viewportWidth = _scrollViewer?.Viewport.Width ?? 0.0;
        if (double.IsFinite(viewportWidth) && viewportWidth > 0.0)
        {
            return viewportWidth;
        }

        if (double.IsFinite(width) && width > 0.0)
        {
            return width;
        }

        if (double.IsFinite(Bounds.Width) && Bounds.Width > 0.0)
        {
            return Bounds.Width;
        }

        return double.IsFinite(_layoutWidth) && _layoutWidth > 0.0
            ? _layoutWidth
            : 1.0;
    }

    private MessageLayout BuildMessageLayout(LocalLlmChatMessageViewModel message, double width)
    {
        var layout = new MessageLayout(message);
        var cardWidth = Math.Max(0.0, width - HorizontalInset * 2.0 - 2.0);
        var card = new Rect(HorizontalInset, 0, cardWidth, 1.0);
        var contentLeft = card.X + CardPaddingX;
        var contentWidth = Math.Max(1.0, card.Width - CardPaddingX * 2.0);
        var y = CardPaddingY;

        BuildHeader(layout, message, card, y, contentWidth);
        y += HeaderHeight;

        var attachmentsAfterText = ShouldShowAttachmentsAfterText(message);
        if (message.HasAttachments && !attachmentsAfterText)
        {
            y = BuildAttachments(layout, message, contentLeft, y, contentWidth);
            y += 4.0;
        }

        var uiFontFamily = ResolveFontFamily(UiFontFamily, Resource("UiFontFamily", DefaultUiFontFamily));
        foreach (var part in message.Parts)
        {
            if (part.IsText)
            {
                var text = NormalizeText(part.Text);
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                var lines = WrapLines(text, contentWidth, uiFontFamily, FontWeight.Normal, FontStyle.Normal, TextFontSize);
                var height = Math.Max(TextLineHeight, lines.Count * TextLineHeight);
                layout.TextBlocks.Add(new TextBlockLayout(
                    new Rect(contentLeft, y, contentWidth, height),
                    lines,
                    TextFontSize,
                    TextLineHeight,
                    FontWeight.Normal,
                    FontStyle.Normal,
                    false,
                    Resource("TextPrimaryBrush", TextPrimaryFallbackBrush)));
                y += height + 6.0;
            }
            else if (part.Snippet is { } snippet)
            {
                y = BuildSnippet(layout, snippet, contentLeft, y, contentWidth);
            }
        }

        if (message.HasAttachments && attachmentsAfterText)
        {
            y = BuildAttachments(layout, message, contentLeft, y, contentWidth);
            y += 4.0;
        }

        if (message.HasThinking)
        {
            y = BuildThinking(layout, message, contentLeft, y, contentWidth);
        }

        layout.CardRect = new Rect(card.X, 0, card.Width, Math.Max(34.0, y + CardPaddingY));
        layout.Height = layout.CardRect.Height + MessageGap;
        return layout;
    }

    private static bool ShouldShowAttachmentsAfterText(LocalLlmChatMessageViewModel message)
    {
        return false;
    }

    private void BuildHeader(MessageLayout layout, LocalLlmChatMessageViewModel message, Rect card, double y, double contentWidth)
    {
        var right = card.Right - CardPaddingX;
        var top = y + 1.0;
        var timeWidth = 36.0;
        right -= timeWidth;

        if (message.PrimaryTextPart is { IsText: true } primaryTextPart
            && !string.IsNullOrWhiteSpace(primaryTextPart.Text))
        {
            var copyRect = new Rect(right - 26.0, top, 24.0, 20.0);
            layout.Hits.Add(new HitRegion(copyRect, ChatTranscriptHitKind.CopyMessage, primaryTextPart));
            right = copyRect.X - 4.0;
        }

        var imagePath = message.AttachedFiles.FirstOrDefault(IsInlineImageAttachment)?.Path;
        if (!string.IsNullOrWhiteSpace(imagePath))
        {
            var downloadRect = new Rect(right - 26.0, top, 24.0, 20.0);
            layout.Hits.Add(new HitRegion(downloadRect, ChatTranscriptHitKind.DownloadAttachment, imagePath));
            right = downloadRect.X - 4.0;
        }

        if (message.CanCreateProject)
        {
            var createRect = new Rect(Math.Max(card.X + CardPaddingX, right - 88.0), top, Math.Min(88.0, Math.Max(0.0, right - card.X - CardPaddingX)), 20.0);
            if (createRect.Width >= 58.0)
            {
                layout.Hits.Add(new HitRegion(createRect, ChatTranscriptHitKind.CreateProject, message));
                right = createRect.X - 4.0;
            }
        }

        layout.HeaderMetaClip = new Rect(card.X + CardPaddingX, y, Math.Max(0.0, right - card.X - CardPaddingX), HeaderHeight);
        layout.TimeRect = new Rect(card.Right - CardPaddingX - timeWidth, top + 1.0, timeWidth, 16.0);
    }

    private double BuildAttachments(
        MessageLayout layout,
        LocalLlmChatMessageViewModel message,
        double x,
        double y,
        double contentWidth)
    {
        var codeFontFamily = ResolveFontFamily(CodeFontFamily, Resource("CodeFontFamily", DefaultCodeFontFamily));
        var left = x;
        var maxRight = x + contentWidth;
        var rowY = y;
        var bottom = y;
        foreach (var attachment in message.AttachedFiles)
        {
            if (IsInlineImageAttachment(attachment))
            {
                if (left > x)
                {
                    bottom = Math.Max(bottom, rowY + AttachmentHeight);
                    rowY = bottom + AttachmentGap;
                    left = x;
                }

                var imageRect = BuildImagePreviewRect(attachment.Path, x, rowY, contentWidth);
                layout.Attachments.Add(new AttachmentLayout(imageRect, "Generated image", attachment, true));
                layout.Hits.Add(new HitRegion(imageRect, ChatTranscriptHitKind.OpenImagePreview, attachment.Path));
                bottom = imageRect.Bottom;
                rowY = bottom + AttachmentGap;
                left = x;
                continue;
            }

            var title = CleanOneLine(attachment.DisplayTitle, 80);
            var text = GetFormattedText(title, Resource("TextMutedBrush", TextMutedFallbackBrush), codeFontFamily, FontWeight.Bold, FontStyle.Normal, 9.0);
            var chipWidth = Math.Clamp(text.Width + 14.0, 42.0, Math.Min(180.0, contentWidth));
            if (left > x && left + chipWidth > maxRight)
            {
                bottom = Math.Max(bottom, rowY + AttachmentHeight);
                left = x;
                rowY = bottom + AttachmentGap;
            }

            var rect = new Rect(left, rowY, chipWidth, AttachmentHeight);
            layout.Attachments.Add(new AttachmentLayout(rect, title, attachment, false));
            layout.Hits.Add(new HitRegion(rect, ChatTranscriptHitKind.OpenAttachment, attachment.Path));
            left += chipWidth + AttachmentGap;
            bottom = Math.Max(bottom, rect.Bottom);
        }

        return bottom;
    }

    private double BuildSnippet(MessageLayout layout, ChatSnippetViewModel snippet, double x, double y, double contentWidth)
    {
        var header = new Rect(x, y, contentWidth, SnippetHeaderHeight);
        var codeHeight = Math.Clamp(snippet.DisplayHeight, 28.0, 260.0);
        var codeRect = new Rect(x, y + SnippetHeaderHeight + 4.0, contentWidth, codeHeight);
        var codeLines = SplitLines(snippet.DisplayText);
        var snippetLayout = new SnippetLayout(snippet, header, codeRect, codeLines);

        var right = header.Right - SnippetPadding;
        AddSnippetButton(layout, snippetLayout, ChatTranscriptHitKind.PreviewSnippet, "GO", PreviewSnippetCommand, snippet, snippet.IsPatch, ref right);
        AddSnippetButton(layout, snippetLayout, ChatTranscriptHitKind.UseSnippet, snippet.ActionLabel, UseSnippetForCcCommand, snippet, snippet.HasPromptAction, ref right);
        AddSnippetButton(layout, snippetLayout, ChatTranscriptHitKind.SaveSnippet, "Save as", SaveSnippetAsCommand, snippet, snippet.CanSaveAsFile, ref right);
        AddSnippetButton(layout, snippetLayout, ChatTranscriptHitKind.CopySnippet, "Copy", CopySnippetCommand, snippet, true, ref right);
        AddSnippetButton(layout, snippetLayout, ChatTranscriptHitKind.ToggleSnippet, snippet.ToggleLabel, ToggleSnippetCommand, snippet, true, ref right);
        snippetLayout.MetaClip = new Rect(header.X + 52.0, header.Y, Math.Max(0.0, right - header.X - 56.0), header.Height);

        layout.Snippets.Add(snippetLayout);
        return codeRect.Bottom + 7.0;
    }

    private void AddSnippetButton(
        MessageLayout layout,
        SnippetLayout snippetLayout,
        ChatTranscriptHitKind kind,
        string label,
        ICommand? command,
        ChatSnippetViewModel snippet,
        bool isVisible,
        ref double right)
    {
        if (!isVisible)
        {
            return;
        }

        var width = Math.Clamp(label.Length * 6.4 + 14.0, 30.0, 68.0);
        if (right - width < snippetLayout.HeaderRect.X + 112.0)
        {
            return;
        }

        var rect = new Rect(right - width, snippetLayout.HeaderRect.Y + 2.0, width, ButtonHeight);
        var hit = new HitRegion(rect, kind, snippet);
        layout.Hits.Add(hit);
        snippetLayout.Buttons.Insert(0, new ButtonLayout(rect, label, command?.CanExecute(snippet) == true, hit));
        right = rect.X - 3.0;
    }

    private double BuildThinking(MessageLayout layout, LocalLlmChatMessageViewModel message, double x, double y, double contentWidth)
    {
        var buttonRect = new Rect(x, y, 70.0, ButtonHeight);
        var hit = new HitRegion(buttonRect, ChatTranscriptHitKind.ToggleThinking, message);
        layout.Hits.Add(hit);
        var thinking = new ThinkingLayout(buttonRect, hit);
        y += ButtonHeight + 5.0;

        if (message.IsThinkingExpanded)
        {
            var codeFontFamily = ResolveFontFamily(CodeFontFamily, Resource("CodeFontFamily", DefaultCodeFontFamily));
            var lines = WrapLines(NormalizeText(message.ThinkingText), contentWidth - 12.0, codeFontFamily, FontWeight.Normal, FontStyle.Normal, CodeFontSize);
            var height = Math.Clamp(12.0 + lines.Count * CodeLineHeight, 76.0, 260.0);
            thinking.TextBlock = new TextBlockLayout(
                new Rect(x, y, contentWidth, height),
                lines,
                CodeFontSize,
                CodeLineHeight,
                FontWeight.Normal,
                FontStyle.Normal,
                true,
                Resource("TextPrimaryBrush", TextPrimaryFallbackBrush));
            y += height + 6.0;
        }

        layout.Thinking = thinking;
        return y;
    }

    private void DrawMessage(
        DrawingContext context,
        MessageLayout layout,
        int index,
        double rowTop,
        double viewportTop,
        double viewportBottom)
    {
        var message = layout.Message;
        var card = OffsetY(layout.CardRect, rowTop);
        var background = message.IsUser
            ? Resource("CommandPrimaryBackgroundBrush", CommandPrimaryBackgroundFallbackBrush)
            : Resource("CommandBackgroundBrush", CommandBackgroundFallbackBrush);
        var border = message.IsUser
            ? Resource("AccentBorderBrush", AccentBorderFallbackBrush)
            : Resource("PanelBorderBrush", PanelBorderFallbackBrush);
        context.DrawRectangle(background, new Pen(border, 1), card, 5, 5);

        DrawHeader(context, layout, message, rowTop);

        foreach (var attachment in layout.Attachments)
        {
            DrawAttachment(context, attachment, rowTop);
        }

        for (var blockIndex = 0; blockIndex < layout.TextBlocks.Count; blockIndex++)
        {
            var text = layout.TextBlocks[blockIndex];
            DrawTextSelection(context, text, index, blockIndex, rowTop, viewportTop, viewportBottom);
            DrawTextBlock(context, text, rowTop, viewportTop, viewportBottom);
        }

        foreach (var snippet in layout.Snippets)
        {
            DrawSnippet(context, snippet, rowTop, viewportTop, viewportBottom);
        }

        if (layout.Thinking is not null)
        {
            DrawThinking(context, layout.Thinking, rowTop, viewportTop, viewportBottom);
        }
    }

    private void DrawHeader(DrawingContext context, MessageLayout layout, LocalLlmChatMessageViewModel message, double rowTop)
    {
        var uiFontFamily = ResolveFontFamily(UiFontFamily, Resource("UiFontFamily", DefaultUiFontFamily));
        var codeFontFamily = ResolveFontFamily(CodeFontFamily, Resource("CodeFontFamily", DefaultCodeFontFamily));
        var accent = Resource("AccentBrush", AccentFallbackBrush);
        var muted = Resource("TextMutedBrush", TextMutedFallbackBrush);

        var role = GetFormattedText(message.RoleLabel, accent, codeFontFamily, FontWeight.Black, FontStyle.Normal, 9.0);
        var meta = GetFormattedText(CleanOneLine(message.MetaLabel, 180), muted, uiFontFamily, FontWeight.SemiBold, FontStyle.Normal, MetaFontSize);
        var clip = OffsetY(layout.HeaderMetaClip, rowTop);
        DrawClippedText(context, role, new Rect(clip.X, clip.Y + 1.0, clip.Width, 12.0), new Point(clip.X, clip.Y + 1.0));
        DrawClippedText(context, meta, new Rect(clip.X, clip.Y + 14.0, clip.Width, 11.0), new Point(clip.X, clip.Y + 14.0));

        var time = GetFormattedText(message.Time, muted, codeFontFamily, FontWeight.Bold, FontStyle.Normal, 8.5);
        DrawClippedText(context, time, OffsetY(layout.TimeRect, rowTop), new Point(layout.TimeRect.X, rowTop + layout.TimeRect.Y));

        foreach (var hit in layout.Hits.Where(hit => hit.Kind is ChatTranscriptHitKind.CopyMessage or ChatTranscriptHitKind.CreateProject or ChatTranscriptHitKind.DownloadAttachment))
        {
            if (hit.Kind == ChatTranscriptHitKind.CopyMessage)
            {
                DrawCopyIconButton(context, OffsetY(hit.Rect, rowTop), CanExecuteHit(hit), ReferenceEquals(hit, _hoveredHit));
            }
            else if (hit.Kind == ChatTranscriptHitKind.DownloadAttachment)
            {
                DrawDownloadIconButton(context, OffsetY(hit.Rect, rowTop), CanExecuteHit(hit), ReferenceEquals(hit, _hoveredHit));
            }
            else
            {
                DrawButton(context, OffsetY(hit.Rect, rowTop), "Create Project", CanExecuteHit(hit), ReferenceEquals(hit, _hoveredHit));
            }
        }
    }

    private void DrawAttachment(DrawingContext context, AttachmentLayout attachment, double rowTop)
    {
        if (attachment.IsImagePreview)
        {
            DrawImageAttachment(context, attachment, rowTop);
            return;
        }

        var rect = OffsetY(attachment.Rect, rowTop);
        context.DrawRectangle(
            Resource("CommandBackgroundBrush", CommandBackgroundFallbackBrush),
            new Pen(Resource("CommandBorderBrush", CommandBorderFallbackBrush), 1),
            rect,
            5,
            5);

        var font = ResolveFontFamily(CodeFontFamily, Resource("CodeFontFamily", DefaultCodeFontFamily));
        var text = GetFormattedText(attachment.Label, Resource("TextMutedBrush", TextMutedFallbackBrush), font, FontWeight.Bold, FontStyle.Normal, 9.0);
        DrawClippedText(context, text, new Rect(rect.X + 6.0, rect.Y, Math.Max(0.0, rect.Width - 12.0), rect.Height), new Point(rect.X + 6.0, rect.Y + 2.0));
    }

    private void DrawImageAttachment(DrawingContext context, AttachmentLayout attachment, double rowTop)
    {
        var rect = OffsetY(attachment.Rect, rowTop);
        var border = Resource("CommandBorderBrush", CommandBorderFallbackBrush);
        context.DrawRectangle(
            Resource("EditorSurfaceBrush", EditorSurfaceFallbackBrush),
            new Pen(border, 1),
            rect,
            5,
            5);

        var imageBounds = new Rect(
            rect.X + ImagePreviewInset,
            rect.Y + ImagePreviewInset,
            Math.Max(0.0, rect.Width - ImagePreviewInset * 2.0),
            Math.Max(0.0, rect.Height - ImagePreviewInset * 2.0));
        var bitmap = TryGetImageBitmap(attachment.Attachment.Path);
        if (bitmap is null || imageBounds.Width <= 0.0 || imageBounds.Height <= 0.0)
        {
            var font = ResolveFontFamily(UiFontFamily, Resource("UiFontFamily", DefaultUiFontFamily));
            var text = GetFormattedText("Image preview unavailable", Resource("TextMutedBrush", TextMutedFallbackBrush), font, FontWeight.SemiBold, FontStyle.Normal, 10.0);
            DrawClippedText(
                context,
                text,
                imageBounds,
                new Point(
                    imageBounds.X + Math.Max(0.0, (imageBounds.Width - text.Width) * 0.5),
                    imageBounds.Y + Math.Max(0.0, (imageBounds.Height - text.Height) * 0.5)));
            return;
        }

        context.DrawImage(bitmap, FitImageRect(bitmap.PixelSize, imageBounds));
    }

    private void DrawSnippet(DrawingContext context, SnippetLayout snippet, double rowTop, double viewportTop, double viewportBottom)
    {
        var codeFontFamily = ResolveFontFamily(CodeFontFamily, Resource("CodeFontFamily", DefaultCodeFontFamily));
        var uiFontFamily = ResolveFontFamily(UiFontFamily, Resource("UiFontFamily", DefaultUiFontFamily));
        var header = OffsetY(snippet.HeaderRect, rowTop);
        var code = OffsetY(snippet.CodeRect, rowTop);

        var shell = new Rect(header.X, header.Y, header.Width, header.Height + 4.0 + code.Height);
        context.DrawRectangle(
            Resource("EditorSurfaceBrush", EditorSurfaceFallbackBrush),
            new Pen(Resource("CommandBorderBrush", CommandBorderFallbackBrush), 1),
            shell,
            5,
            5);

        var typeRect = new Rect(header.X + SnippetPadding, header.Y + 2.0, 44.0, ButtonHeight);
        context.DrawRectangle(
            Resource("CommandPrimaryBackgroundBrush", CommandPrimaryBackgroundFallbackBrush),
            new Pen(Resource("AccentBorderBrush", AccentBorderFallbackBrush), 1),
            typeRect,
            4,
            4);
        var typeText = GetFormattedText(CleanOneLine(snippet.Snippet.TypeLabel, 14), Resource("AccentBrush", AccentFallbackBrush), codeFontFamily, FontWeight.Black, FontStyle.Normal, 8.2);
        DrawClippedText(context, typeText, typeRect, new Point(typeRect.X + Math.Max(3.0, (typeRect.Width - typeText.Width) * 0.5), typeRect.Y + 2.0));

        var meta = GetFormattedText(CleanOneLine(snippet.Snippet.CompactMetaLabel, 120), Resource("TextMutedBrush", TextMutedFallbackBrush), codeFontFamily, FontWeight.SemiBold, FontStyle.Normal, 8.6);
        DrawClippedText(context, meta, OffsetY(snippet.MetaClip, rowTop), new Point(snippet.MetaClip.X, rowTop + snippet.MetaClip.Y + 4.0));

        foreach (var button in snippet.Buttons)
        {
            DrawButton(context, OffsetY(button.Rect, rowTop), button.Label, button.IsEnabled, ReferenceEquals(button.Hit, _hoveredHit));
        }

        context.DrawRectangle(
            Resource("CommandBackgroundBrush", CommandBackgroundFallbackBrush),
            new Pen(Resource("PanelBorderBrush", PanelBorderFallbackBrush), 1),
            code,
            4,
            4);

        var textBlock = new TextBlockLayout(
            new Rect(snippet.CodeRect.X + 6.0, snippet.CodeRect.Y + 4.0, Math.Max(0.0, snippet.CodeRect.Width - 12.0), Math.Max(0.0, snippet.CodeRect.Height - 8.0)),
            snippet.CodeLines,
            CodeFontSize,
            CodeLineHeight,
            FontWeight.Normal,
            FontStyle.Normal,
            true,
            Resource("TextPrimaryBrush", TextPrimaryFallbackBrush));
        DrawTextBlock(context, textBlock, rowTop, viewportTop, viewportBottom, wrap: false);
    }

    private void DrawThinking(DrawingContext context, ThinkingLayout thinking, double rowTop, double viewportTop, double viewportBottom)
    {
        DrawButton(context, OffsetY(thinking.ButtonRect, rowTop), "Thinking", CanExecuteHit(thinking.Hit), ReferenceEquals(thinking.Hit, _hoveredHit));
        if (thinking.TextBlock is null)
        {
            return;
        }

        var rect = OffsetY(thinking.TextBlock.Rect, rowTop);
        context.DrawRectangle(
            Resource("CommandBackgroundBrush", CommandBackgroundFallbackBrush),
            new Pen(Resource("PanelBorderBrush", PanelBorderFallbackBrush), 1),
            rect,
            5,
            5);
        DrawTextBlock(context, thinking.TextBlock with
        {
            Rect = new Rect(thinking.TextBlock.Rect.X + 6.0, thinking.TextBlock.Rect.Y + 6.0, Math.Max(0.0, thinking.TextBlock.Rect.Width - 12.0), Math.Max(0.0, thinking.TextBlock.Rect.Height - 12.0))
        }, rowTop, viewportTop, viewportBottom);
    }

    private void DrawTextBlock(
        DrawingContext context,
        TextBlockLayout block,
        double rowTop,
        double viewportTop,
        double viewportBottom,
        bool wrap = true)
    {
        var rect = OffsetY(block.Rect, rowTop);
        if (rect.Bottom < viewportTop || rect.Y > viewportBottom || rect.Width <= 0.0 || rect.Height <= 0.0)
        {
            return;
        }

        var font = block.UseCodeFont
            ? ResolveFontFamily(CodeFontFamily, Resource("CodeFontFamily", DefaultCodeFontFamily))
            : ResolveFontFamily(UiFontFamily, Resource("UiFontFamily", DefaultUiFontFamily));
        var firstLine = Math.Max(0, (int)Math.Floor((viewportTop - rect.Y) / block.LineHeight) - 1);
        var lastLine = Math.Min(block.Lines.Count - 1, (int)Math.Ceiling((viewportBottom - rect.Y) / block.LineHeight) + 1);
        if (lastLine < firstLine)
        {
            return;
        }

        using (context.PushClip(rect))
        {
            for (var lineIndex = firstLine; lineIndex <= lastLine; lineIndex++)
            {
                var line = block.Lines[lineIndex];
                if (string.IsNullOrEmpty(line))
                {
                    continue;
                }

                var renderedLine = wrap
                    ? line
                    : FitLineToWidth(line, rect.Width, block.Brush, font, block.Weight, block.Style, block.FontSize);
                var text = GetFormattedText(renderedLine, block.Brush, font, block.Weight, block.Style, block.FontSize);
                context.DrawText(text, new Point(rect.X, rect.Y + lineIndex * block.LineHeight));
            }
        }
    }

    private void DrawTextSelection(
        DrawingContext context,
        TextBlockLayout block,
        int rowIndex,
        int blockIndex,
        double rowTop,
        double viewportTop,
        double viewportBottom)
    {
        if (!TryGetSelectionRange(out var start, out var end)
            || rowIndex < start.RowIndex
            || rowIndex > end.RowIndex)
        {
            return;
        }

        var rect = OffsetY(block.Rect, rowTop);
        if (rect.Bottom < viewportTop || rect.Y > viewportBottom || rect.Width <= 0.0 || rect.Height <= 0.0)
        {
            return;
        }

        var font = block.UseCodeFont
            ? ResolveFontFamily(CodeFontFamily, Resource("CodeFontFamily", DefaultCodeFontFamily))
            : ResolveFontFamily(UiFontFamily, Resource("UiFontFamily", DefaultUiFontFamily));
        using (context.PushClip(rect))
        {
            for (var lineIndex = 0; lineIndex < block.Lines.Count; lineIndex++)
            {
                var line = block.Lines[lineIndex];
                var lineStart = new TextPosition(rowIndex, blockIndex, lineIndex, 0);
                var lineEnd = new TextPosition(rowIndex, blockIndex, lineIndex, line.Length);
                if (CompareTextPositions(lineEnd, start) <= 0 || CompareTextPositions(lineStart, end) >= 0)
                {
                    continue;
                }

                var startColumn = CompareTextPositions(start, lineStart) > 0 && start.RowIndex == rowIndex && start.BlockIndex == blockIndex && start.LineIndex == lineIndex
                    ? Math.Clamp(start.Column, 0, line.Length)
                    : 0;
                var endColumn = CompareTextPositions(end, lineEnd) < 0 && end.RowIndex == rowIndex && end.BlockIndex == blockIndex && end.LineIndex == lineIndex
                    ? Math.Clamp(end.Column, 0, line.Length)
                    : line.Length;
                if (endColumn <= startColumn)
                {
                    continue;
                }

                var prefixWidth = MeasureTextWidth(line[..startColumn], font, block.Weight, block.Style, block.FontSize);
                var selectedWidth = MeasureTextWidth(line[startColumn..endColumn], font, block.Weight, block.Style, block.FontSize);
                var highlight = new Rect(
                    rect.X + prefixWidth,
                    rect.Y + lineIndex * block.LineHeight,
                    Math.Max(2.0, selectedWidth),
                    block.LineHeight);
                context.DrawRectangle(TextSelectionFallbackBrush, null, highlight, 2, 2);
            }
        }
    }

    private bool TryGetTextPosition(Point point, out TextPosition position)
    {
        position = default;
        EnsureLayoutCache(Bounds.Width);
        if (_itemCount == 0 || point.X < 0 || point.Y < 0 || point.X > Bounds.Width || point.Y > GetTotalHeight())
        {
            return false;
        }

        var rowIndex = FindRowIndexAtOrAfter(point.Y);
        if (rowIndex < 0 || rowIndex >= _itemCount)
        {
            return false;
        }

        var rowTop = GetRowTop(rowIndex);
        var layout = GetOrBuildLayout(rowIndex);
        if (layout is null)
        {
            return false;
        }

        for (var blockIndex = 0; blockIndex < layout.TextBlocks.Count; blockIndex++)
        {
            var block = layout.TextBlocks[blockIndex];
            var rect = OffsetY(block.Rect, rowTop);
            if (point.Y < rect.Y || point.Y > rect.Bottom || point.X < rect.X - 6.0 || point.X > rect.Right + 6.0)
            {
                continue;
            }

            var lineIndex = Math.Clamp((int)Math.Floor((point.Y - rect.Y) / block.LineHeight), 0, block.Lines.Count - 1);
            var line = block.Lines[lineIndex];
            var column = CalculateColumnAtX(block, line, Math.Max(0.0, point.X - rect.X));
            position = new TextPosition(rowIndex, blockIndex, lineIndex, column);
            return true;
        }

        return false;
    }

    private int CalculateColumnAtX(TextBlockLayout block, string line, double x)
    {
        if (string.IsNullOrEmpty(line) || x <= 0.0)
        {
            return 0;
        }

        var font = block.UseCodeFont
            ? ResolveFontFamily(CodeFontFamily, Resource("CodeFontFamily", DefaultCodeFontFamily))
            : ResolveFontFamily(UiFontFamily, Resource("UiFontFamily", DefaultUiFontFamily));
        var lo = 0;
        var hi = line.Length;
        while (lo < hi)
        {
            var mid = lo + ((hi - lo + 1) / 2);
            if (MeasureTextWidth(line[..mid], font, block.Weight, block.Style, block.FontSize) <= x)
            {
                lo = mid;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return lo;
    }

    private bool HasTextSelection => TryGetSelectionRange(out _, out _);

    private bool TryGetSelectionRange(out TextPosition start, out TextPosition end)
    {
        start = default;
        end = default;
        if (_selectionAnchor is not { } anchor || _selectionActive is not { } active)
        {
            return false;
        }

        if (CompareTextPositions(anchor, active) == 0)
        {
            return false;
        }

        if (CompareTextPositions(anchor, active) <= 0)
        {
            start = anchor;
            end = active;
        }
        else
        {
            start = active;
            end = anchor;
        }

        return true;
    }

    private void ClearTextSelection()
    {
        if (_selectionAnchor is null && _selectionActive is null && !_isSelectingText)
        {
            return;
        }

        _selectionAnchor = null;
        _selectionActive = null;
        _isSelectingText = false;
        InvalidateVisual();
    }

    private async Task CopySelectedTextAsync()
    {
        var selected = BuildSelectedText();
        if (string.IsNullOrWhiteSpace(selected))
        {
            return;
        }

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is not null)
        {
            await clipboard.SetTextAsync(selected);
        }
    }

    private string BuildSelectedText()
    {
        if (!TryGetSelectionRange(out var start, out var end))
        {
            return "";
        }

        EnsureLayoutCache(Bounds.Width);
        var builder = new List<string>();
        for (var rowIndex = start.RowIndex; rowIndex <= end.RowIndex && rowIndex < _itemCount; rowIndex++)
        {
            var layout = GetOrBuildLayout(rowIndex);
            if (layout is null)
            {
                continue;
            }

            for (var blockIndex = 0; blockIndex < layout.TextBlocks.Count; blockIndex++)
            {
                var block = layout.TextBlocks[blockIndex];
                for (var lineIndex = 0; lineIndex < block.Lines.Count; lineIndex++)
                {
                    var line = block.Lines[lineIndex];
                    var lineStart = new TextPosition(rowIndex, blockIndex, lineIndex, 0);
                    var lineEnd = new TextPosition(rowIndex, blockIndex, lineIndex, line.Length);
                    if (CompareTextPositions(lineEnd, start) <= 0 || CompareTextPositions(lineStart, end) >= 0)
                    {
                        continue;
                    }

                    var startColumn = start.RowIndex == rowIndex && start.BlockIndex == blockIndex && start.LineIndex == lineIndex
                        ? Math.Clamp(start.Column, 0, line.Length)
                        : 0;
                    var endColumn = end.RowIndex == rowIndex && end.BlockIndex == blockIndex && end.LineIndex == lineIndex
                        ? Math.Clamp(end.Column, 0, line.Length)
                        : line.Length;
                    if (endColumn > startColumn)
                    {
                        builder.Add(line[startColumn..endColumn]);
                    }
                }
            }
        }

        return string.Join(Environment.NewLine, builder);
    }

    private static int CompareTextPositions(TextPosition left, TextPosition right)
    {
        var row = left.RowIndex.CompareTo(right.RowIndex);
        if (row != 0)
        {
            return row;
        }

        var block = left.BlockIndex.CompareTo(right.BlockIndex);
        if (block != 0)
        {
            return block;
        }

        var line = left.LineIndex.CompareTo(right.LineIndex);
        return line != 0 ? line : left.Column.CompareTo(right.Column);
    }

    private void DrawButton(DrawingContext context, Rect rect, string label, bool isEnabled, bool isHovered)
    {
        var background = isHovered && isEnabled
            ? Resource("HistoryActiveBrush", HistoryActiveFallbackBrush)
            : Resource("CommandBackgroundBrush", CommandBackgroundFallbackBrush);
        var border = isHovered && isEnabled
            ? Resource("AccentBorderBrush", AccentBorderFallbackBrush)
            : Resource("CommandBorderBrush", CommandBorderFallbackBrush);
        var foreground = isEnabled
            ? Resource("TextPrimaryBrush", TextPrimaryFallbackBrush)
            : Resource("TextMutedBrush", TextMutedFallbackBrush);
        context.DrawRectangle(background, new Pen(border, 1), rect, 4, 4);

        var font = ResolveFontFamily(UiFontFamily, Resource("UiFontFamily", DefaultUiFontFamily));
        var text = GetFormattedText(CleanOneLine(label, 24), foreground, font, FontWeight.ExtraBold, FontStyle.Normal, 8.2);
        DrawClippedText(
            context,
            text,
            new Rect(rect.X + 3.0, rect.Y, Math.Max(0.0, rect.Width - 6.0), rect.Height),
            new Point(rect.X + Math.Max(3.0, (rect.Width - text.Width) * 0.5), rect.Y + 2.0));
    }

    private void DrawCopyIconButton(DrawingContext context, Rect rect, bool isEnabled, bool isHovered)
    {
        var background = isHovered && isEnabled
            ? Resource("HistoryActiveBrush", HistoryActiveFallbackBrush)
            : Resource("CommandBackgroundBrush", CommandBackgroundFallbackBrush);
        var border = isHovered && isEnabled
            ? Resource("AccentBorderBrush", AccentBorderFallbackBrush)
            : Resource("CommandBorderBrush", CommandBorderFallbackBrush);
        var foreground = isEnabled
            ? Resource("TextPrimaryBrush", TextPrimaryFallbackBrush)
            : Resource("TextMutedBrush", TextMutedFallbackBrush);

        context.DrawRectangle(background, new Pen(border, 1), rect, 4, 4);

        var x = rect.X + Math.Max(0.0, (rect.Width - 11.0) * 0.5);
        var y = rect.Y + Math.Max(0.0, (rect.Height - 12.0) * 0.5);
        var pen = new Pen(foreground, 1.15);
        context.DrawRectangle(null, pen, new Rect(x, y, 7.5, 8.5), 1.4, 1.4);
        context.DrawRectangle(null, pen, new Rect(x + 3.0, y + 3.0, 7.5, 8.5), 1.4, 1.4);
    }

    private void DrawDownloadIconButton(DrawingContext context, Rect rect, bool isEnabled, bool isHovered)
    {
        var background = isHovered && isEnabled
            ? Resource("HistoryActiveBrush", HistoryActiveFallbackBrush)
            : Resource("CommandBackgroundBrush", CommandBackgroundFallbackBrush);
        var border = isHovered && isEnabled
            ? Resource("AccentBorderBrush", AccentBorderFallbackBrush)
            : Resource("CommandBorderBrush", CommandBorderFallbackBrush);
        var foreground = isEnabled
            ? Resource("TextPrimaryBrush", TextPrimaryFallbackBrush)
            : Resource("TextMutedBrush", TextMutedFallbackBrush);

        context.DrawRectangle(background, new Pen(border, 1), rect, 4, 4);

        var centerX = rect.X + rect.Width * 0.5;
        var top = rect.Y + 4.2;
        var pen = new Pen(foreground, 1.25);
        context.DrawLine(pen, new Point(centerX, top), new Point(centerX, top + 7.0));
        context.DrawLine(pen, new Point(centerX - 3.2, top + 4.2), new Point(centerX, top + 7.4));
        context.DrawLine(pen, new Point(centerX + 3.2, top + 4.2), new Point(centerX, top + 7.4));
        context.DrawLine(pen, new Point(centerX - 4.5, rect.Bottom - 5.0), new Point(centerX + 4.5, rect.Bottom - 5.0));
    }

    private void DrawEmptyState(DrawingContext context, double viewportTop, double viewportHeight)
    {
        var uiFontFamily = ResolveFontFamily(UiFontFamily, Resource("UiFontFamily", DefaultUiFontFamily));
        var text = GetFormattedText("No messages in this chat.", Resource("TextMutedBrush", TextMutedFallbackBrush), uiFontFamily, FontWeight.SemiBold, FontStyle.Normal, 11.0);
        var x = Math.Max(8.0, (Bounds.Width - text.Width) * 0.5);
        var y = viewportTop + Math.Max(24.0, (viewportHeight - text.Height) * 0.35);
        DrawClippedText(context, text, new Rect(0, viewportTop, Bounds.Width, viewportHeight), new Point(x, y));
    }

    private HitRegion? HitTest(Point point)
    {
        EnsureLayoutCache(Bounds.Width);
        if (_itemCount == 0 || point.X < 0 || point.Y < 0 || point.X > Bounds.Width || point.Y > GetTotalHeight())
        {
            return null;
        }

        var index = FindRowIndexAtOrAfter(point.Y);
        if (index < 0 || index >= _itemCount)
        {
            return null;
        }

        var rowTop = GetRowTop(index);
        var layout = GetOrBuildLayout(index);
        if (layout is null)
        {
            return null;
        }

        if (point.Y >= rowTop + GetRowHeight(index) && index + 1 < _itemCount)
        {
            index = FindRowIndexAtOrAfter(point.Y);
            rowTop = GetRowTop(index);
            layout = GetOrBuildLayout(index);
            if (layout is null)
            {
                return null;
            }
        }

        foreach (var hit in layout.Hits)
        {
            if (OffsetY(hit.Rect, rowTop).Contains(point) && CanExecuteHit(hit))
            {
                return hit;
            }
        }

        return null;
    }

    private bool CanExecuteHit(HitRegion hit)
    {
        if (hit.Kind is ChatTranscriptHitKind.OpenImagePreview or ChatTranscriptHitKind.DownloadAttachment)
        {
            return hit.Parameter is string path && File.Exists(path);
        }

        return ResolveCommand(hit.Kind) is { } command && command.CanExecute(hit.Parameter);
    }

    private void ExecuteHit(HitRegion hit)
    {
        if (hit.Kind == ChatTranscriptHitKind.OpenImagePreview && hit.Parameter is string imagePath)
        {
            OpenImagePreviewWindow(imagePath);
            return;
        }

        if (hit.Kind == ChatTranscriptHitKind.DownloadAttachment && hit.Parameter is string downloadPath)
        {
            _ = DownloadImageAsync(downloadPath);
            return;
        }

        var command = ResolveCommand(hit.Kind);
        if (command?.CanExecute(hit.Parameter) == true)
        {
            command.Execute(hit.Parameter);
        }
    }

    private ICommand? ResolveCommand(ChatTranscriptHitKind kind)
    {
        return kind switch
        {
            ChatTranscriptHitKind.OpenAttachment => OpenAttachmentCommand,
            ChatTranscriptHitKind.CopyMessage => CopyChatTextCommand,
            ChatTranscriptHitKind.CreateProject => CreateProjectFromMessageCommand,
            ChatTranscriptHitKind.ToggleSnippet => ToggleSnippetCommand,
            ChatTranscriptHitKind.CopySnippet => CopySnippetCommand,
            ChatTranscriptHitKind.SaveSnippet => SaveSnippetAsCommand,
            ChatTranscriptHitKind.UseSnippet => UseSnippetForCcCommand,
            ChatTranscriptHitKind.PreviewSnippet => PreviewSnippetCommand,
            ChatTranscriptHitKind.ToggleThinking => ToggleThinkingCommand,
            _ => null
        };
    }

    private void SetHoveredHit(HitRegion? hit)
    {
        if (ReferenceEquals(_hoveredHit, hit))
        {
            return;
        }

        _hoveredHit = hit;
        Cursor = hit is null ? ArrowCursor : HandCursor;
        InvalidateVisual();
    }

    private int FindRowIndexAtOrAfter(double y)
    {
        if (_itemCount == 0)
        {
            return -1;
        }

        var lo = 0;
        var hi = _itemCount - 1;
        while (lo < hi)
        {
            var mid = lo + ((hi - lo) / 2);
            if (GetRowTop(mid + 1) <= y)
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

    private IReadOnlyList<string> WrapLines(
        string text,
        double width,
        FontFamily fontFamily,
        FontWeight weight,
        FontStyle style,
        double fontSize)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [""];
        }

        var lines = new List<string>();
        foreach (var rawLine in text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                lines.Add("");
                continue;
            }

            WrapSingleLine(line, width, fontFamily, weight, style, fontSize, lines);
        }

        return lines.Count == 0 ? [""] : lines;
    }

    private void WrapSingleLine(
        string line,
        double width,
        FontFamily fontFamily,
        FontWeight weight,
        FontStyle style,
        double fontSize,
        List<string> lines)
    {
        var words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var current = "";
        foreach (var word in words)
        {
            var candidate = string.IsNullOrEmpty(current) ? word : $"{current} {word}";
            if (MeasureTextWidth(candidate, fontFamily, weight, style, fontSize) <= width)
            {
                current = candidate;
                continue;
            }

            if (!string.IsNullOrEmpty(current))
            {
                lines.Add(current);
                current = "";
            }

            if (MeasureTextWidth(word, fontFamily, weight, style, fontSize) <= width)
            {
                current = word;
            }
            else
            {
                BreakLongWord(word, width, fontFamily, weight, style, fontSize, lines);
            }
        }

        if (!string.IsNullOrEmpty(current))
        {
            lines.Add(current);
        }
    }

    private void BreakLongWord(
        string word,
        double width,
        FontFamily fontFamily,
        FontWeight weight,
        FontStyle style,
        double fontSize,
        List<string> lines)
    {
        var remaining = word;
        while (remaining.Length > 0)
        {
            var lo = 1;
            var hi = remaining.Length;
            var best = 1;
            while (lo <= hi)
            {
                var mid = lo + ((hi - lo) / 2);
                if (MeasureTextWidth(remaining[..mid], fontFamily, weight, style, fontSize) <= width)
                {
                    best = mid;
                    lo = mid + 1;
                }
                else
                {
                    hi = mid - 1;
                }
            }

            lines.Add(remaining[..best]);
            remaining = remaining[best..];
        }
    }

    private double MeasureTextWidth(string text, FontFamily fontFamily, FontWeight weight, FontStyle style, double fontSize)
    {
        return GetFormattedText(text, Resource("TextPrimaryBrush", TextPrimaryFallbackBrush), fontFamily, weight, style, fontSize).Width;
    }

    private string FitLineToWidth(
        string text,
        double width,
        IBrush brush,
        FontFamily fontFamily,
        FontWeight weight,
        FontStyle style,
        double fontSize)
    {
        var clean = text ?? "";
        if (GetFormattedText(clean, brush, fontFamily, weight, style, fontSize).Width <= width)
        {
            return clean;
        }

        while (clean.Length > 4)
        {
            clean = clean[..^4].TrimEnd() + "...";
            if (GetFormattedText(clean, brush, fontFamily, weight, style, fontSize).Width <= width)
            {
                return clean;
            }
        }

        return clean;
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

    private Rect BuildImagePreviewRect(string path, double x, double y, double contentWidth)
    {
        var width = Math.Max(96.0, contentWidth);
        var height = ImagePreviewFallbackHeight;
        if (TryGetImageBitmap(path) is { } bitmap
            && bitmap.PixelSize.Width > 0
            && bitmap.PixelSize.Height > 0)
        {
            height = width * bitmap.PixelSize.Height / bitmap.PixelSize.Width;
            if (height > ImagePreviewMaxHeight)
            {
                var scale = ImagePreviewMaxHeight / height;
                width = Math.Max(96.0, width * scale);
                height = ImagePreviewMaxHeight;
            }
        }

        return new Rect(x, y, Math.Min(contentWidth, width), Math.Clamp(height, 96.0, ImagePreviewMaxHeight));
    }

    private Bitmap? TryGetImageBitmap(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            var fullPath = Path.GetFullPath(path);
            if (!File.Exists(fullPath))
            {
                return null;
            }

            var info = new FileInfo(fullPath);
            if (_imageCache.TryGetValue(fullPath, out var cached)
                && cached.LastWriteUtc == info.LastWriteTimeUtc
                && cached.Length == info.Length)
            {
                return cached.Bitmap;
            }

            if (_imageCache.Count > 64)
            {
                ClearImageCache();
            }

            using var stream = File.OpenRead(fullPath);
            var bitmap = new Bitmap(stream);
            if (_imageCache.TryGetValue(fullPath, out var previous))
            {
                previous.Bitmap.Dispose();
            }

            _imageCache[fullPath] = new ImageCacheEntry(bitmap, info.LastWriteTimeUtc, info.Length);
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private void OpenImagePreviewWindow(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }

        try
        {
            using var stream = File.OpenRead(path);
            var bitmap = new Bitmap(stream);
            var owner = TopLevel.GetTopLevel(this) as Window;
            var image = new Image
            {
                Source = bitmap,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(48)
            };
            var closeButton = new Button
            {
                Content = "x",
                Width = 34,
                Height = 30,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 16, 16, 0)
            };
            var grid = new Grid
            {
                Background = new SolidColorBrush(Color.FromArgb(235, 13, 17, 20))
            };
            grid.Children.Add(image);
            grid.Children.Add(closeButton);

            var window = new Window
            {
                Title = Path.GetFileName(path),
                Content = grid,
                WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
                WindowState = WindowState.FullScreen,
                SystemDecorations = SystemDecorations.None,
                Background = Brushes.Transparent
            };
            closeButton.Click += (_, _) => window.Close();
            window.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    window.Close();
                    e.Handled = true;
                }
            };
            window.Closed += (_, _) => bitmap.Dispose();
            if (owner is null)
            {
                window.Show();
            }
            else
            {
                window.Show(owner);
            }
        }
        catch
        {
            // A failed preview should not disrupt the chat surface.
        }
    }

    private async Task DownloadImageAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }

        try
        {
            var suggestedName = Path.GetFileName(path);
            var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
            if (storage is not null)
            {
                var target = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Download image",
                    SuggestedFileName = suggestedName,
                    FileTypeChoices =
                    [
                        new FilePickerFileType("Image")
                        {
                            Patterns = ["*.png", "*.jpg", "*.jpeg", "*.webp", "*.bmp", "*.gif", "*.tif", "*.tiff"]
                        }
                    ]
                });
                if (target is null)
                {
                    return;
                }

                await using var source = File.OpenRead(path);
                await using var destination = await target.OpenWriteAsync();
                try
                {
                    destination.SetLength(0);
                }
                catch
                {
                    // Some storage-backed streams do not support truncation before writing.
                }

                await source.CopyToAsync(destination);
                return;
            }

            var downloads = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Downloads");
            Directory.CreateDirectory(downloads);
            File.Copy(path, CreateUniqueDownloadPath(Path.Combine(downloads, suggestedName)), overwrite: false);
        }
        catch
        {
            // Download is a convenience action; ignore IO failures here.
        }
    }

    private static string CreateUniqueDownloadPath(string path)
    {
        if (!File.Exists(path))
        {
            return path;
        }

        var directory = Path.GetDirectoryName(path) ?? "";
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        for (var index = 1; index < 10_000; index++)
        {
            var candidate = Path.Combine(directory, $"{name} ({index}){extension}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.Combine(directory, $"{name}-{DateTime.Now:yyyyMMdd-HHmmss}{extension}");
    }

    private void ClearImageCache()
    {
        foreach (var cached in _imageCache.Values)
        {
            cached.Bitmap.Dispose();
        }

        _imageCache.Clear();
    }

    private static bool IsInlineImageAttachment(ContextControlAttachmentViewModel attachment)
    {
        if (!string.Equals(attachment.Kind, "image", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(attachment.Path))
        {
            return false;
        }

        return Path.GetExtension(attachment.Path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".webp" or ".bmp" or ".gif" or ".tif" or ".tiff";
    }

    private static Rect FitImageRect(PixelSize pixelSize, Rect bounds)
    {
        var sourceWidth = Math.Max(1.0, pixelSize.Width);
        var sourceHeight = Math.Max(1.0, pixelSize.Height);
        var scale = Math.Min(bounds.Width / sourceWidth, bounds.Height / sourceHeight);
        if (!double.IsFinite(scale) || scale <= 0.0)
        {
            scale = 1.0;
        }

        var width = Math.Min(bounds.Width, sourceWidth * scale);
        var height = Math.Min(bounds.Height, sourceHeight * scale);
        return new Rect(
            bounds.X + Math.Max(0.0, (bounds.Width - width) * 0.5),
            bounds.Y + Math.Max(0.0, (bounds.Height - height) * 0.5),
            width,
            height);
    }

    private static Rect OffsetY(Rect rect, double y)
    {
        return new Rect(rect.X, rect.Y + y, rect.Width, rect.Height);
    }

    private static void DrawClippedText(DrawingContext context, FormattedText formatted, Rect clip, Point point)
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

    private static string NormalizeText(string? value)
    {
        return (value ?? "")
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Replace("<br />", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("<br/>", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("<br>", "\n", StringComparison.OrdinalIgnoreCase)
            .Trim();
    }

    private static string CleanOneLine(string? value, int maxLength)
    {
        var clean = (value ?? "")
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
        if (clean.Length <= maxLength)
        {
            return clean;
        }

        return clean[..Math.Max(0, maxLength - 1)] + "...";
    }

    private static IReadOnlyList<string> SplitLines(string? value)
    {
        var text = NormalizeText(value);
        return text.Length == 0
            ? [""]
            : text.Split('\n').ToArray();
    }

    private readonly record struct TextCacheKey(
        string Text,
        int BrushId,
        string FontFamily,
        FontWeight Weight,
        FontStyle Style,
        double FontSize);

    private readonly record struct TextPosition(int RowIndex, int BlockIndex, int LineIndex, int Column);

    private sealed class MessageLayout(LocalLlmChatMessageViewModel message)
    {
        public LocalLlmChatMessageViewModel Message { get; } = message;
        public double Height { get; set; }
        public Rect CardRect { get; set; }
        public Rect HeaderMetaClip { get; set; }
        public Rect TimeRect { get; set; }
        public List<TextBlockLayout> TextBlocks { get; } = [];
        public List<AttachmentLayout> Attachments { get; } = [];
        public List<SnippetLayout> Snippets { get; } = [];
        public ThinkingLayout? Thinking { get; set; }
        public List<HitRegion> Hits { get; } = [];
    }

    private sealed record TextBlockLayout(
        Rect Rect,
        IReadOnlyList<string> Lines,
        double FontSize,
        double LineHeight,
        FontWeight Weight,
        FontStyle Style,
        bool UseCodeFont,
        IBrush Brush);

    private sealed record AttachmentLayout(Rect Rect, string Label, ContextControlAttachmentViewModel Attachment, bool IsImagePreview);

    private sealed record ImageCacheEntry(Bitmap Bitmap, DateTime LastWriteUtc, long Length);

    private sealed class SnippetLayout(ChatSnippetViewModel snippet, Rect headerRect, Rect codeRect, IReadOnlyList<string> codeLines)
    {
        public ChatSnippetViewModel Snippet { get; } = snippet;
        public Rect HeaderRect { get; } = headerRect;
        public Rect CodeRect { get; } = codeRect;
        public IReadOnlyList<string> CodeLines { get; } = codeLines;
        public Rect MetaClip { get; set; }
        public List<ButtonLayout> Buttons { get; } = [];
    }

    private sealed record ButtonLayout(Rect Rect, string Label, bool IsEnabled, HitRegion Hit);

    private sealed class ThinkingLayout(Rect buttonRect, HitRegion hit)
    {
        public Rect ButtonRect { get; } = buttonRect;
        public HitRegion Hit { get; } = hit;
        public TextBlockLayout? TextBlock { get; set; }
    }

    private sealed class HitRegion(Rect rect, ChatTranscriptHitKind kind, object? parameter)
    {
        public Rect Rect { get; } = rect;
        public ChatTranscriptHitKind Kind { get; } = kind;
        public object? Parameter { get; } = parameter;
    }

    private sealed class HeightDeltaIndex
    {
        private const int BlockSize = 256;
        private const int BlocksPerGroup = 256;

        private readonly Dictionary<int, double> _deltas = [];
        private readonly Dictionary<int, double> _blockSums = [];
        private readonly Dictionary<int, double> _groupSums = [];
        private int _count;

        public void Reset(int count)
        {
            _count = Math.Max(0, count);
            _deltas.Clear();
            _blockSums.Clear();
            _groupSums.Clear();
        }

        public void Resize(int count)
        {
            count = Math.Max(0, count);
            if (count < _count)
            {
                Reset(count);
                return;
            }

            _count = count;
        }

        public void SetDelta(int index, double delta)
        {
            if (index < 0 || index >= _count)
            {
                return;
            }

            _deltas.TryGetValue(index, out var previousDelta);
            var difference = delta - previousDelta;
            if (Math.Abs(difference) < 0.01)
            {
                return;
            }

            if (Math.Abs(delta) < 0.01)
            {
                _deltas.Remove(index);
            }
            else
            {
                _deltas[index] = delta;
            }

            var block = index / BlockSize;
            AddToSparseSum(_blockSums, block, difference);
            AddToSparseSum(_groupSums, block / BlocksPerGroup, difference);
        }

        public double PrefixSum(int count)
        {
            count = Math.Clamp(count, 0, _count);
            if (count == 0 || _deltas.Count == 0)
            {
                return 0.0;
            }

            var fullBlocks = count / BlockSize;
            var remainingRows = count % BlockSize;
            var fullGroups = fullBlocks / BlocksPerGroup;
            var remainingBlocks = fullBlocks % BlocksPerGroup;

            var sum = 0.0;
            for (var group = 0; group < fullGroups; group++)
            {
                if (_groupSums.TryGetValue(group, out var groupSum))
                {
                    sum += groupSum;
                }
            }

            var blockStart = fullGroups * BlocksPerGroup;
            for (var blockOffset = 0; blockOffset < remainingBlocks; blockOffset++)
            {
                if (_blockSums.TryGetValue(blockStart + blockOffset, out var blockSum))
                {
                    sum += blockSum;
                }
            }

            var rowStart = fullBlocks * BlockSize;
            for (var offset = 0; offset < remainingRows; offset++)
            {
                if (_deltas.TryGetValue(rowStart + offset, out var delta))
                {
                    sum += delta;
                }
            }

            return sum;
        }

        private static void AddToSparseSum(Dictionary<int, double> sums, int key, double delta)
        {
            sums.TryGetValue(key, out var previous);
            var next = previous + delta;
            if (Math.Abs(next) < 0.01)
            {
                sums.Remove(key);
            }
            else
            {
                sums[key] = next;
            }
        }
    }

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
