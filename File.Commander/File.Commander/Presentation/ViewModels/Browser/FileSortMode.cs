namespace File.Commander.Presentation.ViewModels.Browser;

/// <summary>Order of a folder's entries. Picked from the sort button and kept across navigation.</summary>
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
    // One instance per mode and folder placement, so callers can tell by reference whether the order changed
    private static readonly Dictionary<(FileSortMode, bool), IComparer<FileEntryViewModel>> Comparers =
        Enum.GetValues<FileSortMode>()
            .SelectMany(mode => new[] { (mode, false), (mode, true) })
            .ToDictionary(key => key, key => Create(key.Item1, key.Item2));

    public static IComparer<FileEntryViewModel> For(FileSortMode mode, bool mixFilesAndFolders = false)
        => Comparers[(mode, mixFilesAndFolders)];

    private static IComparer<FileEntryViewModel> Create(FileSortMode mode, bool mixFilesAndFolders) =>
        Comparer<FileEntryViewModel>.Create((a, b) =>
        {
            // Folders come first, whatever the order, unless the user mixes them in
            if (!mixFilesAndFolders && a.IsDirectory != b.IsDirectory)
                return a.IsDirectory ? -1 : 1;

            var result = mode switch
            {
                FileSortMode.NameDescending => -ByName(a, b),
                FileSortMode.NewestFirst => MissingLast(b.Modified, a.Modified, a.Modified, b.Modified),
                FileSortMode.OldestFirst => MissingLast(a.Modified, b.Modified, a.Modified, b.Modified),
                // Folders have no size, so among them this falls through to the name
                FileSortMode.LargestFirst => MissingLast(b.Size, a.Size, a.Size, b.Size),
                FileSortMode.Type => string.Compare(a.TypeText, b.TypeText, StringComparison.CurrentCultureIgnoreCase),
                _ => 0,
            };

            // Ties (and A-Z itself) are ordered by name
            return result != 0 ? result : ByName(a, b);
        });

    private static int ByName(FileEntryViewModel a, FileEntryViewModel b)
        => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);

    /// <summary>
    /// Compares <paramref name="x"/> with <paramref name="y"/> (already swapped for descending orders),
    /// but entries whose value couldn't be read (<paramref name="ofA"/>, <paramref name="ofB"/>) go last either way.
    /// </summary>
    private static int MissingLast<T>(T? x, T? y, T? ofA, T? ofB) where T : struct, IComparable<T>
        => (ofA.HasValue, ofB.HasValue) switch
        {
            (true, true) => x!.Value.CompareTo(y!.Value),
            (true, false) => -1,
            (false, true) => 1,
            _ => 0,
        };
}
