using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace File.Commander.Presentation.Controls;

/// <summary>
/// A thin progress ring around the Action center's icon. <see cref="Value"/> (0–1) fills it clockwise from the top;
/// while <see cref="IsIndeterminate"/> a quarter arc spins instead.
/// </summary>
public sealed class ActivityRing : Control
{
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<ActivityRing, double>(nameof(Value));

    public static readonly StyledProperty<bool> IsIndeterminateProperty =
        AvaloniaProperty.Register<ActivityRing, bool>(nameof(IsIndeterminate));

    public static readonly StyledProperty<double> StrokeThicknessProperty =
        AvaloniaProperty.Register<ActivityRing, double>(nameof(StrokeThickness), 2.5);

    public static readonly StyledProperty<IBrush?> StrokeProperty =
        AvaloniaProperty.Register<ActivityRing, IBrush?>(nameof(Stroke));

    public static readonly StyledProperty<IBrush?> TrackProperty =
        AvaloniaProperty.Register<ActivityRing, IBrush?>(nameof(Track));

    // One turn of the indeterminate arc
    private static readonly TimeSpan SpinPeriod = TimeSpan.FromSeconds(1.1);

    private double _spinAngle;
    private TimeSpan? _spinStart;
    private bool _frameRequested;

    static ActivityRing()
    {
        AffectsRender<ActivityRing>(ValueProperty, IsIndeterminateProperty, StrokeThicknessProperty,
            StrokeProperty, TrackProperty);
    }

    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public bool IsIndeterminate
    {
        get => GetValue(IsIndeterminateProperty);
        set => SetValue(IsIndeterminateProperty, value);
    }

    public double StrokeThickness
    {
        get => GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    public IBrush? Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public IBrush? Track
    {
        get => GetValue(TrackProperty);
        set => SetValue(TrackProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var thickness = StrokeThickness;
        var size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= thickness * 2)
            return;

        var radius = (size - thickness) / 2;
        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);

        if (Track is { } track)
            context.DrawEllipse(null, new Pen(track, thickness), center, radius, radius);

        if (Stroke is not { } stroke)
            return;

        var pen = new Pen(stroke, thickness, lineCap: PenLineCap.Round);
        if (IsIndeterminate)
        {
            DrawArc(context, pen, center, radius, _spinAngle, 0.25);
            RequestSpinFrame();
            return;
        }

        _spinStart = null;
        var sweep = Math.Clamp(Value, 0, 1);
        if (sweep >= 0.999)
            context.DrawEllipse(null, pen, center, radius, radius);
        else if (sweep > 0)
            DrawArc(context, pen, center, radius, 0, sweep);
    }

    /// <param name="startAngle">Degrees clockwise from the top.</param>
    /// <param name="sweep">Part of a full turn, 0–1.</param>
    private static void DrawArc(DrawingContext context, IPen pen, Point center, double radius, double startAngle,
        double sweep)
    {
        var start = PointAt(center, radius, startAngle);
        var end = PointAt(center, radius, startAngle + sweep * 360);

        var geometry = new StreamGeometry();
        using (var figure = geometry.Open())
        {
            figure.BeginFigure(start, isFilled: false);
            figure.ArcTo(end, new Size(radius, radius), 0, sweep > 0.5, SweepDirection.Clockwise);
            figure.EndFigure(isClosed: false);
        }

        context.DrawGeometry(null, pen, geometry);
    }

    private static Point PointAt(Point center, double radius, double degrees)
    {
        var radians = (degrees - 90) * Math.PI / 180;
        return new Point(center.X + radius * Math.Cos(radians), center.Y + radius * Math.Sin(radians));
    }

    /// <summary>Spins only while drawn: a hidden or detached ring asks for no frames.</summary>
    private void RequestSpinFrame()
    {
        if (_frameRequested || TopLevel.GetTopLevel(this) is not { } topLevel)
            return;

        _frameRequested = true;
        topLevel.RequestAnimationFrame(time =>
        {
            _frameRequested = false;
            if (!IsIndeterminate || !IsEffectivelyVisible)
            {
                _spinStart = null;
                return;
            }

            _spinStart ??= time;
            _spinAngle = (time - _spinStart.Value).TotalSeconds / SpinPeriod.TotalSeconds % 1 * 360;
            InvalidateVisual();
        });
    }
}
