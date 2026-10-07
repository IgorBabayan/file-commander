using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace File.Commander.Presentation.Services;

/// <summary>What Open with… offers for one file.</summary>
/// <param name="Type">The file's MIME type, e.g. "application/zip".</param>
/// <param name="Description">The type as people call it, e.g. "Zip archive"; null when shared-mime-info has no name for it.</param>
/// <param name="Default">The app a double click opens it in, when there is one.</param>
/// <param name="Recommended">Other apps made for the type (or a type it is a kind of), best first.</param>
/// <param name="Others">Every other installed app, by name.</param>
public sealed record OpenWithChoices(
    string Type,
    string? Description,
    DesktopApp? Default,
    IReadOnlyList<DesktopApp> Recommended,
    IReadOnlyList<DesktopApp> Others);

/// <summary>
/// The apps of the Open with… dialog, as GNOME Files lists them: the current default, the recommended apps, then every
/// other app. And what picking one changes, written to the user's mimeapps.list the way GIO writes it, so every
/// file manager and xdg-open follow it: the app picked becomes the type's most recently used one (listed first next
/// time); "Always use for this file type" makes it the default.
/// </summary>
public static class OpenWithApps
{
    private const string DefaultGroup = "Default Applications";
    private const string AddedGroup = "Added Associations";
    private const string RemovedGroup = "Removed Associations";

    /// <summary>The choices for <paramref name="path"/>. Reads the desktop files: call it off the UI thread.</summary>
    public static OpenWithChoices For(string path)
    {
        var mime = MimeDatabase.Default;
        var type = mime.TypeOf(path);
        var applications = Applications.Load();
        var lists = MimeAppsLists.Load();

        // What a double click opens: the same lookup, so the two always agree
        var current = FileLauncher.FindApp(mime, type, applications);

        var recommended = new List<DesktopApp>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (current is not null)
            seen.Add(current.Id);

        foreach (var kind in mime.Hierarchy(type))
        {
            var removed = lists.Removed(kind);
            var ids = lists.Defaults(kind).Concat(lists.Added(kind)).Concat(applications.Declaring(kind));
            foreach (var id in ids)
            {
                if (removed.Contains(id) || !seen.Add(id))
                    continue;

                if (applications.Find(id) is { NoDisplay: false } app)
                    recommended.Add(app);
            }
        }

        var others = applications.All()
            .Where(app => !app.NoDisplay && seen.Add(app.Id))
            .OrderBy(app => app.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(app => app.Id, StringComparer.Ordinal)
            .ToList();

        return new OpenWithChoices(type, Describe(type), current, recommended, others);
    }

    /// <summary>
    /// <paramref name="app"/> was picked for files of <paramref name="type"/>: it goes first in the type's added
    /// associations (and out of its removed ones). With <paramref name="makeDefault"/>, it also becomes the type's
    /// default. Returns what went wrong, or null.
    /// </summary>
    public static string? Remember(string type, DesktopApp app, bool makeDefault)
        => Remember(UserMimeAppsList(), type, app.Id, makeDefault);

    /// <inheritdoc cref="Remember(string, DesktopApp, bool)"/>
    /// <param name="file">The mimeapps.list to change; created when missing.</param>
    public static string? Remember(string file, string type, string appId, bool makeDefault)
    {
        try
        {
            var lines = IOFile.Exists(file) ? IOFile.ReadAllLines(file).ToList() : [];

            if (makeDefault)
                SetValue(lines, DefaultGroup, type, appId);

            var added = IniFile.SplitList(GetValue(lines, AddedGroup, type) ?? string.Empty);
            added.Remove(appId);
            added.Insert(0, appId);
            SetValue(lines, AddedGroup, type, string.Join(';', added) + ";");

            if (GetValue(lines, RemovedGroup, type) is { } removedValue)
            {
                var removed = IniFile.SplitList(removedValue);
                if (removed.Remove(appId))
                    SetValue(lines, RemovedGroup, type, removed.Count > 0 ? string.Join(';', removed) + ";" : null);
            }

            Write(file, lines);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.WriteLine($"Can't write '{file}': {ex.Message}");
            return $"The choice couldn't be saved in {file}: {ex.Message}";
        }
    }

    /// <summary>$XDG_CONFIG_HOME/mimeapps.list: where GIO, KDE and xdg-mime write the user's choices.</summary>
    private static string UserMimeAppsList() => IOPath.Combine(XdgDirectories.ConfigDirectories()[0], "mimeapps.list");

    /// <summary>
    /// The type's name from shared-mime-info (mime/application/zip.xml: "Zip archive"), in English, as the rest of the
    /// app is. Null when no data directory describes it.
    /// </summary>
    public static string? Describe(string type) => Describe(type, XdgDirectories.DataDirectories());

    public static string? Describe(string type, IEnumerable<string> dataDirectories)
    {
        if (type.Contains("..", StringComparison.Ordinal) || type.Count(c => c == '/') != 1)
            return null;

        foreach (var directory in dataDirectories)
        {
            var file = IOPath.Combine(directory, "mime", type + ".xml");
            if (!IOFile.Exists(file))
                continue;

            try
            {
                var comment = XDocument.Load(file).Root?
                    .Elements()
                    .FirstOrDefault(element => element.Name.LocalName == "comment"
                                               && element.Attribute(XNamespace.Xml + "lang") is null);
                if (comment is not null && !string.IsNullOrWhiteSpace(comment.Value))
                    return comment.Value.Trim();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or XmlException)
            {
                Trace.WriteLine($"Can't read '{file}': {ex.Message}");
            }
        }

        return null;
    }

    // ===== mimeapps.list, edited in place: comments, other groups and the order of keys are kept =====

    private static string? GetValue(List<string> lines, string group, string key)
    {
        var (start, end) = GroupRange(lines, group);
        if (start < 0)
            return null;

        for (var i = start + 1; i < end; i++)
        {
            if (KeyOf(lines[i]) == key)
                return lines[i][(lines[i].IndexOf('=') + 1)..].Trim();
        }

        return null;
    }

    /// <summary>Sets, adds or (with a null <paramref name="value"/>) removes one key of <paramref name="group"/>.</summary>
    private static void SetValue(List<string> lines, string group, string key, string? value)
    {
        var (start, end) = GroupRange(lines, group);
        if (start < 0)
        {
            if (value is null)
                return;

            // A new group at the end, after a blank line
            if (lines.Count > 0 && lines[^1].Trim().Length > 0)
                lines.Add(string.Empty);

            lines.Add($"[{group}]");
            lines.Add($"{key}={value}");
            return;
        }

        for (var i = start + 1; i < end; i++)
        {
            if (KeyOf(lines[i]) != key)
                continue;

            if (value is null)
                lines.RemoveAt(i);
            else
                lines[i] = $"{key}={value}";

            return;
        }

        if (value is null)
            return;

        // After the group's last entry, before the blank lines that separate it from the next group
        var at = end;
        while (at > start + 1 && lines[at - 1].Trim().Length == 0)
            at--;

        lines.Insert(at, $"{key}={value}");
    }

    /// <summary>The line of [<paramref name="group"/>] and the line where the next group starts; (-1, -1) when missing.</summary>
    private static (int Start, int End) GroupRange(List<string> lines, string group)
    {
        var header = $"[{group}]";
        var start = lines.FindIndex(line => line.Trim() == header);
        if (start < 0)
            return (-1, -1);

        var end = start + 1;
        while (end < lines.Count && !lines[end].TrimStart().StartsWith('['))
            end++;

        return (start, end);
    }

    private static string? KeyOf(string line)
    {
        var trimmed = line.TrimStart();
        if (trimmed.Length == 0 || trimmed[0] == '#')
            return null;

        var equals = trimmed.IndexOf('=');
        return equals > 0 ? trimmed[..equals].Trim() : null;
    }

    /// <summary>
    /// Written next to it and renamed over it: a crash never leaves half a file. A symlinked file (dotfiles kept in a
    /// repository) is written where it points, so the link stays.
    /// </summary>
    private static void Write(string file, List<string> lines)
    {
        if (new FileInfo(file).ResolveLinkTarget(returnFinalTarget: true) is { } target)
            file = target.FullName;

        if (IOPath.GetDirectoryName(file) is { } folder)
            Directory.CreateDirectory(folder);

        var temporary = file + ".file-commander.tmp";
        IOFile.WriteAllText(temporary, string.Join('\n', lines) + "\n", new UTF8Encoding(false));
        IOFile.Move(temporary, file, overwrite: true);
    }
}
