using System.Collections.Concurrent;
using System.Globalization;

namespace File.Commander.Presentation.Services;

/// <summary>
/// The picture of a .desktop file's Icon= key, found like the desktop does (freedesktop.org Icon Theme spec): an
/// absolute path as is; a name in the user's icon theme, the themes it inherits, then hicolor, then /usr/share/pixmaps.
/// Only PNG files are returned: they are what Avalonia decodes. An icon that only exists as SVG or XPM gives null,
/// and the entry keeps its default icon.
/// </summary>
public static class DesktopIcons
{
    private const string Fallback = "hicolor";

    // By name and size: a folder of .desktop files mostly shares a few sizes. Null results are kept too.
    private static readonly ConcurrentDictionary<(string Name, int Size), string?> Found = new();
    private static readonly ConcurrentDictionary<string, IconTheme?> Themes = new(StringComparer.Ordinal);
    private static readonly Lazy<IReadOnlyList<string>> BaseDirectories = new(FindBaseDirectories);
    private static readonly Lazy<IReadOnlyList<string>> ThemeChain = new(BuildThemeChain);

    /// <summary>True for "*.desktop": <see cref="IconOf"/> may find a picture for it.</summary>
    public static bool IsDesktopFile(string path) => path.EndsWith(".desktop", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The PNG to show for <paramref name="desktopFile"/> at about <paramref name="size"/> pixels, or null when it has
    /// no Icon= or the icon can't be found as a PNG. Reads files: call it off the UI thread. Never throws.
    /// </summary>
    public static string? IconOf(string desktopFile, int size)
    {
        try
        {
            var entry = IniFile.Read(desktopFile).Group("Desktop Entry");
            return entry.TryGetValue("Icon", out var icon) && !string.IsNullOrWhiteSpace(icon)
                ? Find(icon.Trim(), size)
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Trace.WriteLine($"Can't find the icon of '{desktopFile}': {ex.Message}");
            return null;
        }
    }

    /// <summary>A path or an icon name ("org.gnome.TextEditor", or "firefox.png" as older entries write it).</summary>
    public static string? Find(string icon, int size)
    {
        if (IOPath.IsPathRooted(icon))
            return IsPng(icon) && IOFile.Exists(icon) ? icon : null;

        // A name may not be a path; "firefox.png" is still found as "firefox"
        if (icon.Contains('/'))
            return null;

        var name = IOPath.GetExtension(icon).ToLowerInvariant() is ".png" or ".svg" or ".xpm"
            ? IOPath.GetFileNameWithoutExtension(icon)
            : icon;

        return Found.GetOrAdd((name, Math.Max(1, size)), key => Lookup(key.Name, key.Size));
    }

    private static string? Lookup(string name, int size)
    {
        foreach (var themeName in ThemeChain.Value)
        {
            if (Theme(themeName)?.Find(name, size) is { } path)
                return path;
        }

        foreach (var directory in BaseDirectories.Value.Append("/usr/share/pixmaps"))
        {
            var path = IOPath.Combine(directory, name + ".png");
            if (IOFile.Exists(path))
                return path;
        }

        return null;
    }

    private static bool IsPng(string path) => path.EndsWith(".png", StringComparison.OrdinalIgnoreCase);

    private static IconTheme? Theme(string name) => Themes.GetOrAdd(name, n => IconTheme.Load(n, BaseDirectories.Value));

    /// <summary>The user's theme, every theme it inherits (depth first, as the spec says), then hicolor.</summary>
    private static List<string> BuildThemeChain()
    {
        var chain = new List<string>();

        void Add(string name, int depth)
        {
            if (depth > 10 || name == Fallback || chain.Contains(name) || Theme(name) is not { } theme)
                return;

            chain.Add(name);
            foreach (var parent in theme.Inherits)
                Add(parent, depth + 1);
        }

        if (UserThemeName() is { } user)
            Add(user, 0);

        chain.Add(Fallback);
        return chain;
    }

    /// <summary>~/.icons, then $XDG_DATA_HOME/icons and $XDG_DATA_DIRS/*/icons.</summary>
    private static List<string> FindBaseDirectories()
        => new[] { IOPath.Combine(SystemLocations.HomeDirectory, ".icons") }
            .Concat(XdgDirectories.DataDirectories().Select(directory => IOPath.Combine(directory, "icons")))
            .Where(Directory.Exists)
            .Distinct(StringComparer.Ordinal)
            .ToList();

    /// <summary>The icon theme set in the desktop's settings: GTK's settings.ini, KDE's kdeglobals, then GNOME's gsettings.</summary>
    private static string? UserThemeName()
    {
        foreach (var config in XdgDirectories.ConfigDirectories())
        {
            foreach (var gtk in new[] { "gtk-4.0", "gtk-3.0" })
            {
                if (Value(IniFile.Read(IOPath.Combine(config, gtk, "settings.ini")).Group("Settings"), "gtk-icon-theme-name") is { } name)
                    return name;
            }

            if (Value(IniFile.Read(IOPath.Combine(config, "kdeglobals")).Group("Icons"), "Theme") is { } kde)
                return kde;
        }

        return GnomeThemeName();

        static string? Value(IReadOnlyDictionary<string, string> group, string key)
            => group.TryGetValue(key, out var value) && value.Trim().Trim('"') is { Length: > 0 } trimmed ? trimmed : null;
    }

    private static string? GnomeThemeName()
    {
        if (Terminals.FindProgram("gsettings") is not { } gsettings)
            return null;

        try
        {
            var start = new ProcessStartInfo(gsettings)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            start.ArgumentList.Add("get");
            start.ArgumentList.Add("org.gnome.desktop.interface");
            start.ArgumentList.Add("icon-theme");

            using var process = Process.Start(start);

            if (process is null)
                return null;

            var output = process.StandardOutput.ReadToEndAsync();
            if (!process.WaitForExit(1000) || !output.Wait(100))
            {
                try
                {
                    process.Kill();
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    // Already gone
                }

                return null;
            }

            // 'Adwaita'
            var name = output.Result.Trim().Trim('\'', '"');
            return process.ExitCode == 0 && name.Length > 0 ? name : null;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    /// <summary>One icon theme: its index.theme, found in every base directory that has the theme.</summary>
    private sealed class IconTheme
    {
        private readonly IReadOnlyList<string> _roots;
        private readonly IReadOnlyList<SizeDirectory> _directories;

        private IconTheme(IReadOnlyList<string> roots, IReadOnlyList<SizeDirectory> directories, IReadOnlyList<string> inherits)
        {
            _roots = roots;
            _directories = directories;
            Inherits = inherits;
        }

        public IReadOnlyList<string> Inherits { get; }

        public static IconTheme? Load(string name, IReadOnlyList<string> baseDirectories)
        {
            var roots = baseDirectories
                .Select(directory => IOPath.Combine(directory, name))
                .Where(Directory.Exists)
                .ToList();

            // The first index.theme describes the theme; the other roots add files to the same folders
            var index = roots.Select(root => IOPath.Combine(root, "index.theme")).FirstOrDefault(IOFile.Exists);
            if (index is null)
                return null;

            var ini = IniFile.Read(index);
            var theme = ini.Group("Icon Theme");
            var names = IniFile.SplitList(theme.GetValueOrDefault("Directories", "").Replace(',', ';'))
                .Concat(IniFile.SplitList(theme.GetValueOrDefault("ScaledDirectories", "").Replace(',', ';')))
                .Distinct(StringComparer.Ordinal);

            var directories = new List<SizeDirectory>();
            foreach (var directory in names)
            {
                var group = ini.Group(directory);
                if (Int(group, "Size") is not { } size)
                    continue;

                directories.Add(new SizeDirectory(
                    directory,
                    size,
                    Int(group, "Scale") ?? 1,
                    group.GetValueOrDefault("Type", "Threshold"),
                    Int(group, "MinSize") ?? size,
                    Int(group, "MaxSize") ?? size,
                    Int(group, "Threshold") ?? 2));
            }

            var inherits = IniFile.SplitList(theme.GetValueOrDefault("Inherits", "").Replace(',', ';'));
            return new IconTheme(roots, directories, inherits);
        }

        /// <summary>A PNG of the size, else the closest one (spec: LookupIcon). Scale 1 folders only.</summary>
        public string? Find(string name, int size)
        {
            string? closest = null;
            var closestDistance = int.MaxValue;

            foreach (var directory in _directories)
            {
                if (directory.Scale != 1)
                    continue;

                foreach (var root in _roots)
                {
                    var path = IOPath.Combine(root, directory.Name, name + ".png");
                    if (!IOFile.Exists(path))
                        continue;

                    if (directory.Matches(size))
                        return path;

                    var distance = directory.Distance(size);
                    if (distance < closestDistance)
                    {
                        closest = path;
                        closestDistance = distance;
                    }
                }
            }

            return closest;
        }

        private static int? Int(IReadOnlyDictionary<string, string> group, string key)
            => group.TryGetValue(key, out var text)
               && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                ? value
                : null;
    }

    private sealed record SizeDirectory(string Name, int Size, int Scale, string Type, int MinSize, int MaxSize, int Threshold)
    {
        public bool Matches(int size) => Type switch
        {
            "Fixed" => Size == size,
            "Scalable" => MinSize <= size && size <= MaxSize,
            _ => Size - Threshold <= size && size <= Size + Threshold,
        };

        public int Distance(int size) => Type switch
        {
            "Fixed" => Math.Abs(Size - size),
            "Scalable" => size < MinSize ? MinSize - size : size > MaxSize ? size - MaxSize : 0,
            _ => size < Size - Threshold ? Size - Threshold - size : size > Size + Threshold ? size - Size - Threshold : 0,
        };
    }
}
