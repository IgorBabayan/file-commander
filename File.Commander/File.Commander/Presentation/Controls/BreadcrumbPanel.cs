using Avalonia;
using Avalonia.Controls;

namespace File.Commander.Presentation.Controls;

/// <summary>
/// Lays children out left to right. When they don't fit, the leading ones are hidden
/// (like Explorer's address bar), so the current folder always stays visible.
/// </summary>
public sealed class BreadcrumbPanel : Panel
{
    private const double Offscreen = -100_000;

    private int _firstVisible;

    public BreadcrumbPanel() => ClipToBounds = true;

    /// <summary>Number of leading children that are currently hidden.</summary>
    public int HiddenCount => _firstVisible;

    protected override Size MeasureOverride(Size availableSize)
    {
        var unconstrained = new Size(double.PositiveInfinity, availableSize.Height);
        foreach (var child in Children)
            child.Measure(unconstrained);

        _firstVisible = 0;
        double width = 0, height = 0;

        // From the right: the last child is always shown, earlier ones while they fit
        for (var i = Children.Count - 1; i >= 0; i--)
        {
            var size = Children[i].DesiredSize;
            if (i < Children.Count - 1 && width + size.Width > availableSize.Width)
            {
                _firstVisible = i + 1;
                break;
            }

            width += size.Width;
            height = Math.Max(height, size.Height);
        }

        return new Size(Math.Min(width, availableSize.Width), height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double x = 0;
        for (var i = 0; i < Children.Count; i++)
        {
            var child = Children[i];
            var size = child.DesiredSize;

            if (i < _firstVisible)
            {
                // Out of the clip: not drawn and can't be clicked
                child.Arrange(new Rect(Offscreen, 0, size.Width, finalSize.Height));
                continue;
            }

            child.Arrange(new Rect(x, 0, size.Width, finalSize.Height));
            x += size.Width;
        }

        return finalSize;
    }
}
