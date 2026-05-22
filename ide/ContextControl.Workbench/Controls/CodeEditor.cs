using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using TextMateSharp.Grammars;
using TextMateSharp.Registry;

namespace ContextControl.Workbench.Controls;

public sealed class CodeEditor : UserControl
{
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

    private readonly CodeTextSurface _surface;
    private readonly CodeMinimap _minimap;
    private readonly ScrollViewer _scroller;
    private readonly Grid _root;

    public CodeEditor()
    {
        Palette.Use(ThemeKey, SyntaxThemeKey);
        _surface = new CodeTextSurface();
        _minimap = new CodeMinimap
        {
            Width = 92,
            Height = 180,
            Opacity = 0.70,
            IsHitTestVisible = true,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 12, 14, 0)
        };
        _minimap.PointerEntered += (_, _) => _minimap.Opacity = 0.94;
        _minimap.PointerExited += (_, _) => _minimap.Opacity = 0.70;

        _scroller = new ScrollViewer
        {
            Background = Palette.Background,
            Content = _surface,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        _scroller.ScrollChanged += (_, _) => UpdateMinimapViewport();

        _root = new Grid { Background = Palette.Background };
        _root.Children.Add(_scroller);
        _root.Children.Add(_minimap);
        Content = _root;

        ApplyDocument();
        ApplyTheme();
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

    private sealed class CodeTextSurface : Control
    {
        private const double TopPadding = 10;
        private const double BottomPadding = 14;
        private const double GutterWidth = 70;
        private const double TextStart = 78;
        private const double LineHeight = 16;
        private const double FontSize = 12;
        private const double LineNumberFontSize = 11;
        private const double CharWidth = 7.15;
        private const double ArrowCenterX = 55;
        private const double LineNumberRight = 42;

        private readonly RegistryOptions _registryOptions;
        private readonly Registry _registry;
        private readonly HashSet<int> _collapsedStartLines = [];
        private Dictionary<int, FoldRegion> _foldsByStartLine = [];
        private int[][] _bracketColorIndexes = [];
        private readonly Dictionary<int, List<TokenSpan>> _textMateSpanCache = [];
        private readonly Dictionary<int, RenderSegment[]> _lineSegmentCache = [];
        private IReadOnlyDictionary<int, string> _lineChanges = new Dictionary<int, string>();
        private string[] _lines = [""];
        private int[] _visibleLineIndexes = [0];
        private string _extension = "";
        private IGrammar? _grammar;
        private int _maxLineLength;
        private double _viewportOffset;
        private double _viewportHeight = 640;

        public CodeTextSurface()
        {
            _registryOptions = new RegistryOptions(ThemeName.LightPlus);
            _registry = new Registry(_registryOptions);
        }

        public void SetDocument(string text, string path, IReadOnlyDictionary<int, string>? lineChanges)
        {
            _extension = Path.GetExtension(path).ToLowerInvariant();
            _lines = NormalizeLines(text);
            _lineChanges = lineChanges ?? new Dictionary<int, string>();
            _collapsedStartLines.Clear();
            _textMateSpanCache.Clear();
            _lineSegmentCache.Clear();
            ConfigureGrammar();
            _foldsByStartLine = BuildFoldRegions(_lines);
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
            InvalidateVisual();
        }

        public void SetViewport(double offset, double height)
        {
            _viewportOffset = Math.Max(0, offset);
            _viewportHeight = Math.Max(LineHeight, height);
            InvalidateVisual();
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            var contentWidth = TextStart + (_maxLineLength * CharWidth) + 160;
            var width = double.IsInfinity(availableSize.Width)
                ? contentWidth
                : Math.Max(availableSize.Width, contentWidth);
            var height = TopPadding + (_visibleLineIndexes.Length * LineHeight) + BottomPadding;
            return new Size(width, height);
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);

            var viewportTop = Math.Max(0, _viewportOffset - LineHeight);
            var viewportBottom = Math.Min(Bounds.Height, _viewportOffset + _viewportHeight + LineHeight);
            var viewportRect = new Rect(0, viewportTop, Bounds.Width, Math.Max(LineHeight, viewportBottom - viewportTop));
            context.DrawRectangle(Palette.Background, null, viewportRect);
            context.DrawRectangle(Palette.GutterBackground, null, new Rect(0, viewportTop, GutterWidth, viewportRect.Height));
            context.DrawLine(new Pen(Palette.GutterRule, 1), new Point(GutterWidth - 0.5, viewportTop), new Point(GutterWidth - 0.5, viewportBottom));

            var firstVisibleIndex = Math.Max(0, (int)Math.Floor((_viewportOffset - TopPadding) / LineHeight) - 2);
            var lastVisibleIndex = Math.Min(
                _visibleLineIndexes.Length - 1,
                (int)Math.Ceiling((_viewportOffset + _viewportHeight - TopPadding) / LineHeight) + 2);

            for (var visibleIndex = firstVisibleIndex; visibleIndex <= lastVisibleIndex; visibleIndex++)
            {
                var lineIndex = _visibleLineIndexes[visibleIndex];
                var y = TopPadding + (visibleIndex * LineHeight);
                DrawLineChangeBackground(context, lineIndex, y);
                DrawLineNumber(context, lineIndex + 1, y);

                if (_foldsByStartLine.TryGetValue(lineIndex, out var fold))
                {
                    DrawFoldArrow(context, y, _collapsedStartLines.Contains(fold.StartLine));
                }

                DrawCodeLine(context, lineIndex, _lines[lineIndex], y);

                if (_collapsedStartLines.Contains(lineIndex))
                {
                    DrawText(context, "...", Palette.Comment, new Point(TextStart + (_lines[lineIndex].Length * CharWidth) + 8, y), FontSize);
                }
            }
        }

        private void DrawLineChangeBackground(DrawingContext context, int lineIndex, double y)
        {
            if (!_lineChanges.TryGetValue(lineIndex, out var change))
            {
                return;
            }

            var brush = change == "delete" ? Palette.DeleteLineBackground : Palette.AddLineBackground;
            var stripe = change == "delete" ? Palette.DeleteStripe : Palette.AddStripe;
            context.DrawRectangle(brush, null, new Rect(GutterWidth, y, Math.Max(0, Bounds.Width - GutterWidth), LineHeight));
            context.DrawRectangle(stripe, null, new Rect(GutterWidth, y + 2, 2, LineHeight - 4));
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);

            var position = e.GetPosition(this);
            if (position.X < ArrowCenterX - 8 || position.X > ArrowCenterX + 8 || position.Y < TopPadding)
            {
                return;
            }

            var visibleLine = (int)((position.Y - TopPadding) / LineHeight);
            if (visibleLine < 0 || visibleLine >= _visibleLineIndexes.Length)
            {
                return;
            }

            var lineIndex = _visibleLineIndexes[visibleLine];
            if (!_foldsByStartLine.ContainsKey(lineIndex))
            {
                return;
            }

            if (!_collapsedStartLines.Add(lineIndex))
            {
                _collapsedStartLines.Remove(lineIndex);
            }

            RebuildVisibleLines();
            InvalidateMeasure();
            InvalidateVisual();
            e.Handled = true;
        }

        private void DrawLineNumber(DrawingContext context, int number, double y)
        {
            var text = number.ToString(CultureInfo.InvariantCulture);
            var x = LineNumberRight - (text.Length * CharWidth);
            DrawText(context, text, Palette.LineNumber, new Point(Math.Max(5, x), y + 1), LineNumberFontSize);
        }

        private void DrawFoldArrow(DrawingContext context, double y, bool collapsed)
        {
            var centerY = y + (LineHeight / 2) + 0.5;
            var pen = new Pen(Palette.FoldArrow, 1.35);

            if (collapsed)
            {
                context.DrawLine(pen, new Point(ArrowCenterX - 2.5, centerY - 4), new Point(ArrowCenterX + 2.5, centerY));
                context.DrawLine(pen, new Point(ArrowCenterX + 2.5, centerY), new Point(ArrowCenterX - 2.5, centerY + 4));
                return;
            }

            context.DrawLine(pen, new Point(ArrowCenterX - 4, centerY - 2.5), new Point(ArrowCenterX, centerY + 2.5));
            context.DrawLine(pen, new Point(ArrowCenterX, centerY + 2.5), new Point(ArrowCenterX + 4, centerY - 2.5));
        }

        private void DrawCodeLine(DrawingContext context, int lineIndex, string line, double y)
        {
            if (line.Length == 0)
            {
                return;
            }

            foreach (var segment in GetLineSegments(line, lineIndex))
            {
                DrawSegment(context, line, segment.Start, segment.Length, segment.Brush, y);
            }
        }

        private static void DrawSegment(DrawingContext context, string line, int start, int length, IBrush brush, double y)
        {
            if (length <= 0)
            {
                return;
            }

            var text = line.Substring(start, length);
            var x = TextStart + (start * CharWidth);
            DrawText(context, text, brush, new Point(x, y), FontSize);
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
            _grammar = null;
            if (string.IsNullOrWhiteSpace(_extension))
            {
                return;
            }

            try
            {
                var scopeName = _registryOptions.GetScopeByExtension(_extension);
                if (!string.IsNullOrWhiteSpace(scopeName))
                {
                    _grammar = _registry.LoadGrammar(scopeName);
                }
            }
            catch
            {
                _grammar = null;
            }
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
            var visible = new List<int>(_lines.Length);
            for (var index = 0; index < _lines.Length; index++)
            {
                visible.Add(index);
                if (_collapsedStartLines.Contains(index) && _foldsByStartLine.TryGetValue(index, out var fold))
                {
                    index = Math.Min(fold.EndLine, _lines.Length - 1);
                }
            }

            _visibleLineIndexes = visible.Count == 0 ? [0] : visible.ToArray();
        }

        private static string[] NormalizeLines(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return [""];
            }

            return text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        }

        private static Dictionary<int, FoldRegion> BuildFoldRegions(IReadOnlyList<string> lines)
        {
            var result = new Dictionary<int, FoldRegion>();
            var stack = new Stack<(int BraceLine, int OwnerLine, int Column)>();

            for (var lineIndex = 0; lineIndex < lines.Count; lineIndex++)
            {
                var line = lines[lineIndex];
                for (var column = 0; column < line.Length; column++)
                {
                    if (line[column] == '{')
                    {
                        stack.Push((lineIndex, FindFoldOwnerLine(lines, lineIndex, column), column));
                    }
                    else if (line[column] == '}' && stack.Count > 0)
                    {
                        var open = stack.Pop();
                        if (lineIndex > open.OwnerLine
                            && (!result.TryGetValue(open.OwnerLine, out var existing) || lineIndex > existing.EndLine))
                        {
                            result[open.OwnerLine] = new FoldRegion(open.OwnerLine, lineIndex);
                        }
                    }
                }
            }

            return result;
        }

        private static int FindFoldOwnerLine(IReadOnlyList<string> lines, int braceLine, int braceColumn)
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

                return lineIndex;
            }

            return braceLine;
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
        private IReadOnlyDictionary<int, string> _lineChanges = new Dictionary<int, string>();
        private string[] _lines = [""];
        private string _extension = "";
        private double _viewportOffset;
        private double _viewportHeight;
        private double _extentHeight;

        public void SetDocument(string text, string path, IReadOnlyDictionary<int, string>? lineChanges)
        {
            _extension = Path.GetExtension(path).ToLowerInvariant();
            _lineChanges = lineChanges ?? new Dictionary<int, string>();
            _lines = string.IsNullOrEmpty(text)
                ? [""]
                : text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');

            Height = Math.Clamp(_lines.Length * 0.42, 118, 420);
            InvalidateMeasure();
            InvalidateVisual();
        }

        public void SetViewport(double offset, double viewportHeight, double extentHeight)
        {
            _viewportOffset = offset;
            _viewportHeight = viewportHeight;
            _extentHeight = extentHeight;
            InvalidateVisual();
        }

        public void ApplyTheme()
        {
            InvalidateVisual();
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);

            var bounds = Bounds;
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                return;
            }

            var inner = new Rect(8, 8, Math.Max(0, bounds.Width - 16), Math.Max(0, bounds.Height - 16));
            context.DrawRectangle(Palette.MinimapShell, new Pen(Palette.MinimapBorder, 1), bounds, 6, 6);
            context.DrawRectangle(Palette.MinimapCanvas, null, inner, 3, 3);

            var rowHeight = inner.Height / Math.Max(1, _lines.Length);
            var drawnHeight = Math.Max(0.7, Math.Min(2.2, rowHeight * 0.7));
            var step = Math.Max(1, (int)Math.Ceiling(_lines.Length / 700.0));

            for (var index = 0; index < _lines.Length; index += step)
            {
                var trimmed = _lines[index].Trim();
                if (trimmed.Length == 0)
                {
                    continue;
                }

                var y = inner.Y + (index * rowHeight);
                var width = Math.Clamp(trimmed.Length * 0.55, 4, inner.Width);
                context.DrawRectangle(GetMinimapBrush(trimmed), null, new Rect(inner.X, y, width, drawnHeight));
            }

            foreach (var change in _lineChanges)
            {
                if (change.Key < 0 || change.Key >= _lines.Length)
                {
                    continue;
                }

                var y = inner.Y + (change.Key * rowHeight);
                var brush = change.Value == "delete" ? Palette.MinimapDelete : Palette.MinimapAdd;
                context.DrawRectangle(brush, null, new Rect(inner.X, y, inner.Width, Math.Max(1.1, drawnHeight)));
            }

            DrawViewport(context, inner);
        }

        private void DrawViewport(DrawingContext context, Rect inner)
        {
            if (_extentHeight <= 0 || _viewportHeight <= 0)
            {
                return;
            }

            var top = inner.Y + Math.Clamp(_viewportOffset / _extentHeight, 0, 1) * inner.Height;
            var height = Math.Clamp((_viewportHeight / _extentHeight) * inner.Height, 8, inner.Height);
            if (top + height > inner.Bottom)
            {
                top = inner.Bottom - height;
            }

            var rect = new Rect(inner.X - 2, top, inner.Width + 4, height);
            context.DrawRectangle(Palette.MinimapViewport, new Pen(Palette.MinimapViewportBorder, 1), rect, 3, 3);
        }

        private IBrush GetMinimapBrush(string trimmed)
        {
            if (trimmed.StartsWith("#", StringComparison.Ordinal)
                || trimmed.StartsWith("//", StringComparison.Ordinal)
                || trimmed.StartsWith("/*", StringComparison.Ordinal))
            {
                return Palette.MinimapComment;
            }

            if (_extension is ".json" && trimmed.Contains(':', StringComparison.Ordinal))
            {
                return Palette.MinimapKeyword;
            }

            return trimmed.StartsWith("function ", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("public ", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("private ", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("class ", StringComparison.OrdinalIgnoreCase)
                    ? Palette.MinimapKeyword
                    : Palette.MinimapCode;
        }
    }

    private readonly record struct FoldRegion(int StartLine, int EndLine);

    private readonly record struct TokenSpan(int Start, int Length, IBrush Brush);

    private readonly record struct RenderSegment(int Start, int Length, IBrush Brush);

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

    private static class Palette
    {
        private static PaletteSet _current = CreateEmpty();

        public static readonly FontFamily CodeFont = new("Cascadia Code, Cascadia Mono, Consolas, monospace");
        public static readonly Typeface CodeTypeface = new(CodeFont);

        public static IBrush Background => _current.Background;
        public static IBrush GutterBackground => _current.GutterBackground;
        public static IBrush GutterRule => _current.GutterRule;
        public static IBrush LineNumber => _current.LineNumber;
        public static IBrush FoldArrow => _current.FoldArrow;
        public static IBrush Code => _current.Code;
        public static IBrush Keyword => _current.Keyword;
        public static IBrush Type => _current.Type;
        public static IBrush String => _current.String;
        public static IBrush Number => _current.Number;
        public static IBrush Comment => _current.Comment;
        public static IBrush Variable => _current.Variable;
        public static IBrush Function => _current.Function;
        public static IBrush[] Brackets => _current.Brackets;
        public static IBrush MinimapShell => _current.MinimapShell;
        public static IBrush MinimapCanvas => _current.MinimapCanvas;
        public static IBrush MinimapBorder => _current.MinimapBorder;
        public static IBrush MinimapCode => _current.MinimapCode;
        public static IBrush MinimapKeyword => _current.MinimapKeyword;
        public static IBrush MinimapComment => _current.MinimapComment;
        public static IBrush MinimapAdd => _current.MinimapAdd;
        public static IBrush MinimapDelete => _current.MinimapDelete;
        public static IBrush MinimapViewport => _current.MinimapViewport;
        public static IBrush MinimapViewportBorder => _current.MinimapViewportBorder;
        public static IBrush AddLineBackground => _current.AddLineBackground;
        public static IBrush DeleteLineBackground => _current.DeleteLineBackground;
        public static IBrush AddStripe => _current.AddStripe;
        public static IBrush DeleteStripe => _current.DeleteStripe;

        public static void Use(string? themeKey, string? syntaxThemeKey)
        {
            var normalizedTheme = themeKey?.ToLowerInvariant();
            _current = normalizedTheme switch
            {
                "dark" => CreateDark(),
                "matrix" => CreateMatrix(),
                _ => CreateEmpty()
            };
            ApplySyntax(_current, normalizedTheme, syntaxThemeKey);
        }

        private static PaletteSet CreateEmpty()
        {
            return new PaletteSet
            {
                Background = Brush(255, 255, 255),
                GutterBackground = Brush(245, 248, 247),
                GutterRule = Brush(213, 223, 224),
                LineNumber = Brush(140, 153, 157),
                FoldArrow = Brush(83, 97, 102),
                Code = Brush(39, 48, 52),
                Keyword = Brush(13, 107, 114),
                Type = Brush(43, 122, 104),
                String = Brush(155, 92, 36),
                Number = Brush(107, 94, 183),
                Comment = Brush(122, 133, 136),
                Variable = Brush(135, 90, 37),
                Function = Brush(113, 93, 31),
                Brackets =
                [
                    Brush(198, 143, 26),
                    Brush(13, 107, 114),
                    Brush(132, 91, 183),
                    Brush(43, 122, 104),
                    Brush(178, 74, 66),
                    Brush(71, 113, 161)
                ],
                MinimapShell = Brush(234, 240, 240, 248),
                MinimapCanvas = Brush(255, 255, 255, 255),
                MinimapBorder = Brush(184, 197, 199, 190),
                MinimapCode = Brush(64, 76, 80, 148),
                MinimapKeyword = Brush(13, 107, 114, 168),
                MinimapComment = Brush(122, 133, 136, 110),
                MinimapAdd = Brush(30, 127, 87, 160),
                MinimapDelete = Brush(178, 74, 66, 160),
                MinimapViewport = Brush(13, 107, 114, 38),
                MinimapViewportBorder = Brush(13, 107, 114, 96),
                AddLineBackground = Brush(30, 127, 87, 34),
                DeleteLineBackground = Brush(178, 74, 66, 34),
                AddStripe = Brush(30, 127, 87, 130),
                DeleteStripe = Brush(178, 74, 66, 130)
            };
        }

        private static PaletteSet CreateDark()
        {
            return new PaletteSet
            {
                Background = Brush(11, 14, 16),
                GutterBackground = Brush(17, 22, 24),
                GutterRule = Brush(42, 54, 58),
                LineNumber = Brush(126, 140, 145),
                FoldArrow = Brush(183, 199, 203),
                Code = Brush(221, 228, 230),
                Keyword = Brush(107, 211, 209),
                Type = Brush(127, 205, 184),
                String = Brush(230, 183, 116),
                Number = Brush(200, 176, 255),
                Comment = Brush(132, 146, 151),
                Variable = Brush(226, 184, 124),
                Function = Brush(226, 212, 127),
                Brackets =
                [
                    Brush(230, 191, 92),
                    Brush(107, 211, 209),
                    Brush(200, 176, 255),
                    Brush(127, 205, 184),
                    Brush(255, 123, 114),
                    Brush(130, 170, 222)
                ],
                MinimapShell = Brush(22, 29, 32, 248),
                MinimapCanvas = Brush(11, 14, 16, 255),
                MinimapBorder = Brush(65, 80, 85, 210),
                MinimapCode = Brush(221, 228, 230, 120),
                MinimapKeyword = Brush(107, 211, 209, 150),
                MinimapComment = Brush(132, 146, 151, 105),
                MinimapAdd = Brush(115, 213, 155, 155),
                MinimapDelete = Brush(255, 123, 114, 155),
                MinimapViewport = Brush(107, 211, 209, 34),
                MinimapViewportBorder = Brush(107, 211, 209, 100),
                AddLineBackground = Brush(115, 213, 155, 34),
                DeleteLineBackground = Brush(255, 123, 114, 34),
                AddStripe = Brush(115, 213, 155, 150),
                DeleteStripe = Brush(255, 123, 114, 150)
            };
        }

        private static PaletteSet CreateMatrix()
        {
            return new PaletteSet
            {
                Background = Brush(2, 6, 4),
                GutterBackground = Brush(6, 17, 15),
                GutterRule = Brush(23, 55, 47),
                LineNumber = Brush(103, 172, 143),
                FoldArrow = Brush(150, 255, 195),
                Code = Brush(220, 232, 226),
                Keyword = Brush(101, 240, 178),
                Type = Brush(129, 220, 188),
                String = Brush(230, 183, 116),
                Number = Brush(209, 247, 122),
                Comment = Brush(92, 153, 126),
                Variable = Brush(226, 184, 124),
                Function = Brush(226, 212, 127),
                Brackets =
                [
                    Brush(215, 247, 122),
                    Brush(101, 240, 178),
                    Brush(157, 205, 255),
                    Brush(129, 220, 188),
                    Brush(255, 121, 121),
                    Brush(189, 162, 255)
                ],
                MinimapShell = Brush(6, 20, 15, 250),
                MinimapCanvas = Brush(2, 6, 4, 255),
                MinimapBorder = Brush(42, 169, 121, 220),
                MinimapCode = Brush(220, 232, 226, 120),
                MinimapKeyword = Brush(101, 240, 178, 150),
                MinimapComment = Brush(92, 153, 126, 120),
                MinimapAdd = Brush(106, 255, 185, 155),
                MinimapDelete = Brush(255, 121, 121, 155),
                MinimapViewport = Brush(101, 240, 178, 36),
                MinimapViewportBorder = Brush(101, 240, 178, 115),
                AddLineBackground = Brush(106, 255, 185, 34),
                DeleteLineBackground = Brush(255, 121, 121, 34),
                AddStripe = Brush(106, 255, 185, 160),
                DeleteStripe = Brush(255, 121, 121, 160)
            };
        }

        private static void ApplySyntax(PaletteSet palette, string? themeKey, string? syntaxThemeKey)
        {
            var key = syntaxThemeKey?.ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(key) || key == "adaptive")
            {
                key = themeKey == "empty" ? "empty-light" : "a11y-dark";
            }

            var syntax = key switch
            {
                "empty-light" => Syntax(
                    Brush(39, 48, 52), Brush(13, 107, 114), Brush(43, 122, 104), Brush(155, 92, 36),
                    Brush(107, 94, 183), Brush(122, 133, 136), Brush(135, 90, 37), Brush(113, 93, 31),
                    [Brush(198, 143, 26), Brush(13, 107, 114), Brush(132, 91, 183), Brush(43, 122, 104), Brush(178, 74, 66), Brush(71, 113, 161)]),
                "solarized-light" => Syntax(
                    Brush(88, 110, 117), Brush(38, 139, 210), Brush(42, 161, 152), Brush(203, 75, 22),
                    Brush(108, 113, 196), Brush(147, 161, 161), Brush(181, 137, 0), Brush(133, 153, 0),
                    [Brush(181, 137, 0), Brush(38, 139, 210), Brush(211, 54, 130), Brush(42, 161, 152), Brush(220, 50, 47), Brush(108, 113, 196)]),
                "a11y-dark" => Syntax(
                    Brush(221, 228, 230), Brush(107, 211, 209), Brush(127, 205, 184), Brush(230, 183, 116),
                    Brush(200, 176, 255), Brush(132, 146, 151), Brush(226, 184, 124), Brush(226, 212, 127),
                    [Brush(230, 191, 92), Brush(107, 211, 209), Brush(200, 176, 255), Brush(127, 205, 184), Brush(255, 123, 114), Brush(130, 170, 222)]),
                "github-dark" => Syntax(
                    Brush(201, 209, 217), Brush(255, 123, 114), Brush(121, 192, 255), Brush(165, 214, 255),
                    Brush(121, 192, 255), Brush(139, 148, 158), Brush(255, 166, 87), Brush(210, 168, 255),
                    [Brush(255, 212, 128), Brush(121, 192, 255), Brush(210, 168, 255), Brush(86, 211, 100), Brush(255, 123, 114), Brush(165, 214, 255)]),
                "one-dark" => Syntax(
                    Brush(171, 178, 191), Brush(198, 120, 221), Brush(97, 175, 239), Brush(152, 195, 121),
                    Brush(209, 154, 102), Brush(92, 99, 112), Brush(224, 108, 117), Brush(229, 192, 123),
                    [Brush(229, 192, 123), Brush(97, 175, 239), Brush(198, 120, 221), Brush(152, 195, 121), Brush(224, 108, 117), Brush(86, 182, 194)]),
                "nord" => Syntax(
                    Brush(216, 222, 233), Brush(129, 161, 193), Brush(143, 188, 187), Brush(163, 190, 140),
                    Brush(180, 142, 173), Brush(97, 110, 128), Brush(235, 203, 139), Brush(136, 192, 208),
                    [Brush(235, 203, 139), Brush(129, 161, 193), Brush(180, 142, 173), Brush(143, 188, 187), Brush(191, 97, 106), Brush(136, 192, 208)]),
                "monokai" => Syntax(
                    Brush(248, 248, 242), Brush(249, 38, 114), Brush(102, 217, 239), Brush(230, 219, 116),
                    Brush(174, 129, 255), Brush(117, 113, 94), Brush(253, 151, 31), Brush(166, 226, 46),
                    [Brush(230, 219, 116), Brush(102, 217, 239), Brush(174, 129, 255), Brush(166, 226, 46), Brush(249, 38, 114), Brush(253, 151, 31)]),
                "dracula" => Syntax(
                    Brush(248, 248, 242), Brush(255, 121, 198), Brush(139, 233, 253), Brush(241, 250, 140),
                    Brush(189, 147, 249), Brush(98, 114, 164), Brush(255, 184, 108), Brush(80, 250, 123),
                    [Brush(241, 250, 140), Brush(139, 233, 253), Brush(189, 147, 249), Brush(80, 250, 123), Brush(255, 121, 198), Brush(255, 184, 108)]),
                "solarized-dark" => Syntax(
                    Brush(131, 148, 150), Brush(38, 139, 210), Brush(42, 161, 152), Brush(203, 75, 22),
                    Brush(108, 113, 196), Brush(88, 110, 117), Brush(181, 137, 0), Brush(133, 153, 0),
                    [Brush(181, 137, 0), Brush(38, 139, 210), Brush(211, 54, 130), Brush(42, 161, 152), Brush(220, 50, 47), Brush(108, 113, 196)]),
                "high-contrast-dark" => Syntax(
                    Brush(245, 247, 250), Brush(87, 166, 255), Brush(126, 231, 135), Brush(255, 214, 128),
                    Brush(214, 181, 255), Brush(170, 181, 191), Brush(255, 176, 87), Brush(255, 235, 120),
                    [Brush(255, 235, 120), Brush(87, 166, 255), Brush(214, 181, 255), Brush(126, 231, 135), Brush(255, 125, 125), Brush(122, 221, 255)]),
                _ => Syntax(
                    Brush(212, 216, 225), Brush(120, 168, 255), Brush(121, 192, 170), Brush(230, 177, 126),
                    Brush(197, 165, 255), Brush(126, 135, 147), Brush(224, 181, 127), Brush(220, 210, 138),
                    [Brush(238, 205, 122), Brush(111, 177, 255), Brush(205, 153, 255), Brush(113, 212, 174), Brush(242, 132, 130), Brush(160, 170, 246)])
            };

            palette.Code = syntax.Code;
            palette.Keyword = syntax.Keyword;
            palette.Type = syntax.Type;
            palette.String = syntax.String;
            palette.Number = syntax.Number;
            palette.Comment = syntax.Comment;
            palette.Variable = syntax.Variable;
            palette.Function = syntax.Function;
            palette.Brackets = syntax.Brackets;
            palette.MinimapCode = WithAlpha(syntax.Code, 132);
            palette.MinimapKeyword = WithAlpha(syntax.Keyword, 160);
            palette.MinimapComment = WithAlpha(syntax.Comment, 112);
        }

        private static SyntaxPalette Syntax(
            IBrush code,
            IBrush keyword,
            IBrush type,
            IBrush @string,
            IBrush number,
            IBrush comment,
            IBrush variable,
            IBrush function,
            IBrush[] brackets)
        {
            return new SyntaxPalette(code, keyword, type, @string, number, comment, variable, function, brackets);
        }

        private static SolidColorBrush WithAlpha(IBrush brush, byte alpha)
        {
            var color = brush is ISolidColorBrush solid ? solid.Color : Colors.White;
            return new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
        }

        private static SolidColorBrush Brush(byte red, byte green, byte blue, byte alpha = 255)
        {
            return new SolidColorBrush(Color.FromArgb(alpha, red, green, blue));
        }
    }

    private sealed class PaletteSet
    {
        public required IBrush Background { get; init; }
        public required IBrush GutterBackground { get; init; }
        public required IBrush GutterRule { get; init; }
        public required IBrush LineNumber { get; init; }
        public required IBrush FoldArrow { get; init; }
        public required IBrush Code { get; set; }
        public required IBrush Keyword { get; set; }
        public required IBrush Type { get; set; }
        public required IBrush String { get; set; }
        public required IBrush Number { get; set; }
        public required IBrush Comment { get; set; }
        public required IBrush Variable { get; set; }
        public required IBrush Function { get; set; }
        public required IBrush[] Brackets { get; set; }
        public required IBrush MinimapShell { get; init; }
        public required IBrush MinimapCanvas { get; init; }
        public required IBrush MinimapBorder { get; init; }
        public required IBrush MinimapCode { get; set; }
        public required IBrush MinimapKeyword { get; set; }
        public required IBrush MinimapComment { get; set; }
        public required IBrush MinimapAdd { get; init; }
        public required IBrush MinimapDelete { get; init; }
        public required IBrush MinimapViewport { get; init; }
        public required IBrush MinimapViewportBorder { get; init; }
        public required IBrush AddLineBackground { get; init; }
        public required IBrush DeleteLineBackground { get; init; }
        public required IBrush AddStripe { get; init; }
        public required IBrush DeleteStripe { get; init; }
    }

    private sealed record SyntaxPalette(
        IBrush Code,
        IBrush Keyword,
        IBrush Type,
        IBrush String,
        IBrush Number,
        IBrush Comment,
        IBrush Variable,
        IBrush Function,
        IBrush[] Brackets);
}
