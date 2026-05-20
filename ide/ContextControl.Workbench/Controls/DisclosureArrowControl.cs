using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace ContextControl.Workbench.Controls;

public sealed class DisclosureArrowControl : Control
{
    public static readonly StyledProperty<double> AngleProperty =
        AvaloniaProperty.Register<DisclosureArrowControl, double>(nameof(Angle));

    public static readonly StyledProperty<string> ThemeKeyProperty =
        AvaloniaProperty.Register<DisclosureArrowControl, string>(nameof(ThemeKey), "empty");

    private static readonly IBrush EmptyArrowBrush = new SolidColorBrush(Color.FromRgb(82, 88, 98));
    private static readonly IBrush DarkArrowBrush = new SolidColorBrush(Color.FromRgb(190, 199, 214));
    private static readonly IBrush MatrixArrowBrush = new SolidColorBrush(Color.FromRgb(129, 255, 154));

    public double Angle
    {
        get => GetValue(AngleProperty);
        set => SetValue(AngleProperty, value);
    }

    public string ThemeKey
    {
        get => GetValue(ThemeKeyProperty);
        set => SetValue(ThemeKeyProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == AngleProperty
            || change.Property == ThemeKeyProperty)
        {
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var center = new Point(Bounds.Width * 0.5, Bounds.Height * 0.5);
        var size = Math.Min(Bounds.Width, Bounds.Height);
        var backX = size * 0.22;
        var halfHeight = size * 0.30;
        var tipX = size * 0.24;
        var left = Rotate(new Point(center.X - backX, center.Y - halfHeight), center, Angle);
        var tip = Rotate(new Point(center.X + tipX, center.Y), center, Angle);
        var right = Rotate(new Point(center.X - backX, center.Y + halfHeight), center, Angle);

        var geometry = new StreamGeometry();
        using (var stream = geometry.Open())
        {
            stream.BeginFigure(left, true);
            stream.LineTo(tip);
            stream.LineTo(right);
            stream.EndFigure(true);
        }

        context.DrawGeometry(ArrowBrush, null, geometry);
    }

    private IBrush ArrowBrush => ThemeKey?.ToLowerInvariant() switch
    {
        "dark" => DarkArrowBrush,
        "matrix" => MatrixArrowBrush,
        _ => EmptyArrowBrush
    };

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
}
