using System.Globalization;
using System.Runtime.InteropServices;
using File.Commander.Application.Operations;

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

    /// <summary>Deletes every item of <paramref name="bins"/> for good. Never throws.</summary>
    /// <param name="progress">
    /// The Action center: told how many items there are and each one deleted. Canceling stops before the next
    /// item; what's left stays in the trash, restorable.
    /// </param>
    /// <returns>How many items couldn't be deleted; they keep their info file, so they can still be restored.</returns>
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

    private static bool TryDeleteTree(string path) => TryDeleteTree(path, out _);

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
