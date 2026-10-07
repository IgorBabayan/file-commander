using System.Text;
using System.Text.RegularExpressions;

namespace File.Commander.Presentation.Services;

/// <summary>
/// The MIME type of a file, from the freedesktop.org shared-mime-info database ($XDG_DATA_HOME/mime and
/// $XDG_DATA_DIRS/mime): its name patterns (globs2), aliases and subclasses. Files the patterns don't know
/// are told apart by their first bytes, so a README or a .conf file is text/plain. Also the generic icon of each
/// type (generic-icons), which tells its family: archive, document, spreadsheet... Read once, thread-safe.
/// </summary>
public sealed class MimeDatabase
{
    private const string TextPlain = "text/plain";
    private const string OctetStream = "application/octet-stream";
    private const int SniffLength = 4096;

    private static readonly Lazy<MimeDatabase> Shared = new(() => Load(XdgDirectories.DataDirectories()));

    // Highest weight first, then longest pattern: "*.tar.gz" beats "*.gz"
    private readonly List<Glob> _globs;
    private readonly Dictionary<string, string> _aliases;
    private readonly Dictionary<string, List<string>> _parents;
    private readonly Dictionary<string, string> _genericIcons;

    private MimeDatabase(List<Glob> globs, Dictionary<string, string> aliases, Dictionary<string, List<string>> parents,
        Dictionary<string, string> genericIcons)
    {
        _globs = globs;
        _aliases = aliases;
        _parents = parents;
        _genericIcons = genericIcons;
    }

    /// <summary>The database of this system. Empty (everything is sniffed) when shared-mime-info isn't installed.</summary>
    public static MimeDatabase Default => Shared.Value;

    /// <summary>No name pattern was read: shared-mime-info is missing.</summary>
    public bool IsEmpty => _globs.Count == 0;

    /// <param name="dataDirectories">Highest precedence first, like $XDG_DATA_HOME then $XDG_DATA_DIRS.</param>
    public static MimeDatabase Load(IEnumerable<string> dataDirectories)
    {
        var globs = new List<Glob>();
        var aliases = new Dictionary<string, string>(StringComparer.Ordinal);
        var parents = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var genericIcons = new Dictionary<string, string>(StringComparer.Ordinal);

        // Lower precedence first, so a user's own patterns and hierarchy override the system's
        foreach (var directory in dataDirectories.Reverse())
        {
            var mime = IOPath.Combine(directory, "mime");
            ReadGlobs(IOPath.Combine(mime, "globs2"), globs);

            foreach (var (alias, canonical) in ReadPairs(IOPath.Combine(mime, "aliases")))
                aliases[alias] = canonical;

            foreach (var (child, parent) in ReadPairs(IOPath.Combine(mime, "subclasses")))
            {
                if (!parents.TryGetValue(child, out var list))
                    parents[child] = list = [];
                if (!list.Contains(parent))
                    list.Add(parent);
            }

            foreach (var (type, icon) in ReadPairs(IOPath.Combine(mime, "generic-icons"), ':'))
                genericIcons[type] = icon;
        }

        globs.Sort((a, b) => b.Weight != a.Weight
            ? b.Weight.CompareTo(a.Weight)
            : b.Pattern.Length.CompareTo(a.Pattern.Length));

        return new MimeDatabase(globs, aliases, parents, genericIcons);
    }

    /// <summary>The type of the file at <paramref name="path"/>: by name, else by content.</summary>
    public string TypeOf(string path)
        => TypeOfName(IOPath.GetFileName(path)) ?? TypeOfContent(path);

    /// <summary>
    /// The type of the file at <paramref name="path"/> listed as <paramref name="name"/> (the trash keeps files under
    /// other names): by name, else by content.
    /// </summary>
    public string TypeOf(string path, string name)
        => TypeOfName(name) ?? TypeOfContent(path);

    /// <summary>
    /// The icon that stands for the type's family, as GIO's g_content_type_get_generic_icon_name gives it: from
    /// generic-icons ("package-x-generic" for application/zip, "x-office-spreadsheet" for .xlsx), else
    /// "&lt;media&gt;-x-generic" ("text-x-generic" for any text/*).
    /// </summary>
    public string GenericIconName(string type)
    {
        type = Canonical(type);
        if (_genericIcons.TryGetValue(type, out var icon))
            return icon;

        var slash = type.IndexOf('/');
        return (slash < 0 ? type : type[..slash]) + "-x-generic";
    }

    /// <summary>The type the name patterns give <paramref name="name"/>, or null when none matches.</summary>
    public string? TypeOfName(string name)
    {
        foreach (var glob in _globs)
        {
            if (glob.Matches(name))
                return Canonical(glob.Type);
        }

        return null;
    }

    /// <summary>
    /// <paramref name="type"/> and the types it is a kind of, most specific first: application/x-shellscript,
    /// then application/x-executable and text/plain. Every text/* is a kind of text/plain.
    /// </summary>
    public IReadOnlyList<string> Hierarchy(string type)
    {
        var result = new List<string>();
        var queue = new Queue<string>();
        queue.Enqueue(Canonical(type));

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (result.Contains(current))
                continue;

            result.Add(current);

            if (_parents.TryGetValue(current, out var parents))
            {
                foreach (var parent in parents)
                    queue.Enqueue(Canonical(parent));
            }

            if (current.StartsWith("text/", StringComparison.Ordinal) && current != TextPlain)
                queue.Enqueue(TextPlain);
        }

        return result;
    }

    /// <summary>Text: the editor is the expected app for it.</summary>
    public bool IsText(string type) => Hierarchy(type).Contains(TextPlain);

    private string Canonical(string type) => _aliases.TryGetValue(type, out var canonical) ? canonical : type;

    /// <summary>
    /// The first bytes, for names no pattern knows ("README", ".bashrc", "notes"): a few binary signatures,
    /// else text when there is no NUL byte and it reads as UTF-8. An empty file is text: it opens in the editor.
    /// </summary>
    private static string TypeOfContent(string path)
    {
        byte[] head;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            head = new byte[SniffLength];
            var length = 0;
            int read;
            while (length < head.Length && (read = stream.Read(head, length, head.Length - length)) > 0)
                length += read;

            Array.Resize(ref head, length);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OctetStream;
        }

        if (head.Length == 0)
            return TextPlain;

        ReadOnlySpan<byte> span = head;
        if (span.StartsWith("\u007fELF"u8)) return "application/x-executable";
        if (span.StartsWith("%PDF-"u8)) return "application/pdf";
        if (span.StartsWith(new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G' })) return "image/png";
        if (span.StartsWith(new byte[] { 0xFF, 0xD8, 0xFF })) return "image/jpeg";
        if (span.StartsWith("GIF8"u8)) return "image/gif";
        if (span.StartsWith("PK\u0003\u0004"u8)) return "application/zip";
        if (span.StartsWith(new byte[] { 0x1F, 0x8B })) return "application/gzip";
        if (span.StartsWith("#!"u8)) return "application/x-shellscript";

        return span.Contains((byte)0) || !IsUtf8(span) ? OctetStream : TextPlain;
    }

    /// <summary>Valid UTF-8, allowing a character cut off by the end of the sniffed bytes.</summary>
    private static bool IsUtf8(ReadOnlySpan<byte> bytes)
    {
        var decoder = new UTF8Encoding(false, throwOnInvalidBytes: true).GetDecoder();
        var chars = new char[bytes.Length + 1];
        try
        {
            // flush: false keeps an incomplete last character instead of rejecting it
            decoder.GetChars(bytes, chars, flush: false);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    /// <summary>globs2: "weight:type:pattern[:flags]" per line. "__NOGLOBS__" drops a type's lower-precedence patterns.</summary>
    private static void ReadGlobs(string file, List<Glob> globs)
    {
        foreach (var line in ReadLines(file))
        {
            var fields = line.Split(':');
            if (fields.Length < 3 || !int.TryParse(fields[0], out var weight))
                continue;

            var type = fields[1];
            var pattern = fields[2];
            if (pattern == "__NOGLOBS__")
            {
                globs.RemoveAll(glob => glob.Type == type);
                continue;
            }

            var caseSensitive = fields.Length > 3 && fields[3].Split(',').Contains("cs");
            globs.Add(new Glob(weight, type, pattern, caseSensitive));
        }
    }

    /// <summary>
    /// aliases and subclasses: two types per line, separated by a space. generic-icons: a type and an icon name,
    /// separated by a colon.
    /// </summary>
    private static IEnumerable<(string, string)> ReadPairs(string file, char separator = ' ')
    {
        foreach (var line in ReadLines(file))
        {
            var split = line.IndexOf(separator);
            if (split > 0)
                yield return (line[..split], line[(split + 1)..].Trim());
        }
    }

    private static IEnumerable<string> ReadLines(string file)
    {
        string[] lines;
        try
        {
            lines = IOFile.Exists(file) ? IOFile.ReadAllLines(file) : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.WriteLine($"Can't read '{file}': {ex.Message}");
            yield break;
        }

        foreach (var line in lines)
        {
            if (line.Length > 0 && line[0] != '#')
                yield return line;
        }
    }

    /// <summary>One pattern: a literal name, "*.ext" (the common case, matched without a regex), or any glob.</summary>
    private sealed class Glob
    {
        private readonly bool _caseSensitive;
        private readonly string? _suffix;
        private readonly Regex? _regex;

        public Glob(int weight, string type, string pattern, bool caseSensitive)
        {
            Weight = weight;
            Type = type;
            Pattern = pattern;
            _caseSensitive = caseSensitive;

            if (pattern.Length > 1 && pattern[0] == '*' && pattern.IndexOfAny(['*', '?', '['], 1) < 0)
            {
                _suffix = pattern[1..];
            }
            else if (pattern.IndexOfAny(['*', '?', '[']) >= 0)
            {
                _regex = new Regex("^" + GlobToRegex(pattern) + "$",
                    RegexOptions.CultureInvariant | (caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase));
            }
        }

        public int Weight { get; }

        public string Type { get; }

        public string Pattern { get; }

        public bool Matches(string name)
        {
            var comparison = _caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            if (_suffix is not null)
                return name.Length > _suffix.Length && name.EndsWith(_suffix, comparison);

            return _regex?.IsMatch(name) ?? string.Equals(name, Pattern, comparison);
        }

        private static string GlobToRegex(string pattern)
        {
            var regex = new StringBuilder();
            for (var i = 0; i < pattern.Length; i++)
            {
                var c = pattern[i];
                switch (c)
                {
                    case '*':
                        regex.Append(".*");
                        break;
                    case '?':
                        regex.Append('.');
                        break;
                    case '[' when pattern.IndexOf(']', i + 1) is var end and > 0:
                        var set = pattern[(i + 1)..end];
                        regex.Append('[').Append(set.StartsWith('!') ? "^" + Regex.Escape(set[1..]) : Regex.Escape(set)).Append(']');
                        i = end;
                        break;
                    default:
                        regex.Append(Regex.Escape(c.ToString()));
                        break;
                }
            }

            return regex.ToString();
        }
    }
}

/// <summary>The XDG base directories (freedesktop.org Base Directory spec), highest precedence first.</summary>
public static class XdgDirectories
{
    /// <summary>$XDG_DATA_HOME, then $XDG_DATA_DIRS (default /usr/local/share and /usr/share).</summary>
    public static IReadOnlyList<string> DataDirectories()
        => Combine(Home("XDG_DATA_HOME", ".local/share"), "XDG_DATA_DIRS", "/usr/local/share:/usr/share");

    /// <summary>$XDG_CONFIG_HOME, then $XDG_CONFIG_DIRS (default /etc/xdg).</summary>
    public static IReadOnlyList<string> ConfigDirectories()
        => Combine(Home("XDG_CONFIG_HOME", ".config"), "XDG_CONFIG_DIRS", "/etc/xdg");

    /// <summary>$XDG_CURRENT_DESKTOP in lower case, e.g. ["hyprland"] or ["ubuntu", "gnome"].</summary>
    public static IReadOnlyList<string> CurrentDesktops()
        => (Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? string.Empty)
            .Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(desktop => desktop.ToLowerInvariant())
            .ToList();

    private static string Home(string variable, string fallback)
    {
        var value = Environment.GetEnvironmentVariable(variable);
        return !string.IsNullOrEmpty(value) && IOPath.IsPathRooted(value)
            ? value
            : IOPath.Combine(SystemLocations.HomeDirectory, fallback);
    }

    private static IReadOnlyList<string> Combine(string home, string variable, string fallback)
    {
        var value = Environment.GetEnvironmentVariable(variable);
        var others = (string.IsNullOrEmpty(value) ? fallback : value)
            .Split(':', StringSplitOptions.RemoveEmptyEntries)
            .Where(IOPath.IsPathRooted);

        return new[] { home }.Concat(others).Distinct(StringComparer.Ordinal).ToList();
    }
}
