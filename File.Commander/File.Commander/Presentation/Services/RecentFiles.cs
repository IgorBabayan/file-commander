using System.Globalization;
using System.Xml;

namespace File.Commander.Presentation.Services;

/// <summary>
/// Recently used files as the desktop records them: the freedesktop recently-used.xbel (GTK, GNOME and most apps)
/// and KDE's RecentDocuments folder. Read-only: a file opened through xdg-open is recorded by the app that opens it.
/// </summary>
public static class RecentFiles
{
    /// <summary>Plenty for a list people scan by eye, and keeps the page quick to build.</summary>
    private const int MaxItems = 200;

    /// <summary>
    /// Local files and folders that still exist, most recently used first. A source that is missing,
    /// unreadable or half-written is skipped, so this never throws for it.
    /// </summary>
    public static IReadOnlyList<FileSystemInfo> Read(CancellationToken token)
    {
        var used = new Dictionary<string, DateTime>(StringComparer.Ordinal);
        ReadXbel(used, token);
        ReadKdeRecentDocuments(used, token);

        var result = new List<FileSystemInfo>();
        foreach (var (path, _) in used.OrderByDescending(pair => pair.Value))
        {
            token.ThrowIfCancellationRequested();

            // Deleted, moved, or on a drive that isn't mounted now
            if (Directory.Exists(path))
                result.Add(new DirectoryInfo(path));
            else if (IOFile.Exists(path))
                result.Add(new FileInfo(path));

            if (result.Count == MaxItems)
                break;
        }

        return result;
    }

    private static string DataHome
    {
        get
        {
            var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            return string.IsNullOrEmpty(dataHome)
                ? IOPath.Combine(SystemLocations.HomeDirectory, ".local", "share")
                : dataHome;
        }
    }

    /// <summary>&lt;bookmark href="file:///…" added="…" modified="…" visited="…"&gt;, times in ISO 8601.</summary>
    private static void ReadXbel(Dictionary<string, DateTime> used, CancellationToken token)
    {
        var path = IOPath.Combine(DataHome, "recently-used.xbel");
        if (!IOFile.Exists(path))
            return;

        try
        {
            using var stream = IOFile.OpenRead(path);
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Ignore,
                XmlResolver = null,
                IgnoreComments = true,
                IgnoreWhitespace = true,
            };

            using var reader = XmlReader.Create(stream, settings);
            while (reader.Read())
            {
                token.ThrowIfCancellationRequested();

                if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "bookmark")
                    continue;

                if (ToLocalPath(reader.GetAttribute("href")) is not { } local)
                    continue;

                var time = Latest(reader.GetAttribute("visited"), reader.GetAttribute("modified"),
                    reader.GetAttribute("added"));
                Remember(used, local, time);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or XmlException)
        {
            // Keep what was read before the error; the KDE source may still have more
            Trace.WriteLine($"Can't read '{path}': {ex.Message}");
        }
    }

    /// <summary>One .desktop file per document with a URL= key; the file's own time is when it was used.</summary>
    private static void ReadKdeRecentDocuments(Dictionary<string, DateTime> used, CancellationToken token)
    {
        var folder = new DirectoryInfo(IOPath.Combine(DataHome, "RecentDocuments"));
        if (!folder.Exists)
            return;

        try
        {
            foreach (var file in folder.EnumerateFiles("*.desktop"))
            {
                token.ThrowIfCancellationRequested();

                if (ReadDesktopUrl(file.FullName) is { } local)
                    Remember(used, local, file.LastWriteTimeUtc);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.WriteLine($"Can't read '{folder.FullName}': {ex.Message}");
        }
    }

    private static string? ReadDesktopUrl(string path)
    {
        try
        {
            foreach (var line in IOFile.ReadLines(path))
            {
                var eq = line.IndexOf('=');
                if (eq <= 0)
                    continue;

                var key = line[..eq].Trim();
                if (key is not ("URL" or "URL[$e]"))
                    continue;

                var value = line[(eq + 1)..].Trim();
                return value.StartsWith('/') ? Locations.Normalize(value) : ToLocalPath(value);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Removed while being read: skip that one
        }

        return null;
    }

    /// <summary>"file:///home/me/My%20Notes.txt" → "/home/me/My Notes.txt". Null for anything that isn't a local file.</summary>
    private static string? ToLocalPath(string? uri)
    {
        if (string.IsNullOrEmpty(uri)
            || !Uri.TryCreate(uri, UriKind.Absolute, out var parsed)
            || !parsed.IsFile
            || parsed.IsUnc)
            return null;

        return Locations.Normalize(parsed.LocalPath);
    }

    private static DateTime Latest(params string?[] values)
    {
        var latest = DateTime.MinValue;
        foreach (var value in values)
        {
            if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var time)
                && time.UtcDateTime > latest)
                latest = time.UtcDateTime;
        }

        return latest;
    }

    // The same file can be in both sources: keep its latest use
    private static void Remember(Dictionary<string, DateTime> used, string path, DateTime time)
    {
        if (!used.TryGetValue(path, out var known) || time > known)
            used[path] = time;
    }
}
