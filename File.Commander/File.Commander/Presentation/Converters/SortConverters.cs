using Avalonia.Data.Converters;

namespace File.Commander.Presentation.Converters;

public static class SortConverters
{
    /// <summary>
    /// True when the bound <see cref="FileSort"/> sorts by the <see cref="FileSortColumn"/> given as
    /// ConverterParameter. Shows the arrow on that column's header.
    /// </summary>
    public static readonly IValueConverter IsSortedBy = new FuncValueConverter<FileSort, object?, bool>(
        (sort, column) => column is FileSortColumn c && sort.Column == c);
}
