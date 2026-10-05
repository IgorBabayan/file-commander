using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace File.Commander.Presentation.ViewModels.Browser;

/// <summary>
/// Which detail columns the list and tree show (Name is always shown), in which order and how wide.
/// One instance is shared by every folder page, so the choice survives navigation. Columns are toggled
/// from the header's context menu, moved by dragging a header and resized by dragging a header's edge.
/// Stored in settings.json (<see cref="ColumnSettings"/>) by the shell: <see cref="Apply"/> and <see cref="ToSettings"/>.
/// </summary>
public sealed partial class FileColumnsViewModel : ObservableObject
{
    private static readonly IReadOnlyList<DetailColumn> DefaultOrder = Enum.GetValues<DetailColumn>();

    private static readonly IReadOnlyDictionary<DetailColumn, double> NoWidths = new Dictionary<DetailColumn, double>();

    private IReadOnlyList<DetailColumn> _order = DefaultOrder;
    private IReadOnlyDictionary<DetailColumn, double> _widths = NoWidths;

    [ObservableProperty]
    public partial bool ShowSize { get; set; } = true;

    [ObservableProperty]
    public partial bool ShowType { get; set; } = true;

    [ObservableProperty]
    public partial bool ShowModified { get; set; } = true;

    [ObservableProperty]
    public partial bool ShowCreated { get; set; } = true;

    [ObservableProperty]
    public partial bool ShowAccessed { get; set; } = true;

    [ObservableProperty]
    public partial bool ShowPermissions { get; set; } = true;

    /// <summary>
    /// Every detail column, left to right, hidden ones included (they keep their place for when they are
    /// turned back on). A new list on every change, so a reference check tells whether it changed.
    /// </summary>
    public IReadOnlyList<DetailColumn> Order
    {
        get => _order;
        private set => SetProperty(ref _order, value);
    }

    /// <summary>Widths the user dragged columns to. A column that isn't in it has its default width.</summary>
    public IReadOnlyDictionary<DetailColumn, double> Widths
    {
        get => _widths;
        private set => SetProperty(ref _widths, value);
    }

    public bool IsShown(DetailColumn column) => column switch
    {
        DetailColumn.Size => ShowSize,
        DetailColumn.Type => ShowType,
        DetailColumn.Modified => ShowModified,
        DetailColumn.Created => ShowCreated,
        DetailColumn.Accessed => ShowAccessed,
        DetailColumn.Permissions => ShowPermissions,
        _ => false,
    };

    /// <summary>The columns turned on, left to right.</summary>
    public IReadOnlyList<DetailColumn> ShownColumns() => Order.Where(IsShown).ToList();

    /// <summary>The width the user gave <paramref name="column"/>, or null for its default.</summary>
    public double? WidthOf(DetailColumn column) => Widths.TryGetValue(column, out var width) ? width : null;

    /// <summary>Moves <paramref name="column"/> to <paramref name="index"/> of the order without it.</summary>
    public void Move(DetailColumn column, int index)
    {
        var order = Order.ToList();
        if (!order.Remove(column))
            return;

        order.Insert(Math.Clamp(index, 0, order.Count), column);
        if (!order.SequenceEqual(Order))
            Order = order;
    }

    /// <summary>Gives <paramref name="column"/> a width of the user's, or its default one back (null).</summary>
    public void SetWidth(DetailColumn column, double? width)
    {
        if (WidthOf(column) == width)
            return;

        var widths = new Dictionary<DetailColumn, double>(Widths);
        if (width is { } value)
            widths[column] = value;
        else
            widths.Remove(column);

        Widths = widths;
    }

    /// <summary>
    /// Shows what <paramref name="settings"/> stores. Only what differs is changed, so applying what is
    /// already shown raises nothing.
    /// </summary>
    public void Apply(ColumnSettings settings)
    {
        var hidden = Parse(settings.Hidden).ToHashSet();
        ShowSize = !hidden.Contains(DetailColumn.Size);
        ShowType = !hidden.Contains(DetailColumn.Type);
        ShowModified = !hidden.Contains(DetailColumn.Modified);
        ShowCreated = !hidden.Contains(DetailColumn.Created);
        ShowAccessed = !hidden.Contains(DetailColumn.Accessed);
        ShowPermissions = !hidden.Contains(DetailColumn.Permissions);

        // Listed ones first, the rest (a column added in a later version) after them
        var order = Parse(settings.Order).Concat(DefaultOrder).Distinct().ToList();
        if (!order.SequenceEqual(Order))
            Order = order;

        var widths = new Dictionary<DetailColumn, double>();
        if (settings.Widths is { } stored)
        {
            foreach (var (name, width) in stored)
            {
                if (TryParse(name, out var column) && double.IsFinite(width) && width > 0)
                    widths[column] = width;
            }
        }

        if (widths.Count != Widths.Count || widths.Any(w => WidthOf(w.Key) != w.Value))
            Widths = widths.Count == 0 ? NoWidths : widths;
    }

    /// <summary>What is shown now, to store. Defaults are left out (null), so new defaults reach everyone.</summary>
    public ColumnSettings ToSettings()
    {
        var hidden = DefaultOrder.Where(c => !IsShown(c)).Select(c => c.ToString()).ToList();
        return new ColumnSettings
        {
            Hidden = hidden.Count == 0 ? null : hidden,
            Order = Order.SequenceEqual(DefaultOrder) ? null : Order.Select(c => c.ToString()).ToList(),
            Widths = Widths.Count == 0 ? null : Widths.ToDictionary(w => w.Key.ToString(), w => w.Value),
        };
    }

    private static IEnumerable<DetailColumn> Parse(IEnumerable<string>? names)
    {
        if (names is null)
            yield break;

        foreach (var name in names)
        {
            if (TryParse(name, out var column))
                yield return column;
        }
    }

    // By name only: "3" would parse too, and a hand-edited file may have anything
    private static bool TryParse(string? name, out DetailColumn column)
        => Enum.TryParse(name, ignoreCase: true, out column) && Enum.IsDefined(column)
           && !char.IsDigit(name![0]);

    /// <summary>The header's context menu: every column shown, in the default order and width.</summary>
    [RelayCommand]
    private void Reset()
    {
        ShowSize = ShowType = ShowModified = ShowCreated = ShowAccessed = ShowPermissions = true;
        Order = DefaultOrder;
        Widths = NoWidths;
    }
}
