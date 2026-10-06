using Avalonia;
using Avalonia.Controls;

namespace File.Commander.Presentation.Views.Shell;

/// <summary>
/// Lays out the title bar's toolbar items left to right, like Firefox's navigation toolbar. Items with a
/// <see cref="FlexProperty"/> (the address bar, flexible spaces) share the width the others leave, by weight,
/// and never get narrower than their MinWidth. Neighbors of the same <see cref="GroupProperty"/> (back, forward,
/// up) touch, forming one pill; other items are <see cref="Spacing"/> apart.
/// </summary>
public sealed class ToolbarPanel : Panel
{
    public static readonly AttachedProperty<double> FlexProperty =
        AvaloniaProperty.RegisterAttached<ToolbarPanel, Control, double>("Flex");

    public static readonly AttachedProperty<string?> GroupProperty =
        AvaloniaProperty.RegisterAttached<ToolbarPanel, Control, string?>("Group");

    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<ToolbarPanel, double>(nameof(Spacing), 8);

    static ToolbarPanel()
    {
        AffectsParentMeasure<ToolbarPanel>(FlexProperty, GroupProperty);
        AffectsMeasure<ToolbarPanel>(SpacingProperty);
    }

    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    public static double GetFlex(Control control) => control.GetValue(FlexProperty);

    public static void SetFlex(Control control, double value) => control.SetValue(FlexProperty, value);

    public static string? GetGroup(Control control) => control.GetValue(GroupProperty);

    public static void SetGroup(Control control, string? value) => control.SetValue(GroupProperty, value);

    protected override Size MeasureOverride(Size availableSize)
    {
        var children = VisibleChildren();
        var (fixedWidth, totalFlex) = (0.0, 0.0);
        var height = 0.0;

        foreach (var child in children)
        {
            var flex = GetFlex(child);
            if (flex > 0)
            {
                totalFlex += flex;
                continue;
            }

            child.Measure(new Size(double.PositiveInfinity, availableSize.Height));
            fixedWidth += child.DesiredSize.Width;
            height = Math.Max(height, child.DesiredSize.Height);
        }

        var spacing = TotalSpacing(children);
        var free = double.IsInfinity(availableSize.Width)
            ? 0
            : Math.Max(0, availableSize.Width - fixedWidth - spacing);

        var flexWidth = 0.0;
        foreach (var child in children)
        {
            var flex = GetFlex(child);
            if (flex <= 0)
                continue;

            var width = FlexWidth(child, flex, totalFlex, free);
            child.Measure(new Size(width, availableSize.Height));
            flexWidth += width;
            height = Math.Max(height, child.DesiredSize.Height);
        }

        var total = fixedWidth + spacing + flexWidth;
        return new Size(double.IsInfinity(availableSize.Width) ? total : Math.Min(total, availableSize.Width), height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var children = VisibleChildren();
        var (fixedWidth, totalFlex) = (0.0, 0.0);

        foreach (var child in children)
        {
            var flex = GetFlex(child);
            if (flex > 0)
                totalFlex += flex;
            else
                fixedWidth += child.DesiredSize.Width;
        }

        var free = Math.Max(0, finalSize.Width - fixedWidth - TotalSpacing(children));
        var x = 0.0;
        Control? previous = null;

        // Full height: items center themselves (VerticalAlignment), and the whole bar is a drop target
        foreach (var child in children)
        {
            if (previous is not null)
                x += SpacingBetween(previous, child);

            var flex = GetFlex(child);
            var width = flex > 0 ? FlexWidth(child, flex, totalFlex, free) : child.DesiredSize.Width;
            child.Arrange(new Rect(x, 0, width, finalSize.Height));

            x += width;
            previous = child;
        }

        return finalSize;
    }

    private List<Control> VisibleChildren() => Children.Where(child => child.IsVisible).ToList();

    private static double FlexWidth(Control child, double flex, double totalFlex, double free)
        => Math.Max(child.MinWidth, totalFlex > 0 ? free * flex / totalFlex : 0);

    private double TotalSpacing(List<Control> children)
    {
        var total = 0.0;
        for (var i = 1; i < children.Count; i++)
            total += SpacingBetween(children[i - 1], children[i]);

        return total;
    }

    private double SpacingBetween(Control left, Control right)
        => GetGroup(left) is { } group && group == GetGroup(right) ? 0 : Spacing;
}
