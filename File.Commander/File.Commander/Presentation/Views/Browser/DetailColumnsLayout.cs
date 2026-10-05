using File.Commander.Presentation.ViewModels.Browser;

namespace File.Commander.Presentation.Views.Browser;

/// <summary>
/// Fits the detail columns into the width a folder view has (narrow in split view). Columns get their
/// preferred width (the user's, when they resized it) when there is room; otherwise they all shrink towards
/// a minimum, so their text is trimmed with an ellipsis; when even that doesn't fit, the rightmost columns
/// are dropped. Name always keeps <see cref="NameMinWidth"/>.
/// </summary>
internal static class DetailColumnsLayout
{
    /// <summary>Left margin of every cell and header (TextBlock.cell, Button.column-header).</summary>
    public const double CellMargin = 12;

    public const double NameMinWidth = 120;

    /// <summary>Widest a column can be dragged.</summary>
    public const double MaxWidth = 480;

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

    /// <summary>The default width of <paramref name="column"/>, before the user resizes it.</summary>
    public static double PreferredWidth(DetailColumn column) => Widths[(int)column].Preferred;

    /// <summary>Narrowest <paramref name="column"/> gets, whether shrunk to fit or dragged by the user.</summary>
    public static double MinWidth(DetailColumn column) => Widths[(int)column].Min;

    /// <summary><paramref name="width"/> kept between the column's minimum and <see cref="MaxWidth"/>.</summary>
    public static double Clamp(DetailColumn column, double width) => Math.Clamp(width, MinWidth(column), MaxWidth);

    /// <param name="available">Width for the name and the detail columns, cell margins included.</param>
    /// <param name="shown">Columns the user turned on, left to right.</param>
    /// <param name="preferredOf">The width each column wants; its default width when null.</param>
    /// <returns>Width of every column (by <see cref="DetailColumn"/>), or null for a shown column that doesn't fit.</returns>
    public static double?[] Fit(double available, IReadOnlyList<DetailColumn> shown,
        Func<DetailColumn, double>? preferredOf = null)
    {
        double Preferred(DetailColumn column) => Clamp(column, preferredOf?.Invoke(column) ?? PreferredWidth(column));

        var result = All.Select(c => (double?)Preferred(c)).ToArray();
        var budget = available - NameMinWidth;

        var kept = shown.ToList();
        while (kept.Count > 0 && Total(kept, MinWidth) > budget)
        {
            result[(int)kept[^1]] = null;
            kept.RemoveAt(kept.Count - 1);
        }

        if (kept.Count == 0 || Total(kept, Preferred) <= budget)
            return result;

        // Every kept column gives up the same share of what it has above its minimum
        var range = kept.Sum(c => Preferred(c) - MinWidth(c));
        var ratio = range > 0 ? Math.Clamp((budget - Total(kept, MinWidth)) / range, 0, 1) : 0;

        foreach (var column in kept)
        {
            var min = MinWidth(column);
            result[(int)column] = Math.Floor(min + (Preferred(column) - min) * ratio);
        }

        return result;
    }

    private static double Total(List<DetailColumn> columns, Func<DetailColumn, double> width)
        => columns.Sum(c => width(c) + CellMargin);
}
