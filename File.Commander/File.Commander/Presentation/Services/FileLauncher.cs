using System.ComponentModel;
using System.Text;

namespace File.Commander.Presentation.Services;

/// <summary>An installed application, from its .desktop file.</summary>
/// <param name="Id">The desktop file ID, e.g. "org.gnome.TextEditor.desktop".</param>
/// <param name="NoDisplay">NoDisplay=true: a handler not meant to be listed among the apps (e.g. a URL opener).</param>
public sealed record DesktopApp(
    string Id,
    string Path,
    string Name,
    string Exec,
    string? Icon,
    string? WorkingDirectory,
    bool Terminal,
    IReadOnlyList<string> Categories,
    bool NoDisplay = false);

/// <summary>
/// Opens files in the application registered for their type, as file managers do, following the freedesktop.org
/// MIME Applications spec: the defaults of the mimeapps.list files (desktop-specific ones first), then the user's
/// added associations, then every installed application that declares the type. More general types are tried
/// after the file's own (a C source is text). Nothing is ever executed: a script opens in the editor.
/// </summary>
/// <remarks>
/// Doesn't go through xdg-open: outside GNOME and KDE it falls back to the web browser for any type it can't
/// settle, and browsers declare text, PDF and image types too. Without a registered default, an editor is
/// preferred for text, and browsers come last for anything but web pages.
/// </remarks>
public static class FileLauncher
{
    private const string DirectoryType = "inode/directory";

    private static readonly HashSet<string> WebTypes = new(StringComparer.Ordinal)
    {
        "text/html", "application/xhtml+xml", "application/x-mswinurl", "x-scheme-handler/http",
        "x-scheme-handler/https",
    };

    /// <summary>Opens <paramref name="path"/>. Returns what went wrong, or null.</summary>
    public static string? Open(string path)
    {
        var mime = MimeDatabase.Default;
        var type = mime.TypeOf(path);
        var app = FindApp(mime, type, Applications.Load());

        if (app is null)
        {
            // Without shared-mime-info nothing can be looked up here: leave it to the system
            return mime.IsEmpty ? OpenWithXdgOpen(path) : $"No app is set to open files of type {type}.";
        }

        return Launch(app, path);
    }

    /// <summary>The app that opens files of <paramref name="type"/>, or null when none is registered.</summary>
    public static DesktopApp? FindApp(MimeDatabase mime, string type, Applications applications)
    {
        var hierarchy = mime.Hierarchy(type);
        var lists = MimeAppsLists.Load();

        // A file never opens in a file manager. GNOME Files, Nemo and others declare archive types, so a double click
        // on a .zip would extract it in another file manager and open its window: archives this app can extract are
        // extracted in the Action center, the others open in an archive app.
        var skipFileManagers = !hierarchy.Contains(DirectoryType);
        bool Usable(DesktopApp app) => !skipFileManagers || !IsFileManager(app);

        // 1. A default set by the user, the desktop or the distribution, for the type itself first.
        //    One that isn't installed is skipped.
        foreach (var current in hierarchy)
        {
            foreach (var id in lists.Defaults(current))
            {
                if (applications.Find(id) is { } app && Usable(app))
                    return app;
            }
        }

        // 2. What declares the type: the user's added associations, then every app's MimeType= (mimeinfo.cache).
        //    Within a type: an editor first for text, browsers last for anything that isn't a web page.
        var isText = mime.IsText(type);
        var isWeb = hierarchy.Any(WebTypes.Contains);
        foreach (var current in hierarchy)
        {
            var removed = lists.Removed(current);
            var candidates = lists.Added(current)
                .Concat(applications.Declaring(current))
                .Where(id => !removed.Contains(id))
                .Distinct(StringComparer.Ordinal)
                .Select(applications.Find)
                .OfType<DesktopApp>()
                .Where(Usable)
                .ToList();

            var best = candidates
                .Select((app, index) => (App: app, Index: index))
                .OrderBy(candidate => Rank(candidate.App, isText, isWeb))
                .ThenBy(candidate => candidate.Index)
                .Select(candidate => candidate.App)
                .FirstOrDefault();

            if (best is not null)
                return best;
        }

        return null;
    }

    /// <summary>
    /// A file manager, by its categories. Not by inode/directory: editors such as VS Code declare it too, and must
    /// still open text files.
    /// </summary>
    private static bool IsFileManager(DesktopApp app) => app.Categories.Contains("FileManager");

    private static int Rank(DesktopApp app, bool isText, bool isWeb)
    {
        if (isText && app.Categories.Contains("TextEditor"))
            return 0;

        return !isWeb && app.Categories.Contains("WebBrowser") ? 2 : 1;
    }

    /// <summary>Runs <paramref name="app"/>'s Exec= line with <paramref name="path"/>, in a terminal if it asks for one.</summary>
    public static string? Launch(DesktopApp app, string path)
    {
        var command = DesktopExec.Expand(app, path);
        if (command.Count == 0)
            return $"{app.Name} can't be started: its desktop file has no command.";

        if (app.Terminal)
        {
            if (Terminals.Wrap(command) is not { } wrapped)
                return $"{app.Name} runs in a terminal, and no terminal app was found. Set $TERMINAL to yours.";

            command = wrapped;
        }

        var start = new ProcessStartInfo(command[0])
        {
            UseShellExecute = false,
            WorkingDirectory = app.WorkingDirectory is { } directory && Directory.Exists(directory)
                ? directory
                : FileOperations.ParentOf(path),
        };

        foreach (var argument in command.Skip(1))
            start.ArgumentList.Add(argument);

        try
        {
            Process.Start(start)?.Dispose();
            return null;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            Trace.WriteLine($"Can't start '{command[0]}' for '{path}': {ex.Message}");
            return $"{app.Name} couldn't be started: {ex.Message}";
        }
    }

    private static string? OpenWithXdgOpen(string path)
    {
        try
        {
            var start = new ProcessStartInfo("xdg-open") { UseShellExecute = false };
            start.ArgumentList.Add(path);
            Process.Start(start)?.Dispose();
            return null;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            Trace.WriteLine($"Can't start xdg-open for '{path}': {ex.Message}");
            return "No app could be found to open this file.";
        }
    }
}

/// <summary>
/// The installed applications: every .desktop file under the applications folders of the data directories,
/// by desktop file ID ("kde4/okular.desktop" is "kde4-okular.desktop"), and the types each declares.
/// </summary>
public sealed class Applications
{
    private readonly Dictionary<string, string> _paths;
    private readonly Dictionary<string, List<string>> _byType;
    private readonly Dictionary<string, DesktopApp?> _apps = new(StringComparer.Ordinal);

    private Applications(Dictionary<string, string> paths, Dictionary<string, List<string>> byType)
    {
        _paths = paths;
        _byType = byType;
    }

    /// <summary>Read on every open, so apps installed meanwhile are found. The folders are small.</summary>
    public static Applications Load() => Load(XdgDirectories.DataDirectories());

    public static Applications Load(IEnumerable<string> dataDirectories)
    {
        var paths = new Dictionary<string, string>(StringComparer.Ordinal);
        var byType = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var directory in dataDirectories)
        {
            var root = IOPath.Combine(directory, "applications");
            if (!Directory.Exists(root))
                continue;

            // The first data directory that has an ID wins: the user's own copy hides the system one
            foreach (var file in DesktopFiles(root))
            {
                var id = IOPath.GetRelativePath(root, file).Replace('/', '-');
                paths.TryAdd(id, file);
            }

            // In precedence order, so the user's apps come before the system's
            foreach (var (type, ids) in IniFile.Read(IOPath.Combine(root, "mimeinfo.cache")).Group("MIME Cache"))
            {
                if (!byType.TryGetValue(type, out var list))
                    byType[type] = list = [];

                foreach (var id in IniFile.SplitList(ids))
                {
                    if (!list.Contains(id))
                        list.Add(id);
                }
            }
        }

        // Apps whose MimeType= isn't in a cache yet (update-desktop-database not run): read them too
        var cached = byType.Values.SelectMany(ids => ids).ToHashSet(StringComparer.Ordinal);
        foreach (var (id, path) in paths)
        {
            if (cached.Contains(id))
                continue;

            if (!IniFile.Read(path).Group("Desktop Entry").TryGetValue("MimeType", out var types))
                continue;

            foreach (var type in IniFile.SplitList(types))
            {
                if (!byType.TryGetValue(type, out var list))
                    byType[type] = list = [];
                list.Add(id);
            }
        }

        return new Applications(paths, byType);
    }

    /// <summary>The app with this desktop file ID, if it is installed and can be launched.</summary>
    public DesktopApp? Find(string id)
    {
        if (_apps.TryGetValue(id, out var known))
            return known;

        var app = _paths.TryGetValue(id, out var path) ? Read(id, path) : null;
        _apps[id] = app;
        return app;
    }

    /// <summary>Every installed app that can be launched, NoDisplay ones included.</summary>
    public IEnumerable<DesktopApp> All() => _paths.Keys.Select(Find).OfType<DesktopApp>();

    /// <summary>The IDs of the apps whose MimeType= lists <paramref name="type"/>.</summary>
    public IReadOnlyList<string> Declaring(string type)
        => _byType.TryGetValue(type, out var ids) ? ids : [];

    private static DesktopApp? Read(string id, string path)
    {
        var entry = IniFile.Read(path).Group("Desktop Entry");
        if (entry.GetValueOrDefault("Type", "Application") != "Application"
            || entry.GetValueOrDefault("Hidden") == "true"
            || !entry.TryGetValue("Exec", out var exec)
            || string.IsNullOrWhiteSpace(exec))
            return null;

        // TryExec: the program the entry needs; when it is missing, the app isn't really installed
        if (entry.TryGetValue("TryExec", out var tryExec) && !string.IsNullOrWhiteSpace(tryExec)
                                                          && Terminals.FindProgram(tryExec) is null)
            return null;

        return new DesktopApp(
            id,
            path,
            entry.GetValueOrDefault("Name") ?? IOPath.GetFileNameWithoutExtension(id),
            exec,
            entry.GetValueOrDefault("Icon"),
            entry.GetValueOrDefault("Path"),
            entry.GetValueOrDefault("Terminal") == "true",
            IniFile.SplitList(entry.GetValueOrDefault("Categories") ?? string.Empty),
            entry.GetValueOrDefault("NoDisplay") == "true");
    }

    private static IEnumerable<string> DesktopFiles(string root)
    {
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
        try
        {
            return Directory.EnumerateFiles(root, "*.desktop", options).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.WriteLine($"Can't list '{root}': {ex.Message}");
            return [];
        }
    }
}

/// <summary>
/// The mimeapps.list files, highest precedence first: in each config and data directory (applications/ for data),
/// $desktop-mimeapps.list for every current desktop, then mimeapps.list; the legacy defaults.list last.
/// </summary>
public sealed class MimeAppsLists
{
    private readonly List<Dictionary<string, Dictionary<string, string>>> _files;

    private MimeAppsLists(List<Dictionary<string, Dictionary<string, string>>> files) => _files = files;

    public static MimeAppsLists Load()
    {
        var desktops = XdgDirectories.CurrentDesktops();
        var folders = XdgDirectories.ConfigDirectories()
            .Concat(XdgDirectories.DataDirectories().Select(directory => IOPath.Combine(directory, "applications")));

        var files = new List<Dictionary<string, Dictionary<string, string>>>();
        foreach (var folder in folders)
        {
            foreach (var name in desktops.Select(desktop => $"{desktop}-mimeapps.list").Append("mimeapps.list"))
                files.Add(IniFile.Read(IOPath.Combine(folder, name)).Groups);
        }

        // defaults.list: older systems' defaults, below every mimeapps.list
        foreach (var directory in XdgDirectories.DataDirectories())
            files.Add(IniFile.Read(IOPath.Combine(directory, "applications", "defaults.list")).Groups);

        return new MimeAppsLists(files);
    }

    /// <summary>The defaults set for <paramref name="type"/>, best first. Each is tried until one is installed.</summary>
    public IEnumerable<string> Defaults(string type) => Values("Default Applications", type);

    /// <summary>Apps the user added for <paramref name="type"/>: picked in Open With…, the last one first.</summary>
    public IEnumerable<string> Added(string type) => Values("Added Associations", type);

    /// <summary>Apps the user removed from <paramref name="type"/>: never picked for it.</summary>
    public HashSet<string> Removed(string type) => Values("Removed Associations", type).ToHashSet(StringComparer.Ordinal);

    private IEnumerable<string> Values(string group, string type)
    {
        foreach (var file in _files)
        {
            if (file.TryGetValue(group, out var entries) && entries.TryGetValue(type, out var value))
            {
                foreach (var id in IniFile.SplitList(value))
                    yield return id;
            }
        }
    }
}

/// <summary>Expands a desktop entry's Exec= line into a command and its arguments (Desktop Entry spec).</summary>
public static class DesktopExec
{
    public static List<string> Expand(DesktopApp app, string path)
    {
        var result = new List<string>();
        var hasFileCode = false;

        foreach (var (argument, quoted) in Tokenize(app.Exec))
        {
            // Flatpak exports wrap the file arguments in @@u … @@ (or @@f): passed on as they are,
            // flatpak run --file-forwarding needs them to give the sandboxed app access to the file
            if (!quoted)
            {
                switch (argument)
                {
                    case "%f" or "%F":
                        result.Add(path);
                        hasFileCode = true;
                        continue;
                    case "%u" or "%U":
                        result.Add(ToUri(path));
                        hasFileCode = true;
                        continue;
                    case "%i":
                        if (!string.IsNullOrEmpty(app.Icon))
                        {
                            result.Add("--icon");
                            result.Add(app.Icon);
                        }

                        continue;
                }
            }

            var expanded = ExpandCodes(argument, app, path, ref hasFileCode);
            if (expanded.Length > 0 || quoted)
                result.Add(expanded);
        }

        // An entry without a file code still takes the file, as the last argument
        if (!hasFileCode && result.Count > 0)
            result.Add(path);

        return result;
    }

    /// <summary>Codes inside a word: %c (name), %k (desktop file), %% (a percent sign), file codes; the rest is dropped.</summary>
    private static string ExpandCodes(string argument, DesktopApp app, string path, ref bool hasFileCode)
    {
        if (!argument.Contains('%'))
            return argument;

        var text = new StringBuilder();
        for (var i = 0; i < argument.Length; i++)
        {
            if (argument[i] != '%' || i + 1 >= argument.Length)
            {
                text.Append(argument[i]);
                continue;
            }

            var code = argument[++i];
            switch (code)
            {
                case '%':
                    text.Append('%');
                    break;
                case 'c':
                    text.Append(app.Name);
                    break;
                case 'k':
                    text.Append(app.Path);
                    break;
                case 'f' or 'F':
                    text.Append(path);
                    hasFileCode = true;
                    break;
                case 'u' or 'U':
                    text.Append(ToUri(path));
                    hasFileCode = true;
                    break;
                // %d %D %n %N %v %m are deprecated, %i only stands alone: dropped
            }
        }

        return text.ToString();
    }

    /// <summary>
    /// Splits at unquoted spaces. Inside double quotes, a backslash before ", `, $ or itself stands for that character.
    /// The value's own escapes (backslash-s, backslash-n…) were already undone when the file was read.
    /// </summary>
    private static IEnumerable<(string Argument, bool Quoted)> Tokenize(string exec)
    {
        var current = new StringBuilder();
        var inQuotes = false;
        var quoted = false;
        var started = false;

        for (var i = 0; i < exec.Length; i++)
        {
            var c = exec[i];
            if (inQuotes)
            {
                if (c == '\\' && i + 1 < exec.Length && exec[i + 1] is '"' or '`' or '$' or '\\')
                    current.Append(exec[++i]);
                else if (c == '"')
                    inQuotes = false;
                else
                    current.Append(c);
                continue;
            }

            if (c == '"')
            {
                inQuotes = quoted = started = true;
            }
            else if (c is ' ' or '\t')
            {
                if (started)
                    yield return (current.ToString(), quoted);

                current.Clear();
                quoted = started = false;
            }
            else
            {
                current.Append(c);
                started = true;
            }
        }

        if (started)
            yield return (current.ToString(), quoted);
    }

    private static string ToUri(string path)
        => "file://" + string.Join('/', path.Split('/').Select(Uri.EscapeDataString));
}

/// <summary>Terminal apps, for desktop entries with Terminal=true (vim, nano, htop…).</summary>
public static class Terminals
{
    // How each one takes the command to run
    private static readonly (string Program, string[] Before)[] Known =
    [
        ("xdg-terminal-exec", []),
        ("kitty", []),
        ("foot", []),
        ("ghostty", ["-e"]),
        ("alacritty", ["-e"]),
        ("wezterm", ["start", "--"]),
        ("konsole", ["-e"]),
        ("gnome-terminal", ["--"]),
        ("ptyxis", ["--"]),
        ("xfce4-terminal", ["-x"]),
        ("tilix", ["-e"]),
        ("xterm", ["-e"]),
    ];

    /// <summary>The command run inside a terminal: $TERMINAL first, then the first known one installed.</summary>
    public static List<string>? Wrap(IReadOnlyList<string> command)
    {
        var preferred = Environment.GetEnvironmentVariable("TERMINAL");
        if (!string.IsNullOrWhiteSpace(preferred) && FindProgram(preferred) is not null)
        {
            var name = IOPath.GetFileName(preferred);
            var before = Known.FirstOrDefault(known => known.Program == name).Before ?? ["-e"];
            return [preferred, .. before, .. command];
        }

        foreach (var (program, before) in Known)
        {
            if (FindProgram(program) is { } found)
                return [found, .. before, .. command];
        }

        return null;
    }

    /// <summary>An absolute path that exists, or a name found on $PATH.</summary>
    public static string? FindProgram(string program)
    {
        if (IOPath.IsPathRooted(program))
            return IOFile.Exists(program) ? program : null;

        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(':',
                     StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = IOPath.Combine(directory, program);
            if (IOFile.Exists(candidate))
                return candidate;
        }

        return null;
    }
}

/// <summary>
/// The key file format of .desktop and mimeapps.list files: [Group] headers and Key=Value lines.
/// Localized keys (Name[de]) are skipped; values have their string escapes (backslash-s, -n, -t, -r and a doubled backslash) undone.
/// </summary>
public sealed class IniFile
{
    private IniFile(Dictionary<string, Dictionary<string, string>> groups) => Groups = groups;

    public Dictionary<string, Dictionary<string, string>> Groups { get; }

    public static IniFile Read(string path)
    {
        var groups = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        string[] lines;
        try
        {
            lines = IOFile.Exists(path) ? IOFile.ReadAllLines(path) : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.WriteLine($"Can't read '{path}': {ex.Message}");
            lines = [];
        }

        Dictionary<string, string>? current = null;
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#')
                continue;

            if (line[0] == '[' && line[^1] == ']')
            {
                var name = line[1..^1];
                if (!groups.TryGetValue(name, out current))
                    groups[name] = current = new Dictionary<string, string>(StringComparer.Ordinal);
                continue;
            }

            var equals = line.IndexOf('=');
            if (current is null || equals <= 0)
                continue;

            var key = line[..equals].Trim();
            if (key.Contains('['))
                continue;

            // The first one wins, as in every reader of these files
            current.TryAdd(key, Unescape(line[(equals + 1)..].Trim()));
        }

        return new IniFile(groups);
    }

    /// <summary>One group's keys; empty when the file or the group is missing.</summary>
    public IReadOnlyDictionary<string, string> Group(string name)
        => Groups.TryGetValue(name, out var group) ? group : new Dictionary<string, string>();

    /// <summary>"a.desktop;b.desktop;" → [a.desktop, b.desktop].</summary>
    public static List<string> SplitList(string value)
        => value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private static string Unescape(string value)
    {
        if (!value.Contains('\\'))
            return value;

        var text = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] != '\\' || i + 1 >= value.Length)
            {
                text.Append(value[i]);
                continue;
            }

            var next = value[++i];
            text.Append(next switch
            {
                's' => " ",
                'n' => "\n",
                't' => "\t",
                'r' => "\r",
                '\\' => "\\",
                // \; in lists and Exec's own escapes (\" \$…) stay for the next reader
                _ => "\\" + next,
            });
        }

        return text.ToString();
    }
}
