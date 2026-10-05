using System.Text;

namespace File.Commander.Presentation.Services;

public enum UserDirectoryKind { Desktop, Videos, Music, Pictures, Documents, Downloads }
 
public enum VolumeKind { System, Fixed, Removable, Optical }
 
public sealed record UserDirectory(UserDirectoryKind Kind, string Name, string Location);
 
public sealed record Volume(VolumeKind Kind, string Name, string MountPoint, long TotalBytes, long FreeBytes)
{
    public long UsedBytes => Math.Max(0, TotalBytes - FreeBytes);
}

/// <summary>
/// Discovers the user's well-known folders (via XDG user-dirs) and the mounted block devices.
/// NOTE: inside the File.Commander.* namespace the identifier "File" resolves to the namespace,
/// so System.IO.File must always be fully qualified in this project.
/// </summary>
public static class SystemLocations
{
    public static string HomeDirectory { get; } =
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private static readonly (UserDirectoryKind Kind, string XdgKey, string Fallback)[] KnownDirectories =
    {
        (UserDirectoryKind.Desktop, "XDG_DESKTOP_DIR", "Desktop"),
        (UserDirectoryKind.Videos, "XDG_VIDEOS_DIR", "Videos"),
        (UserDirectoryKind.Music, "XDG_MUSIC_DIR", "Music"),
        (UserDirectoryKind.Pictures, "XDG_PICTURES_DIR", "Pictures"),
        (UserDirectoryKind.Documents, "XDG_DOCUMENTS_DIR", "Documents"),
        (UserDirectoryKind.Downloads, "XDG_DOWNLOAD_DIR", "Downloads"),
    };

    private static readonly string[] HiddenMountRoots = { "/boot", "/efi", "/snap", "/var/snap", "/var/lib/docker" };
    private static readonly string[] RemovableMountRoots = { "/media", "/run/media" };

    private static readonly HashSet<string> OpticalFormats =
        new(StringComparer.OrdinalIgnoreCase) { "iso9660", "udf" };

    public static IReadOnlyList<UserDirectory> GetUserDirectories()
    {
        var xdg = ReadXdgUserDirs();
        var result = new List<UserDirectory>();

        foreach (var (kind, key, fallback) in KnownDirectories)
        {
            var location = xdg.TryGetValue(key, out var configured)
                ? configured
                : Path.Combine(HomeDirectory, fallback);

            // xdg-user-dirs points a disabled directory at $HOME itself.
            if (PathEquals(location, HomeDirectory) || !Directory.Exists(location))
                continue;

            // Folder name on disk respects the user's locale (e.g. "Завантаження").
            result.Add(new UserDirectory(kind, Path.GetFileName(location.TrimEnd('/')), location));
        }

        return result;
    }

    public static IReadOnlyList<Volume> GetVolumes()
    {
        var volumes = new List<Volume>();
        var seenDevices = new HashSet<string>(StringComparer.Ordinal);

        // Shortest mount point first, so "/" wins over "/home" when both are btrfs subvolumes of one device.
        foreach (var (device, mountPoint, format) in ReadMounts().OrderBy(m => m.MountPoint.Length))
        {
            if (!device.StartsWith("/dev/", StringComparison.Ordinal)) continue; // proc, tmpfs, cgroup, ...
            if (device.StartsWith("/dev/loop", StringComparison.Ordinal)) continue; // snaps, images
            if (IsUnder(mountPoint, HiddenMountRoots)) continue;
            if (!seenDevices.Add(device)) continue; // subvolumes, bind mounts

            long total, free;
            try
            {
                var drive = new DriveInfo(mountPoint);
                if (!drive.IsReady) continue;
                total = drive.TotalSize;
                free = drive.TotalFreeSpace;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
            {
                continue;
            }

            if (total <= 0) continue;

            var kind = mountPoint == "/"
                ? VolumeKind.System
                : OpticalFormats.Contains(format) || device.StartsWith("/dev/sr", StringComparison.Ordinal)
                    ? VolumeKind.Optical
                    : IsUnder(mountPoint, RemovableMountRoots)
                        ? VolumeKind.Removable
                        : VolumeKind.Fixed;

            volumes.Add(new Volume(kind, GetVolumeName(mountPoint), mountPoint, total, free));
        }

        return volumes
            .OrderBy(v => v.Kind)
            .ThenBy(v => v.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>Every mount point, pseudo filesystems included. Read once per operation for <see cref="MountPointOf"/>.</summary>
    public static IReadOnlyList<string> GetMountPoints() => ReadMounts().Select(m => m.MountPoint).ToList();

    /// <summary>
    /// The mount point of the filesystem <paramref name="path"/> is on: the longest of <paramref name="mountPoints"/>
    /// it is in. Two paths with the same one can be renamed into each other; otherwise they must be copied.
    /// </summary>
    public static string MountPointOf(string path, IReadOnlyList<string> mountPoints)
    {
        var best = "/";
        foreach (var mountPoint in mountPoints)
        {
            if (mountPoint.Length > best.Length && IsUnder(path, [mountPoint]))
                best = mountPoint;
        }

        return best;
    }

    private static string GetVolumeName(string mountPoint) => mountPoint switch
    {
        "/" => "System Disk",
        "/home" => "Data Disk",
        _ => Path.GetFileName(mountPoint) is { Length: > 0 } name ? name : mountPoint,
    };

    private static List<(string Device, string MountPoint, string Format)> ReadMounts()
    {
        var mounts = new List<(string, string, string)>();
        string[] lines;
        try
        {
            lines = System.IO.File.ReadAllLines("/proc/self/mounts");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return mounts;
        }

        foreach (var line in lines)
        {
            // <device> <mount point> <fs type> <options> <dump> <pass>
            var fields = line.Split(' ');
            if (fields.Length < 3) continue;
            mounts.Add((UnescapeMountField(fields[0]), UnescapeMountField(fields[1]), fields[2]));
        }

        return mounts;
    }

    /// <summary>The kernel escapes space, tab, newline and backslash as 3-digit octal (e.g. "UOS\04020").</summary>
    private static string UnescapeMountField(string value)
    {
        if (!value.Contains('\\')) return value;

        var sb = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '\\' && i + 3 < value.Length
                                 && IsOctal(value[i + 1]) && IsOctal(value[i + 2]) && IsOctal(value[i + 3]))
            {
                sb.Append((char)((value[i + 1] - '0') * 64 + (value[i + 2] - '0') * 8 + (value[i + 3] - '0')));
                i += 3;
            }
            else
            {
                sb.Append(value[i]);
            }
        }

        return sb.ToString();

        static bool IsOctal(char c) => c is >= '0' and <= '7';
    }

    private static Dictionary<string, string> ReadXdgUserDirs()
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        var configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (string.IsNullOrEmpty(configHome))
            configHome = Path.Combine(HomeDirectory, ".config");

        string[] lines;
        try
        {
            lines = System.IO.File.ReadAllLines(Path.Combine(configHome, "user-dirs.dirs"));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return result;
        }

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;

            var eq = line.IndexOf('=');
            if (eq <= 0) continue;

            var key = line[..eq];
            var value = line[(eq + 1)..].Trim().Trim('"');

            if (value.StartsWith("$HOME", StringComparison.Ordinal))
                value = HomeDirectory + value[5..];
            else if (!value.StartsWith('/'))
                continue;

            result[key] = value;
        }

        return result;
    }

    private static bool IsUnder(string path, IEnumerable<string> roots) =>
        roots.Any(root => path == root || path.StartsWith(root + "/", StringComparison.Ordinal));

    private static bool PathEquals(string a, string b) =>
        string.Equals(a.TrimEnd('/'), b.TrimEnd('/'), StringComparison.Ordinal);
}
