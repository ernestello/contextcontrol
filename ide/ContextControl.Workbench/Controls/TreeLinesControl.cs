using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace ContextControl.Workbench.Controls;

public sealed class TreeLinesControl : Control
{
    public static readonly StyledProperty<int> DepthProperty =
        AvaloniaProperty.Register<TreeLinesControl, int>(nameof(Depth));

    public static readonly StyledProperty<bool> IsLastProperty =
        AvaloniaProperty.Register<TreeLinesControl, bool>(nameof(IsLast));

    public static readonly StyledProperty<IReadOnlyList<bool>?> AncestorContinuesProperty =
        AvaloniaProperty.Register<TreeLinesControl, IReadOnlyList<bool>?>(nameof(AncestorContinues));

    public static readonly StyledProperty<bool> HasExpandedChildrenProperty =
        AvaloniaProperty.Register<TreeLinesControl, bool>(nameof(HasExpandedChildren));

    public static readonly StyledProperty<bool> IsSpacerProperty =
        AvaloniaProperty.Register<TreeLinesControl, bool>(nameof(IsSpacer));

    public static readonly StyledProperty<string> ThemeKeyProperty =
        AvaloniaProperty.Register<TreeLinesControl, string>(nameof(ThemeKey), "empty");

    private const double RailStep = 9.0;
    private const double RailInset = 6.0;
    private static readonly Pen EmptyRailPen = new(new SolidColorBrush(Color.FromRgb(178, 169, 151)), 1);
    private static readonly Pen EmptyBranchPen = new(new SolidColorBrush(Color.FromRgb(151, 141, 122)), 1);
    private static readonly Pen DarkRailPen = new(new SolidColorBrush(Color.FromRgb(73, 82, 96)), 1);
    private static readonly Pen DarkBranchPen = new(new SolidColorBrush(Color.FromRgb(95, 106, 122)), 1);
    private static readonly Pen MatrixRailPen = new(new SolidColorBrush(Color.FromRgb(38, 132, 68)), 1);
    private static readonly Pen MatrixBranchPen = new(new SolidColorBrush(Color.FromRgb(75, 218, 111)), 1);

    public int Depth
    {
        get => GetValue(DepthProperty);
        set => SetValue(DepthProperty, value);
    }

    public bool IsLast
    {
        get => GetValue(IsLastProperty);
        set => SetValue(IsLastProperty, value);
    }

    public IReadOnlyList<bool>? AncestorContinues
    {
        get => GetValue(AncestorContinuesProperty);
        set => SetValue(AncestorContinuesProperty, value);
    }

    public bool HasExpandedChildren
    {
        get => GetValue(HasExpandedChildrenProperty);
        set => SetValue(HasExpandedChildrenProperty, value);
    }

    public bool IsSpacer
    {
        get => GetValue(IsSpacerProperty);
        set => SetValue(IsSpacerProperty, value);
    }

    public string ThemeKey
    {
        get => GetValue(ThemeKeyProperty);
        set => SetValue(ThemeKeyProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == DepthProperty
            || change.Property == IsLastProperty
            || change.Property == AncestorContinuesProperty
            || change.Property == HasExpandedChildrenProperty
            || change.Property == IsSpacerProperty
            || change.Property == ThemeKeyProperty)
        {
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var centerY = Math.Floor(Bounds.Height * 0.5) + 0.5;
        var ancestors = AncestorContinues ?? [];

        DrawAncestorRails(context, ancestors, Depth, 0, Bounds.Height);

        if (IsSpacer)
        {
            DrawRail(context, Math.Max(0, Depth - 1), 0, Bounds.Height);
            return;
        }

        if (Depth <= 0)
        {
            DrawLineToContent(context, 0, centerY);
            if (HasExpandedChildren)
            {
                DrawRail(context, 0, centerY, Bounds.Height);
            }

            return;
        }

        var parentLevel = Depth - 1;
        DrawRail(context, parentLevel, 0, centerY);
        DrawLineToContent(context, parentLevel, centerY);

        if (!IsLast)
        {
            DrawRail(context, parentLevel, centerY, Bounds.Height);
        }

        if (HasExpandedChildren)
        {
            DrawRail(context, Depth, centerY, Bounds.Height);
        }
    }

    private void DrawAncestorRails(
        DrawingContext context,
        IReadOnlyList<bool> ancestors,
        int depth,
        double startY,
        double endY)
    {
        for (var level = 0; level < depth - 1; level++)
        {
            if (level < ancestors.Count && ancestors[level])
            {
                DrawRail(context, level, startY, endY);
            }
        }
    }

    private void DrawLineToContent(DrawingContext context, int level, double y)
    {
        var x = RailX(level);
        context.DrawLine(BranchPen, new Point(x, y), new Point(Math.Max(x, Bounds.Width - 2), y));
    }

    private void DrawRail(DrawingContext context, int level, double startY, double endY)
    {
        var x = RailX(level);
        context.DrawLine(RailPen, new Point(x, startY), new Point(x, endY));
    }

    private Pen RailPen => ThemeKey?.ToLowerInvariant() switch
    {
        "dark" => DarkRailPen,
        "matrix" => MatrixRailPen,
        _ => EmptyRailPen
    };

    private Pen BranchPen => ThemeKey?.ToLowerInvariant() switch
    {
        "dark" => DarkBranchPen,
        "matrix" => MatrixBranchPen,
        _ => EmptyBranchPen
    };

    private static double RailX(int level)
    {
        return Math.Floor(RailInset + Math.Max(0, level) * RailStep) + 0.5;
    }
}
