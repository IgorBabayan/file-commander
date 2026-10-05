using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using File.Commander.Presentation.ViewModels.ActionCenter;

namespace File.Commander.Presentation.Controls;

/// <summary>
/// The speed of an action over its last minute, as a filled line, newest on the right. Scaled to the
/// highest sample, so it shows how steady the speed is rather than how fast it is (the text says that).
/// </summary>
public sealed class SpeedGraph : Control
{
    public static readonly StyledProperty<IReadOnlyList<double>?> SamplesProperty =
        AvaloniaProperty.Register<SpeedGraph, IReadOnlyList<double>?>(nameof(Samples));

    public static readonly StyledProperty<IBrush?> StrokeProperty =
        AvaloniaProperty.Register<SpeedGraph, IBrush?>(nameof(Stroke));

    public static readonly StyledProperty<IBrush?> FillProperty =
        AvaloniaProperty.Register<SpeedGraph, IBrush?>(nameof(Fill));

    public static readonly StyledProperty<IBrush?> GridLinesProperty =
        AvaloniaProperty.Register<SpeedGraph, IBrush?>(nameof(GridLines));

    static SpeedGraph()
    {
        AffectsRender<SpeedGraph>(SamplesProperty, StrokeProperty, FillProperty, GridLinesProperty);
    }

    /// <summary>Oldest first. Replaced, never changed in place.</summary>
    public IReadOnlyList<double>? Samples
    {
        get => GetValue(SamplesProperty);
        set => SetValue(SamplesProperty, value);
    }

    public IBrush? Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public IBrush? Fill
    {
        get => GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    /// <summary>The baseline and the halfway line.</summary>
    public IBrush? GridLines
    {
        get => GetValue(GridLinesProperty);
        set => SetValue(GridLinesProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var width = Bounds.Width;
        var height = Bounds.Height;
        if (width <= 0 || height <= 0)
            return;

        if (GridLines is { } grid)
        {
            var pen = new Pen(grid, 1);
            context.DrawLine(pen, new Point(0, height - 0.5), new Point(width, height - 0.5));
            context.DrawLine(new Pen(grid, 1, new DashStyle([3, 3], 0)), new Point(0, height / 2), new Point(width, height / 2));
        }

        if (Samples is not { Count: > 1 } samples)
            return;

        var max = samples.Max();
        if (max <= 0)
            return;

        // Fixed spacing over the whole minute: the line grows in from the right like a task manager graph
        var step = width / (OperationViewModel.SampleCapacity - 1);
        var left = width - step * (samples.Count - 1);
        var top = 2.0; // Room for the stroke at the peak

        Point At(int index) => new(left + step * index, height - (height - top) * samples[index] / max);

        var line = new StreamGeometry();
        using (var figure = line.Open())
        {
            figure.BeginFigure(At(0), isFilled: false);
            for (var i = 1; i < samples.Count; i++)
                figure.LineTo(At(i));
            figure.EndFigure(isClosed: false);
        }

        if (Fill is { } fill)
        {
            var area = new StreamGeometry();
            using (var figure = area.Open())
            {
                figure.BeginFigure(new Point(left, height), isFilled: true);
                for (var i = 0; i < samples.Count; i++)
                    figure.LineTo(At(i));
                figure.LineTo(new Point(width, height));
                figure.EndFigure(isClosed: true);
            }

            context.DrawGeometry(fill, null, area);
        }

        if (Stroke is { } stroke)
            context.DrawGeometry(null, new Pen(stroke, 1.5, lineJoin: PenLineJoin.Round), line);
    }
}
