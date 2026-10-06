using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace File.Commander.Presentation.Services;

/// <summary>A date filter of the search: a range relative to today, or one picked on the calendar.</summary>
public enum SearchDateRange
{
    Any,
    Today,
    Yesterday,
    Last7Days,
    Last30Days,
    LastYear,
    Custom,
}

/// <summary>
/// When an item was created or modified. Relative ranges are resolved when the search runs, so going back to a
/// search of "the last 7 days" tomorrow still means the last 7 days.
/// </summary>
/// <param name="From">Custom: the first day, included. Null: no lower bound.</param>
/// <param name="To">Custom: the last day, included. Null: no upper bound.</param>
public sealed record SearchDateFilter(SearchDateRange Range, DateOnly? From = null, DateOnly? To = null)
{
    private const string DateFormat = "yyyy-MM-dd";

    public static SearchDateFilter Any { get; } = new(SearchDateRange.Any);

    /// <summary>Filters something: not "Any time", nor a custom range without a single date.</summary>
    public bool IsSet => Range switch
    {
        SearchDateRange.Any => false,
        SearchDateRange.Custom => From is not null || To is not null,
        _ => true,
    };

    /// <summary>The local times it allows, as [Start, End): either side may be open.</summary>
    public (DateTime? Start, DateTime? End) Resolve(DateTime now)
    {
        var today = now.Date;
        switch (Range)
        {
            case SearchDateRange.Today:
                return (today, today.AddDays(1));
            case SearchDateRange.Yesterday:
                return (today.AddDays(-1), today);
            case SearchDateRange.Last7Days:
                return (today.AddDays(-6), today.AddDays(1));
            case SearchDateRange.Last30Days:
                return (today.AddDays(-29), today.AddDays(1));
            case SearchDateRange.LastYear:
                return (today.AddYears(-1).AddDays(1), today.AddDays(1));
            case SearchDateRange.Custom:
            {
                var (from, to) = (From, To);

                // Picked the wrong way round: still the days between them
                if (from is { } a && to is { } b && a > b)
                    (from, to) = (b, a);

                return (from?.ToDateTime(TimeOnly.MinValue), to?.ToDateTime(TimeOnly.MinValue).AddDays(1));
            }
            default:
                return (null, null);
        }
    }

    /// <summary>"7d", "today", "2024-01-31..2024-02-29", "..2024-02-29"… Read back by <see cref="Parse"/>.</summary>
    public string Serialize() => Range switch
    {
        SearchDateRange.Today => "today",
        SearchDateRange.Yesterday => "yesterday",
        SearchDateRange.Last7Days => "7d",
        SearchDateRange.Last30Days => "30d",
        SearchDateRange.LastYear => "year",
        SearchDateRange.Custom => $"{Format(From)}..{Format(To)}",
        _ => "any",
    };

    /// <summary>What <see cref="Serialize"/> wrote. Anything unreadable is <see cref="Any"/>.</summary>
    public static SearchDateFilter Parse(string? text)
    {
        switch (text)
        {
            case "today":
                return new SearchDateFilter(SearchDateRange.Today);
            case "yesterday":
                return new SearchDateFilter(SearchDateRange.Yesterday);
            case "7d":
                return new SearchDateFilter(SearchDateRange.Last7Days);
            case "30d":
                return new SearchDateFilter(SearchDateRange.Last30Days);
            case "year":
                return new SearchDateFilter(SearchDateRange.LastYear);
        }

        var dots = text?.IndexOf("..", StringComparison.Ordinal) ?? -1;
        if (text is null || dots < 0)
            return Any;

        var filter = new SearchDateFilter(SearchDateRange.Custom, ParseDate(text[..dots]), ParseDate(text[(dots + 2)..]));
        return filter.IsSet ? filter : Any;
    }

    private static string Format(DateOnly? date) => date?.ToString(DateFormat, CultureInfo.InvariantCulture) ?? string.Empty;

    private static DateOnly? ParseDate(string text)
        => DateOnly.TryParseExact(text, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;
}

/// <summary>
/// What to look for and where. A search results page lives at <see cref="ToLocation"/>
/// ("search://?q=report&amp;in=%2Fhome%2Fme…"), so Back, Forward, Refresh and new tabs simply run it again.
/// </summary>
/// <param name="Text">Matched against names (wildcards * and ? allowed) and/or file contents. Empty: no text to match.</param>
/// <param name="InNames">The text is looked for in names.</param>
/// <param name="InContents">The text is looked for inside files. Names win when neither is set.</param>
/// <param name="Extensions">Only files with one of these extensions, without the dot ("pdf", "tar.gz"). Empty: any item.</param>
/// <param name="Folder">Where to look. Always a real folder, never a virtual location.</param>
/// <param name="IncludeSubfolders">Looks in every folder below <paramref name="Folder"/>, not only in it.</param>
public sealed record SearchQuery(
    string Text,
    bool InNames,
    bool InContents,
    IReadOnlyList<string> Extensions,
    SearchDateFilter Created,
    SearchDateFilter Modified,
    string Folder,
    bool IncludeSubfolders)
{
    public const string Scheme = "search://";

    private static readonly char[] ExtensionSeparators = [',', ';', ' ', '\t'];

    /// <summary>Something to look for: a text, an extension or a date. A search without any would list everything.</summary>
    public bool HasCriteria => Text.Length > 0 || Extensions.Count > 0 || Created.IsSet || Modified.IsSet;

    /// <summary>Shown on the tab and in the window title.</summary>
    public string Title => Text.Length > 0 ? $"Search: {Text}" : "Search results";

    /// <summary>The last breadcrumb of the address bar, after the folder searched in.</summary>
    public string ShortTitle => Text.Length > 0 ? $"Search “{Text}”" : "Search results";

    /// <summary>The tab's tooltip.</summary>
    public string Description => IncludeSubfolders ? $"Search in {Folder} and its subfolders" : $"Search in {Folder}";

    /// <summary>"pdf, docx" → ["pdf", "docx"]: commas, semicolons and spaces separate; dots and stars are dropped.</summary>
    public static IReadOnlyList<string> ParseExtensions(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        return text.Split(ExtensionSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(extension => extension.TrimStart('*').TrimStart('.').ToLowerInvariant())
            .Where(extension => extension.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    public string ToLocation()
    {
        var parts = new List<string>();

        void Add(string key, string value) => parts.Add($"{key}={Uri.EscapeDataString(value)}");

        if (Text.Length > 0)
            Add("q", Text);
        Add("in", Folder);
        if (!InNames)
            Add("names", "0");
        if (InContents)
            Add("contents", "1");
        if (Extensions.Count > 0)
            Add("ext", string.Join(',', Extensions));
        if (Created.IsSet)
            Add("created", Created.Serialize());
        if (Modified.IsSet)
            Add("modified", Modified.Serialize());
        if (!IncludeSubfolders)
            Add("sub", "0");

        return $"{Scheme}?{string.Join('&', parts)}";
    }

    /// <summary>The search at <paramref name="location"/>; null for every other location, or one without a folder.</summary>
    public static SearchQuery? TryParse(string? location)
    {
        if (location is null || !location.StartsWith(Scheme, StringComparison.Ordinal))
            return null;

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in location[Scheme.Length..].TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var equals = part.IndexOf('=');
            if (equals <= 0)
                continue;

            values[part[..equals]] = Uri.UnescapeDataString(part[(equals + 1)..]);
        }

        if (!values.TryGetValue("in", out var folder) || !IOPath.IsPathRooted(folder))
            return null;

        return new SearchQuery(
            values.GetValueOrDefault("q")?.Trim() ?? string.Empty,
            values.GetValueOrDefault("names") != "0",
            values.GetValueOrDefault("contents") == "1",
            ParseExtensions(values.GetValueOrDefault("ext")),
            SearchDateFilter.Parse(values.GetValueOrDefault("created")),
            SearchDateFilter.Parse(values.GetValueOrDefault("modified")),
            Locations.Normalize(folder),
            values.GetValueOrDefault("sub") != "0");
    }
}

/// <summary>
/// Walks the folder of a <see cref="SearchQuery"/> and yields what matches, shallow folders first. Unreadable folders
/// are skipped, symlinked folders aren't followed (no loops), and the kernel's pseudo filesystems (/proc, /sys…)
/// are left out unless the search starts inside one. Run it on a background thread.
/// </summary>
public static class FileSearch
{
    /// <summary>Bigger files aren't read for their contents: it would take too long and they are rarely text.</summary>
    private const long MaxContentBytes = 64L * 1024 * 1024;

    // Enough to tell text from binary: a text file has no NUL byte in its first block
    private const int SniffBytes = 8 * 1024;

    private const int ChunkChars = 64 * 1024;

    private static readonly string[] PseudoFolders = ["/proc", "/sys", "/dev", "/run"];

    /// <param name="includeHidden">Dot files and folders too, as with Show hidden files.</param>
    /// <param name="skippedFolders">Folders never entered, e.g. the mount points of external drives.</param>
    /// <exception cref="DirectoryNotFoundException">The folder to search in doesn't exist.</exception>
    public static IEnumerable<FileSystemInfo> Find(SearchQuery query, bool includeHidden,
        IReadOnlyCollection<string> skippedFolders, CancellationToken token)
    {
        var root = new DirectoryInfo(query.Folder);
        if (!root.Exists)
            throw new DirectoryNotFoundException(query.Folder);

        var matcher = new Matcher(query, DateTime.Now);
        var options = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            AttributesToSkip = includeHidden ? 0 : FileAttributes.Hidden | FileAttributes.System,
        };

        // A skipped folder that holds the search folder doesn't count: the user asked to look in there
        var skipped = PseudoFolders.Concat(skippedFolders)
            .Select(Locations.Normalize)
            .Where(folder => !FileOperations.IsSameOrInside(root.FullName, folder))
            .ToHashSet(StringComparer.Ordinal);

        var pending = new Queue<DirectoryInfo>();
        pending.Enqueue(root);

        while (pending.TryDequeue(out var folder))
        {
            token.ThrowIfCancellationRequested();

            foreach (var item in ListChildren(folder, options))
            {
                token.ThrowIfCancellationRequested();

                if (query.IncludeSubfolders && item is DirectoryInfo subfolder
                                            && (subfolder.Attributes & FileAttributes.ReparsePoint) == 0
                                            && !skipped.Contains(subfolder.FullName))
                    pending.Enqueue(subfolder);

                if (matcher.Matches(item, token))
                    yield return item;
            }
        }
    }

    /// <summary>A folder that can't be read (gone, no permission) has nothing to offer: it is skipped, not reported.</summary>
    private static List<FileSystemInfo> ListChildren(DirectoryInfo folder, EnumerationOptions options)
    {
        try
        {
            return folder.EnumerateFileSystemInfos("*", options).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return [];
        }
    }

    /// <summary>Checks the cheap criteria (extension, dates, name) before reading a file.</summary>
    private sealed class Matcher
    {
        private readonly string _text;
        private readonly Regex? _wildcard;
        private readonly bool _inNames;
        private readonly bool _inContents;
        private readonly string[] _extensions;
        private readonly (DateTime? Start, DateTime? End) _created;
        private readonly (DateTime? Start, DateTime? End) _modified;

        public Matcher(SearchQuery query, DateTime now)
        {
            _text = query.Text;
            _inContents = query.InContents;
            _inNames = query.InNames || !query.InContents;
            _extensions = query.Extensions.Select(extension => "." + extension).ToArray();
            _created = query.Created.Resolve(now);
            _modified = query.Modified.Resolve(now);

            // "*.txt", "IMG_20??": the whole name has to fit; any other text may be anywhere in the name
            if (_text.IndexOfAny(['*', '?']) >= 0)
            {
                var pattern = "^" + Regex.Escape(_text).Replace(@"\*", ".*").Replace(@"\?", ".") + "$";
                _wildcard = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            }
        }

        public bool Matches(FileSystemInfo item, CancellationToken token)
        {
            // Extensions belong to files: asking for "pdf" never lists a folder
            if (_extensions.Length > 0 && (item is not FileInfo
                                           || !_extensions.Any(e => item.Name.EndsWith(e, StringComparison.OrdinalIgnoreCase))))
                return false;

            if (!InRange(item, _created, static i => i.CreationTime)
                || !InRange(item, _modified, static i => i.LastWriteTime))
                return false;

            if (_text.Length == 0)
                return true;

            if (_inNames && NameMatches(item.Name))
                return true;

            return _inContents && item is FileInfo file && Contains(file, _text, token);
        }

        private bool NameMatches(string name) => _wildcard?.IsMatch(name)
                                                 ?? name.Contains(_text, StringComparison.OrdinalIgnoreCase);

        private static bool InRange(FileSystemInfo item, (DateTime? Start, DateTime? End) range,
            Func<FileSystemInfo, DateTime> read)
        {
            if (range is { Start: null, End: null })
                return true;

            DateTime time;
            try
            {
                time = read(item);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A broken link or a file that vanished: its date is unknown, so it can't be in the range
                return false;
            }

            return (range.Start is not { } start || time >= start) && (range.End is not { } end || time < end);
        }
    }

    /// <summary>
    /// Whether <paramref name="text"/> is in the file, ignoring case. Binary files (a NUL byte in the first block),
    /// empty ones and very big ones aren't read. Never throws but for cancellation.
    /// </summary>
    private static bool Contains(FileInfo file, string text, CancellationToken token)
    {
        try
        {
            // Length 0 also covers what isn't a regular file (FIFOs, devices, sockets): opening those could block
            var length = file.Length;
            if (length == 0 || length > MaxContentBytes)
                return false;

            using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
                SniffBytes, FileOptions.SequentialScan);

            if (IsBinary(stream))
                return false;

            stream.Position = 0;
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, ChunkChars);

            // The end of each chunk is kept, so a match split between two chunks is still found
            var overlap = text.Length - 1;
            var buffer = new char[Math.Max(ChunkChars, text.Length * 2)];
            var kept = 0;
            while (true)
            {
                token.ThrowIfCancellationRequested();

                var read = reader.Read(buffer, kept, buffer.Length - kept);
                if (read == 0)
                    return false;

                var filled = kept + read;
                if (new ReadOnlySpan<char>(buffer, 0, filled).IndexOf(text.AsSpan(), StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;

                kept = Math.Min(overlap, filled);
                Array.Copy(buffer, filled - kept, buffer, 0, kept);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return false;
        }
    }

    /// <summary>A NUL byte in the first block, unless a UTF-16 byte order mark says it's text.</summary>
    private static bool IsBinary(FileStream stream)
    {
        var head = new byte[SniffBytes];
        var count = stream.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);

        if (count >= 2 && ((head[0] == 0xFF && head[1] == 0xFE) || (head[0] == 0xFE && head[1] == 0xFF)))
            return false;

        return Array.IndexOf(head, (byte)0, 0, count) >= 0;
    }
}
