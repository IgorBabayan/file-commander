namespace File.Commander.Presentation.ViewModels.Browser;

/// <summary>A column of the list and tree header. Clicking one sorts the current folder by it.</summary>
public enum FileSortColumn
{
    Name,
    Size,
    Type,
    Modified,
    Created,
    Accessed,
    Permissions
}

/// <summary>
/// How a folder page is ordered: by which column, which way. The sort menu's choices map onto it
/// (<see cref="Of"/>); a header click changes it for the current folder only.
/// </summary>
public readonly record struct FileSort(FileSortColumn Column, bool Descending)
{
    public static FileSort Of(FileSortMode mode) => mode switch
    {
        FileSortMode.NameDescending => new(FileSortColumn.Name, true),
        FileSortMode.NewestFirst => new(FileSortColumn.Modified, true),
        FileSortMode.OldestFirst => new(FileSortColumn.Modified, false),
        FileSortMode.LargestFirst => new(FileSortColumn.Size, true),
        FileSortMode.Type => new(FileSortColumn.Type, false),
        _ => new(FileSortColumn.Name, false),
    };

    /// <summary>
    /// What a click on <paramref name="column"/>'s header gives: the other direction when it already is
    /// the sorted column, otherwise that column in its natural direction.
    /// </summary>
    public FileSort ClickedOn(FileSortColumn column) => column == Column
        ? this with { Descending = !Descending }
        : new FileSort(column, StartsDescending(column));

    // Same as the sort menu: largest and most recent first, text A-Z
    private static bool StartsDescending(FileSortColumn column) => column
        is FileSortColumn.Size or FileSortColumn.Modified or FileSortColumn.Created or FileSortColumn.Accessed;
}
