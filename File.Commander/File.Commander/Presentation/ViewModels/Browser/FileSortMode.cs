namespace File.Commander.Presentation.ViewModels.Browser;

/// <summary>The sort menu's choices. Saved in settings and used by every folder opened.</summary>
public enum FileSortMode
{
    /// <summary>A-Z</summary>
    NameAscending,

    /// <summary>Z-A</summary>
    NameDescending,

    /// <summary>Last Modified: most recently changed first.</summary>
    NewestFirst,

    /// <summary>First Modified: least recently changed first.</summary>
    OldestFirst,

    /// <summary>Size: largest first.</summary>
    LargestFirst,

    /// <summary>Type: by type name, e.g. all "JSON source" files together.</summary>
    Type
}

public static class FileSorting
{
    // One instance per order and folder placement, so callers can tell by reference whether the order changed
    private static readonly Dictionary<(FileSort, bool), IComparer<FileEntryViewModel>> Comparers =
        // Method syntax: "descending" is a keyword inside query expressions
        Enum.GetValues<FileSortColumn>()
            .SelectMany(column => new[] { new FileSort(column, false), new FileSort(column, true) })
            .SelectMany(sort => new[] { (sort, false), (sort, true) })
            .ToDictionary(key => key, key => Create(key.Item1, key.Item2));

    public static IComparer<FileEntryViewModel> For(FileSort sort, bool mixFilesAndFolders = false)
        => Comparers[(sort, mixFilesAndFolders)];

    public static IComparer<FileEntryViewModel> For(FileSortMode mode, bool mixFilesAndFolders = false)
        => For(FileSort.Of(mode), mixFilesAndFolders);

    private static IComparer<FileEntryViewModel> Create(FileSort sort, bool mixFilesAndFolders) =>
        Comparer<FileEntryViewModel>.Create((a, b) =>
        {
            // Folders come first, whatever the order, unless the user mixes them in
            if (!mixFilesAndFolders && a.IsDirectory != b.IsDirectory)
                return a.IsDirectory ? -1 : 1;

            var descending = sort.Descending;
            var result = sort.Column switch
            {
                FileSortColumn.Name => Directed(ByName(a, b), descending),
                FileSortColumn.Type => Directed(
                    string.Compare(a.TypeText, b.TypeText, StringComparison.CurrentCultureIgnoreCase), descending),
                // Folders have no size, so among them this falls through to the name
                FileSortColumn.Size => MissingLast(a.Size, b.Size, descending),
                FileSortColumn.Modified => MissingLast(a.Modified, b.Modified, descending),
                FileSortColumn.Created => MissingLast(a.Created, b.Created, descending),
                FileSortColumn.Accessed => MissingLast(a.Accessed, b.Accessed, descending),
                FileSortColumn.Permissions => MissingLast(ModeOf(a), ModeOf(b), descending),
                _ => 0,
            };

            // Ties are ordered by name, A-Z
            return result != 0 ? result : ByName(a, b);
        });

    private static int ByName(FileEntryViewModel a, FileEntryViewModel b)
        => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);

    private static int Directed(int result, bool descending) => descending ? -result : result;

    // UnixFileMode is an enum, which has no IComparable<T>
    private static int? ModeOf(FileEntryViewModel entry) => entry.Permissions is { } mode ? (int)mode : null;

    /// <summary>Entries whose value couldn't be read go last, whichever the direction.</summary>
    private static int MissingLast<T>(T? a, T? b, bool descending) where T : struct, IComparable<T>
        => (a, b) switch
        {
            ({ } x, { } y) => Directed(x.CompareTo(y), descending),
            (not null, null) => -1,
            (null, not null) => 1,
            _ => 0,
        };
}
