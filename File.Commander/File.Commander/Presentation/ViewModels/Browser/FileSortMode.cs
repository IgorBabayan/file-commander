using System.Globalization;

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

    /// <summary>Size: largest first; folders by how many entries they hold.</summary>
    LargestFirst,

    /// <summary>Type: by family, as GNOME Files: archives, then documents, spreadsheets, text...</summary>
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

    /// <summary>
    /// Compares as GNOME Files (nautilus_file_compare_for_sort) does. The column's own order goes upward, ties go
    /// by name, and a descending sort turns the whole result around, ties included. Folders still lead.
    /// </summary>
    private static IComparer<FileEntryViewModel> Create(FileSort sort, bool mixFilesAndFolders) =>
        Comparer<FileEntryViewModel>.Create((a, b) =>
        {
            // Folders come first, whatever the order, unless the user mixes them in
            if (!mixFilesAndFolders && a.IsDirectory != b.IsDirectory)
                return a.IsDirectory ? -1 : 1;

            var result = sort.Column switch
            {
                FileSortColumn.Name => ByName(a, b),
                FileSortColumn.Size => BySize(a, b),
                FileSortColumn.Type => ByType(a, b),
                FileSortColumn.Modified => UnknownFirst(a.Modified, b.Modified),
                FileSortColumn.Created => UnknownFirst(a.Created, b.Created),
                FileSortColumn.Accessed => UnknownFirst(a.Accessed, b.Accessed),
                FileSortColumn.Permissions => UnknownFirst(ModeOf(a), ModeOf(b)),
                _ => 0,
            };

            if (result == 0 && sort.Column != FileSortColumn.Name)
                result = ByFullPath(a, b);

            return sort.Descending ? -result : result;
        });

    private static CompareInfo Collation => CultureInfo.CurrentCulture.CompareInfo;

    /// <summary>The name, then the folder (search results), then the exact characters.</summary>
    private static int ByName(FileEntryViewModel a, FileEntryViewModel b)
    {
        var result = a.NameKey.CompareTo(b.NameKey);
        if (result == 0)
            result = Collation.Compare(a.ParentPath, b.ParentPath, CompareOptions.None);

        return result != 0 ? result : string.CompareOrdinal(a.Name, b.Name);
    }

    /// <summary>Ties of the other columns: the folder first, so search results from one folder stay together.</summary>
    private static int ByFullPath(FileEntryViewModel a, FileEntryViewModel b)
    {
        var result = Collation.Compare(a.ParentPath, b.ParentPath, CompareOptions.None);
        if (result == 0)
            result = a.NameKey.CompareTo(b.NameKey);

        return result != 0 ? result : string.CompareOrdinal(a.Name, b.Name);
    }

    /// <summary>
    /// Folders before files, also when they are mixed in. Folders by how many entries they hold, files by size.
    /// </summary>
    private static int BySize(FileEntryViewModel a, FileEntryViewModel b)
    {
        if (a.IsDirectory != b.IsDirectory)
            return a.IsDirectory ? -1 : 1;

        return a.IsDirectory ? UnknownFirst(a.ItemCount, b.ItemCount) : UnknownFirst(a.Size, b.Size);
    }

    /// <summary>
    /// Folders before files, also when they are mixed in. Files of the same type are equal; otherwise by family
    /// ("Archive", "Document", "Spreadsheet", "Text"...), then within a family by MIME type, so all PNG images come
    /// before all WebP images.
    /// </summary>
    private static int ByType(FileEntryViewModel a, FileEntryViewModel b)
    {
        if (a.IsDirectory || b.IsDirectory)
            return a.IsDirectory == b.IsDirectory ? 0 : a.IsDirectory ? -1 : 1;

        if (string.Equals(a.MimeType, b.MimeType, StringComparison.Ordinal))
            return 0;

        var result = Collation.Compare(a.BasicType, b.BasicType, CompareOptions.None);
        return result != 0 ? result : Collation.Compare(a.MimeType, b.MimeType, CompareOptions.None);
    }

    // UnixFileMode is an enum, which has no IComparable<T>
    private static int? ModeOf(FileEntryViewModel entry) => entry.Permissions is { } mode ? (int)mode : null;

    /// <summary>Entries whose value couldn't be read come first, so last in a descending sort.</summary>
    private static int UnknownFirst<T>(T? a, T? b) where T : struct, IComparable<T>
        => (a, b) switch
        {
            ({ } x, { } y) => x.CompareTo(y),
            (null, not null) => -1,
            (not null, null) => 1,
            _ => 0,
        };
}
