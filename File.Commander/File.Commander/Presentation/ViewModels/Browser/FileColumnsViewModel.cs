using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace File.Commander.Presentation.ViewModels.Browser;

/// <summary>
/// Which detail columns the list and tree show (Name is always shown), in which order and how wide.
/// One instance is shared by every folder page, so the choice survives navigation. Columns are toggled
/// from the header's context menu, moved by dragging a header and resized by dragging a header's edge.
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

    /// <summary>The header's context menu: every column shown, in the default order and width.</summary>
    [RelayCommand]
    private void Reset()
    {
        ShowSize = ShowType = ShowModified = ShowCreated = ShowAccessed = ShowPermissions = true;
        Order = DefaultOrder;
        Widths = NoWidths;
    }
}
