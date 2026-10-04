namespace File.Commander.Presentation.Views.Browser;

/// <summary>The detail columns of the list and tree, left to right.</summary>
internal enum DetailColumn
{
    Size,
    Type,
    Modified,
    Created,
    Accessed,
    Permissions,
}

/// <summary>
/// Fits the detail columns into the width a folder view has (narrow in split view). Columns get their
/// preferred width when there is room; otherwise they all shrink towards a minimum, so their text is
/// trimmed with an ellipsis; when even that doesn't fit, the rightmost columns are dropped.
/// Name always keeps <see cref="NameMinWidth"/>.
/// </summary>
internal static class DetailColumnsLayout
{
    /// <summary>Left margin of every cell and header (TextBlock.cell, Button.column-header).</summary>
    public const double CellMargin = 12;

    public const double NameMinWidth = 120;

    public static IReadOnlyList<DetailColumn> All { get; } = Enum.GetValues<DetailColumn>();

    // Indexed by DetailColumn. Minimums still show something useful: "1.2 MB", "drwx…", "10/3/2026…"
    private static readonly (double Preferred, double Min)[] Widths =
    [
        (76, 48),   // Size
        (110, 56),  // Type
        (128, 84),  // Modified
        (128, 84),  // Created
        (128, 84),  // Accessed
        (84, 60),   // Permissions
    ];

    public static double PreferredWidth(DetailColumn column) => Widths[(int)column].Preferred;

    /// <param name="available">Width for the name and the detail columns, cell margins included.</param>
    /// <param name="shown">Columns the user turned on, left to right.</param>
    /// <returns>Width of every column (by <see cref="DetailColumn"/>), or null for a shown column that doesn't fit.</returns>
    public static double?[] Fit(double available, IReadOnlyList<DetailColumn> shown)
    {
        var result = All.Select(c => (double?)PreferredWidth(c)).ToArray();
        var budget = available - NameMinWidth;

        var kept = shown.ToList();
        while (kept.Count > 0 && Total(kept, w => w.Min) > budget)
        {
            result[(int)kept[^1]] = null;
            kept.RemoveAt(kept.Count - 1);
        }

        if (kept.Count == 0 || Total(kept, w => w.Preferred) <= budget)
            return result;

        // Every kept column gives up the same share of what it has above its minimum
        var range = kept.Sum(c => Widths[(int)c].Preferred - Widths[(int)c].Min);
        var ratio = range > 0 ? Math.Clamp((budget - Total(kept, w => w.Min)) / range, 0, 1) : 0;

        foreach (var column in kept)
        {
            var (preferred, min) = Widths[(int)column];
            result[(int)column] = Math.Floor(min + (preferred - min) * ratio);
        }

        return result;
    }

    private static double Total(List<DetailColumn> columns, Func<(double Preferred, double Min), double> width)
        => columns.Sum(c => width(Widths[(int)c]) + CellMargin);
}
