using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

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
/// With a <see cref="SeparatorBrush"/> (the header) a thin line is drawn in the gap before every shown
/// detail column: that gap is where the column is resized (DirectoryView.ColumnHeaders.cs). Panel.Render is
/// sealed, so the lines are drawn by a <see cref="SeparatorLayer"/> child laid over the whole panel, created
/// only then: rows don't get one.
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

    /// <summary>Lines between the columns. None when null (the rows).</summary>
    public static readonly StyledProperty<IBrush?> SeparatorBrushProperty =
        AvaloniaProperty.Register<DetailRowPanel, IBrush?>(nameof(SeparatorBrush));

    /// <summary>The line of <see cref="HotColumn"/>: under the pointer or being dragged.</summary>
    public static readonly StyledProperty<IBrush?> HotSeparatorBrushProperty =
        AvaloniaProperty.Register<DetailRowPanel, IBrush?>(nameof(HotSeparatorBrush));

    /// <summary>The column whose separator (on its left) is highlighted.</summary>
    public static readonly StyledProperty<DetailColumn?> HotColumnProperty =
        AvaloniaProperty.Register<DetailRowPanel, DetailColumn?>(nameof(HotColumn));

    // Draws the separators; only while there is a brush to draw them with
    private SeparatorLayer? _separators;

    private static readonly string[] ColumnClasses =
        DetailColumnsLayout.All.Select(ClassOf).ToArray();

    static DetailRowPanel()
    {
        AffectsMeasure<DetailRowPanel>(OrderProperty);
    }

    public IBrush? SeparatorBrush
    {
        get => GetValue(SeparatorBrushProperty);
        set => SetValue(SeparatorBrushProperty, value);
    }

    public IBrush? HotSeparatorBrush
    {
        get => GetValue(HotSeparatorBrushProperty);
        set => SetValue(HotSeparatorBrushProperty, value);
    }

    public DetailColumn? HotColumn
    {
        get => GetValue(HotColumnProperty);
        set => SetValue(HotColumnProperty, value);
    }

    /// <summary>
    /// Where the separator before <paramref name="child"/> (a detail column) is drawn: the middle of the gap
    /// its left margin leaves.
    /// </summary>
    public static double SeparatorX(Control child) => child.Bounds.X - child.Margin.Left / 2;

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
            if (child is SeparatorLayer || (!GetIsLeading(child) && ColumnOf(child) is null))
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
            if (child is SeparatorLayer)
            {
                // Takes no room: it is laid over everything
                child.Measure(default);
                continue;
            }

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
            if (child is SeparatorLayer || GetIsLeading(child) || ColumnOf(child) is not null)
                continue;

            child.Arrange(new Rect(x, 0, nameWidth, height));
        }

        x += nameWidth;
        foreach (var child in details)
        {
            child.Arrange(new Rect(x, 0, child.DesiredSize.Width, height));
            x += child.DesiredSize.Width;
        }

        // Same origin as the panel, so it can draw at the columns' own coordinates
        _separators?.Arrange(new Rect(finalSize));
        _separators?.InvalidateVisual();

        return finalSize;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == SeparatorBrushProperty || change.Property == HotSeparatorBrushProperty)
        {
            if (_separators is null && (SeparatorBrush is not null || HotSeparatorBrush is not null))
            {
                _separators = new SeparatorLayer(this);
                Children.Add(_separators);
            }

            _separators?.InvalidateVisual();
        }
        else if (change.Property == HotColumnProperty)
        {
            _separators?.InvalidateVisual();
        }
    }

    /// <summary>
    /// The lines between the columns of a <see cref="DetailRowPanel"/>, laid over it. Never hit:
    /// the pointer goes to the panel, which finds the grips itself.
    /// </summary>
    private sealed class SeparatorLayer : Control
    {
        // Short of the full height, like the separators of a native list header
        private const double Inset = 1;

        private readonly DetailRowPanel _owner;

        public SeparatorLayer(DetailRowPanel owner)
        {
            _owner = owner;
            IsHitTestVisible = false;
            ZIndex = -1;
        }

        public override void Render(DrawingContext context)
        {
            var top = Inset;
            var bottom = Math.Max(top, Bounds.Height - Inset);

            foreach (var child in _owner.Children)
            {
                if (!child.IsVisible || GetIsLeading(child) || ColumnOf(child) is not { } column)
                    continue;

                var hot = column == _owner.HotColumn;
                var brush = hot ? _owner.HotSeparatorBrush ?? _owner.SeparatorBrush : _owner.SeparatorBrush;
                if (brush is null)
                    continue;

                // On a pixel center, so a 1 px line stays sharp
                var x = Math.Floor(SeparatorX(child)) + 0.5;
                context.DrawLine(new Pen(brush, hot ? 2 : 1), new Point(x, top), new Point(x, bottom));
            }
        }
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
