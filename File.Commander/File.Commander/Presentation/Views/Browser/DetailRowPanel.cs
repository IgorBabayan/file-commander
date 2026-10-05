using Avalonia;
using Avalonia.Controls;
using File.Commander.Presentation.ViewModels.Browser;

namespace File.Commander.Presentation.Views.Browser;

/// <summary>
/// One row of the list or tree, or the column header above them: the leading slot (icon) on the left,
/// the detail columns on the right in the user's order, and the name in between, taking what is left.
/// </summary>
/// <remarks>
/// A child is a detail column when it carries that column's col-* class (the same class that gives it its
/// width, DirectoryView.axaml); a child with <see cref="IsLeadingProperty"/> goes in the leading slot;
/// any other child is the name. Hidden children take no space.
/// The order comes from <see cref="OrderProperty"/>, inherited: DirectoryView sets it once on its root,
/// so the header and every row follow it.
/// </remarks>
public class DetailRowPanel : Panel
{
    /// <summary>Detail columns left to right. Inherited; the declaration order when not set.</summary>
    public static readonly AttachedProperty<IReadOnlyList<DetailColumn>?> OrderProperty =
        AvaloniaProperty.RegisterAttached<DetailRowPanel, Control, IReadOnlyList<DetailColumn>?>(
            "Order", inherits: true);

    /// <summary>Puts the child in the leading slot on the left (the icon).</summary>
    public static readonly AttachedProperty<bool> IsLeadingProperty =
        AvaloniaProperty.RegisterAttached<DetailRowPanel, Control, bool>("IsLeading");

    private static readonly string[] ColumnClasses =
        DetailColumnsLayout.All.Select(ClassOf).ToArray();

    static DetailRowPanel()
    {
        AffectsMeasure<DetailRowPanel>(OrderProperty);
    }

    public static IReadOnlyList<DetailColumn>? GetOrder(Control control) => control.GetValue(OrderProperty);

    public static void SetOrder(Control control, IReadOnlyList<DetailColumn>? value) => control.SetValue(OrderProperty, value);

    public static bool GetIsLeading(Control control) => control.GetValue(IsLeadingProperty);

    public static void SetIsLeading(Control control, bool value) => control.SetValue(IsLeadingProperty, value);

    /// <summary>The style class of <paramref name="column"/>'s cells and header, e.g. col-size.</summary>
    public static string ClassOf(DetailColumn column) => "col-" + column.ToString().ToLowerInvariant();

    /// <summary>The detail column <paramref name="control"/> is a cell or header of, from its col-* class.</summary>
    public static DetailColumn? ColumnOf(Control control)
    {
        for (var i = 0; i < ColumnClasses.Length; i++)
        {
            if (control.Classes.Contains(ColumnClasses[i]))
                return DetailColumnsLayout.All[i];
        }

        return null;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var fixedWidth = 0.0;
        var height = 0.0;
        var unbounded = new Size(double.PositiveInfinity, availableSize.Height);

        // Leading slot and detail columns first: they have their own widths, the name gets the rest
        foreach (var child in Children)
        {
            if (!GetIsLeading(child) && ColumnOf(child) is null)
                continue;

            child.Measure(unbounded);
            fixedWidth += child.DesiredSize.Width;
            height = Math.Max(height, child.DesiredSize.Height);
        }

        var rest = double.IsInfinity(availableSize.Width)
            ? double.PositiveInfinity
            : Math.Max(0, availableSize.Width - fixedWidth);

        var nameWidth = 0.0;
        foreach (var child in Children)
        {
            if (GetIsLeading(child) || ColumnOf(child) is not null)
                continue;

            child.Measure(new Size(rest, availableSize.Height));
            nameWidth = Math.Max(nameWidth, child.DesiredSize.Width);
            height = Math.Max(height, child.DesiredSize.Height);
        }

        return new Size(fixedWidth + nameWidth, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var height = finalSize.Height;
        var x = 0.0;

        foreach (var child in Children)
        {
            if (!GetIsLeading(child))
                continue;

            child.Arrange(new Rect(x, 0, child.DesiredSize.Width, height));
            x += child.DesiredSize.Width;
        }

        var details = DetailsInOrder();
        var detailsWidth = details.Sum(c => c.DesiredSize.Width);
        var nameWidth = Math.Max(0, finalSize.Width - x - detailsWidth);

        foreach (var child in Children)
        {
            if (GetIsLeading(child) || ColumnOf(child) is not null)
                continue;

            child.Arrange(new Rect(x, 0, nameWidth, height));
        }

        x += nameWidth;
        foreach (var child in details)
        {
            child.Arrange(new Rect(x, 0, child.DesiredSize.Width, height));
            x += child.DesiredSize.Width;
        }

        return finalSize;
    }

    /// <summary>The detail column children, left to right. Columns missing from the order go last.</summary>
    private List<Control> DetailsInOrder()
    {
        var byColumn = new Control?[DetailColumnsLayout.All.Count];
        foreach (var child in Children)
        {
            if (!GetIsLeading(child) && ColumnOf(child) is { } column)
                byColumn[(int)column] = child;
        }

        var result = new List<Control>(byColumn.Length);
        foreach (var column in GetOrder(this) ?? DetailColumnsLayout.All)
            Take(column);

        foreach (var column in DetailColumnsLayout.All)
            Take(column);

        return result;

        void Take(DetailColumn column)
        {
            if (byColumn[(int)column] is not { } child)
                return;

            result.Add(child);
            byColumn[(int)column] = null;
        }
    }
}
