using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace File.Commander.Presentation.Services;

/// <summary>
/// The freedesktop.org trash: the home trash ($XDG_DATA_HOME/Trash) and, on other drives,
/// $topdir/.Trash/$uid and $topdir/.Trash-$uid. Each holds files/ (the items) and info/ (where they came from).
/// </summary>
public static class TrashBins
{
    /// <summary>The trash of the home drive, whether or not it exists yet.</summary>
    public static string HomeTrash { get; } = IOPath.Combine(DataHome(), "Trash");

    /// <summary>The trash folders that exist, the home trash first.</summary>
    /// <param name="allDrives">Also the trash folders at the top of every mounted drive.</param>
    public static IReadOnlyList<string> Find(bool allDrives)
    {
        var bins = new List<string>();
        AddIfExists(bins, HomeTrash);

        if (allDrives && UserId() is { } uid)
        {
            foreach (var volume in SystemLocations.GetVolumes())
            {
                AddIfExists(bins, IOPath.Combine(volume.MountPoint, ".Trash", uid));
                AddIfExists(bins, IOPath.Combine(volume.MountPoint, $".Trash-{uid}"));
            }
        }

        return bins;
    }

    /// <summary>Something is in the files/ folder of one of <paramref name="bins"/>.</summary>
    public static bool HasItems(IReadOnlyList<string> bins) => bins.Any(bin =>
    {
        try
        {
            return Directory.EnumerateFileSystemEntries(IOPath.Combine(bin, "files")).Any();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    });

    public static int Empty(IReadOnlyList<string> bins, IOperationProgress? progress = null)
    {
        // Listed up front, so the Action center knows how much there is to do
        var contents = bins.Select(bin => (Bin: bin, Items: Entries(IOPath.Combine(bin, "files")))).ToList();
        progress?.SetTotal(contents.Sum(content => (long)content.Items.Length));

        var failed = 0;
        foreach (var (bin, items) in contents)
        {
            if (progress?.CancellationToken.IsCancellationRequested == true)
                break;

            failed += EmptyBin(bin, items, progress);
        }

        return failed;
    }

    /// <summary>
    /// Moves <paramref name="paths"/> to the trash, restorable by any file manager. Items of the home drive go to the
    /// home trash; items of another drive to the trash at its top ($topdir/.Trash/$uid, else $topdir/.Trash-$uid),
    /// so nothing is copied across drives. Never throws for one item: it is reported to <paramref name="progress"/>.
    /// Items the user isn't allowed to move (in a folder owned by root) are moved with administrator rights at the end,
    /// and then belong to the user, like everything else in their trash.
    /// </summary>
    public static void MoveToTrash(IReadOnlyList<string> paths, IOperationProgress progress)
    {
        progress.SetTotal(paths.Count);

        var mounts = SystemLocations.GetMountPoints();
        var homeMount = SystemLocations.MountPointOf(DataHome(), mounts);
        var uid = UserId();
        var admin = AdminRights.IsAvailable ? new List<AdminStep>() : null;

        foreach (var path in paths)
        {
            if (progress.CancellationToken.IsCancellationRequested)
                break;

            var name = IOPath.GetFileName(path);
            progress.Begin(name);

            // Handed over to be moved as root: counted as done once that ran
            var handedOver = false;
            try
            {
                var mount = SystemLocations.MountPointOf(FileOperations.ParentOf(path), mounts);
                if (mount == homeMount)
                {
                    EnsureBin(HomeTrash);
                    handedOver = MoveIntoBin(path, HomeTrash, topDir: null, admin);
                }
                else if (DriveBin(mount, uid) is { } bin)
                {
                    handedOver = MoveIntoBin(path, bin, mount, admin);
                }
                else
                {
                    progress.Fail(name, "This drive has no trash you can use");
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                progress.Fail(name, ex.Message);
            }

            if (!handedOver)
                progress.Advance();
        }

        if (admin is not null)
            AdminRights.RunInto(admin, progress);
    }

    /// <summary>The trash at the top of another drive: the shared .Trash when it is safe to use, else our own.</summary>
    private static string? DriveBin(string topDir, string? uid)
    {
        if (uid is null)
            return null;

        // The spec only trusts a shared .Trash that is a real folder with the sticky bit set
        var shared = IOPath.Combine(topDir, ".Trash");
        try
        {
            var info = new DirectoryInfo(shared);
            if (info.Exists && info.LinkTarget is null && (info.UnixFileMode & UnixFileMode.StickyBit) != 0)
            {
                var bin = IOPath.Combine(shared, uid);
                EnsureBin(bin);
                return bin;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not usable: try our own below
        }

        var own = IOPath.Combine(topDir, $".Trash-{uid}");
        try
        {
            EnsureBin(own);
            return own;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.WriteLine($"Can't create the trash '{own}': {ex.Message}");
            return null;
        }
    }

    /// <summary>Creates the bin with its files/ and info/ folders, readable by its owner only.</summary>
    private static void EnsureBin(string bin)
    {
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(IOPath.Combine(bin, "files"));
            Directory.CreateDirectory(IOPath.Combine(bin, "info"));
            return;
        }

        const UnixFileMode ownerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
        Directory.CreateDirectory(bin, ownerOnly);
        Directory.CreateDirectory(IOPath.Combine(bin, "files"), ownerOnly);
        Directory.CreateDirectory(IOPath.Combine(bin, "info"), ownerOnly);
    }

    /// <returns>True when the user isn't allowed to move it, and it was added to <paramref name="admin"/>.</returns>
    private static bool MoveIntoBin(string path, string bin, string? topDir, List<AdminStep>? admin)
    {
        var files = IOPath.Combine(bin, "files");
        var info = IOPath.Combine(bin, "info");
        var name = IOPath.GetFileName(path);
        var stored = topDir is null ? path : IOPath.GetRelativePath(topDir, path);
        var content = Encoding.UTF8.GetBytes(
            "[Trash Info]\n"
            + $"Path={EncodeTrashPath(stored)}\n"
            + $"DeletionDate={DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture)}\n");

        for (var number = 1; number < 10_000; number++)
        {
            var candidate = number == 1 ? name : FileOperations.NumberedName(name, number);

            // Left there without an info file by another program: not ours to replace
            if (FileOperations.Exists(IOPath.Combine(files, candidate)))
                continue;

            var infoFile = IOPath.Combine(info, candidate + ".trashinfo");
            try
            {
                using var stream = new FileStream(infoFile, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                stream.Write(content);
            }
            catch (IOException) when (IOFile.Exists(infoFile))
            {
                continue;
            }

            var trashed = IOPath.Combine(files, candidate);
            try
            {
                FileOperations.MoveEntry(path, trashed);
                return false;
            }
            catch (Exception ex) when (AdminRights.IsDenied(ex) && admin is not null)
            {
                // The info file stays: it is deleted as root if the move fails there too
                admin.Add(new AdminStep(name, AdminRights.MoveToTrash(path, trashed, infoFile)));
                return true;
            }
            catch
            {
                TryDeleteTree(infoFile);
                throw;
            }
        }

        throw new IOException("The trash has no free name for it");
    }

    /// <summary>Path= is a URL-escaped path: every segment escaped, the slashes kept.</summary>
    private static string EncodeTrashPath(string path)
        => string.Join('/', path.Split('/').Select(Uri.EscapeDataString));

    private static int EmptyBin(string bin, string[] items, IOperationProgress? progress)
    {
        var files = IOPath.Combine(bin, "files");
        var info = IOPath.Combine(bin, "info");
        var failed = 0;

        foreach (var item in items)
        {
            if (progress?.CancellationToken.IsCancellationRequested == true)
                return failed;

            var name = IOPath.GetFileName(item);
            progress?.Begin(name);

            if (TryDeleteTree(item, out var error))
            {
                TryDeleteTree(IOPath.Combine(info, name + ".trashinfo"));
            }
            else
            {
                failed++;
                progress?.Fail(name, error ?? "Couldn't be deleted");
            }

            progress?.Advance();
        }

        // Info files whose item was already gone
        foreach (var entry in Entries(info))
        {
            if (!entry.EndsWith(".trashinfo", StringComparison.Ordinal))
                continue;

            var item = IOPath.Combine(files, IOPath.GetFileNameWithoutExtension(entry));
            if (!IOFile.Exists(item) && !Directory.Exists(item))
                TryDeleteTree(entry);
        }

        // Left by other file managers: half-deleted items and the cached folder sizes
        foreach (var entry in Entries(IOPath.Combine(bin, "expunged")))
            TryDeleteTree(entry);

        TryDeleteTree(IOPath.Combine(bin, "directorysizes"));
        return failed;
    }

    private static string[] Entries(string folder)
    {
        try
        {
            return Directory.Exists(folder) ? Directory.GetFileSystemEntries(folder) : Array.Empty<string>();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.WriteLine($"Can't list trash folder '{folder}': {ex.Message}");
            return [];
        }
    }

    private static void TryDeleteTree(string path) => TryDeleteTree(path, out _);

    private static bool TryDeleteTree(string path, out string? error)
    {
        try
        {
            DeleteTree(path);
            error = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.WriteLine($"Can't delete '{path}' from the trash: {ex.Message}");
            error = ex.Message;
            return false;
        }
    }

    /// <summary>Symlinks are deleted, never followed. Missing paths are fine.</summary>
    private static void DeleteTree(string path)
    {
        var entry = new FileInfo(path);
        if (!entry.Exists && !Directory.Exists(path) && entry.LinkTarget is null)
            return;

        if ((entry.Attributes & FileAttributes.Directory) == 0 || entry.LinkTarget is not null)
        {
            IOFile.Delete(path);
            return;
        }

        // A read-only folder keeps its entries: give the owner full access to it first
        if (!OperatingSystem.IsWindows())
        {
            try
            {
                IOFile.SetUnixFileMode(path, IOFile.GetUnixFileMode(path)
                                             | UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Not ours: deleting its entries below reports the real problem
            }
        }

        foreach (var child in Directory.GetFileSystemEntries(path))
            DeleteTree(child);

        Directory.Delete(path);
    }

    private static void AddIfExists(List<string> bins, string bin)
    {
        if (Directory.Exists(bin) && !bins.Contains(bin, StringComparer.Ordinal))
            bins.Add(bin);
    }

    private static string DataHome()
    {
        var configured = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        return !string.IsNullOrEmpty(configured) && IOPath.IsPathRooted(configured)
            ? configured
            : IOPath.Combine(SystemLocations.HomeDirectory, ".local", "share");
    }

    private static string? UserId()
    {
        if (OperatingSystem.IsWindows())
            return null;

        try
        {
            return getuid().ToString(CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

#pragma warning disable SYSLIB1054 // A plain uint return needs no marshalling
    [DllImport("libc")]
    private static extern uint getuid();
#pragma warning restore SYSLIB1054
}
