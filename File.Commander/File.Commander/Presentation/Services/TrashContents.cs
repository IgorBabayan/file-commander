using System.Globalization;

namespace File.Commander.Presentation.Services;

/// <summary>An item in a trash folder: the entry in files/, and what its info/*.trashinfo file says about it.</summary>
/// <param name="Info">The item itself, in the trash folder's files/.</param>
/// <param name="OriginalPath">Where it was trashed from. Null when its info file is missing or unreadable.</param>
/// <param name="DeletedAt">When it was trashed, in local time. Null when unknown.</param>
public sealed record TrashItem(FileSystemInfo Info, string? OriginalPath, DateTime? DeletedAt)
{
    /// <summary>
    /// The name it had before it was trashed. files/ may hold it under another name ("report.2.pdf")
    /// when something of the same name was already in the trash.
    /// </summary>
    public string Name => OriginalPath is { } path && IOPath.GetFileName(path) is { Length: > 0 } name
        ? name
        : Info.Name;
}

/// <summary>Reads what is in the freedesktop.org trash folders found by <see cref="TrashBins"/>.</summary>
public static class TrashContents
{
    // Everything in files/, dot files included: the page filters them by the name they had.
    // Unreadable entries are skipped rather than hiding the rest of the trash.
    private static readonly EnumerationOptions AllEntries = new() { IgnoreInaccessible = true, AttributesToSkip = 0 };

    /// <summary>The items of every one of <paramref name="bins"/>, in no particular order.</summary>
    /// <remarks>A trash folder that can't be read is skipped (and traced), so the others are still listed.</remarks>
    public static IEnumerable<TrashItem> Read(IReadOnlyList<string> bins, CancellationToken token = default)
    {
        foreach (var bin in bins)
        {
            token.ThrowIfCancellationRequested();

            var info = IOPath.Combine(bin, "info");
            foreach (var item in Items(IOPath.Combine(bin, "files")))
            {
                token.ThrowIfCancellationRequested();

                var (originalPath, deletedAt) = ReadInfo(IOPath.Combine(info, item.Name + ".trashinfo"), bin);
                yield return new TrashItem(item, originalPath, deletedAt);
            }
        }
    }

    private static List<FileSystemInfo> Items(string files)
    {
        try
        {
            var folder = new DirectoryInfo(files);
            return folder.Exists ? folder.EnumerateFileSystemInfos("*", AllEntries).ToList() : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.WriteLine($"Can't list trash folder '{files}': {ex.Message}");
            return [];
        }
    }

    /// <summary>
    /// The [Trash Info] group: Path (URL-encoded; relative to the drive's top folder in a drive's trash)
    /// and DeletionDate (local time, YYYY-MM-DDThh:mm:ss).
    /// </summary>
    private static (string? OriginalPath, DateTime? DeletedAt) ReadInfo(string infoFile, string bin)
    {
        string[] lines;
        try
        {
            if (!IOFile.Exists(infoFile))
                return (null, null);

            lines = IOFile.ReadAllLines(infoFile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.WriteLine($"Can't read trash info '{infoFile}': {ex.Message}");
            return (null, null);
        }

        string? originalPath = null;
        DateTime? deletedAt = null;
        var inGroup = false;

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            if (line.StartsWith('['))
            {
                inGroup = line == "[Trash Info]";
                continue;
            }

            var separator = line.IndexOf('=');
            if (!inGroup || separator <= 0)
                continue;

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();

            switch (key)
            {
                case "Path":
                    originalPath = DecodePath(value, bin);
                    break;
                case "DeletionDate" when DateTime.TryParseExact(value, "yyyy-MM-dd'T'HH:mm:ss",
                    CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var date):
                    deletedAt = date;
                    break;
            }
        }

        return (originalPath, deletedAt);
    }

    private static string? DecodePath(string value, string bin)
    {
        var path = Uri.UnescapeDataString(value);
        if (path.Length == 0)
            return null;

        if (IOPath.IsPathRooted(path))
            return path;

        // A drive's trash stores paths relative to the drive's top folder: $topdir/.Trash-$uid or $topdir/.Trash/$uid
        return TopFolder(bin) is { } top ? IOPath.Combine(top, path) : null;
    }

    private static string? TopFolder(string bin)
    {
        var parent = IOPath.GetDirectoryName(bin);
        return parent is not null && IOPath.GetFileName(parent) == ".Trash"
            ? IOPath.GetDirectoryName(parent)
            : parent;
    }
}
