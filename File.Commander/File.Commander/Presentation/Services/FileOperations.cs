using System.ComponentModel;
using System.IO.Compression;
using System.IO.Enumeration;
using System.Text;
using File.Commander.Application.Operations;

namespace File.Commander.Presentation.Services;

/// <summary>
/// What the context menu of files and folders does on disk: copy, move, rename, compress, email.
/// The long ones run in the Action center and report to its <see cref="IOperationProgress"/>: one item that
/// fails is listed there and the rest goes on. Symlinks are copied as links, never followed into.
/// What happens to a name that is taken is decided per item (<see cref="NameConflict"/>): by default it gets a
/// number, "report (2).pdf"; a replaced item is only removed once its replacement fully arrived.
/// </summary>
public static class FileOperations
{
    private const int BufferSize = 1 << 20;

    private const UnixFileMode RegularFileType = (UnixFileMode)0x8000;
    private const UnixFileMode DirectoryType = (UnixFileMode)0x4000;

    /// <summary>
    /// Copies or moves <paramref name="sources"/> into the folder <paramref name="target"/>, under their own names.
    /// A name that is taken gets a number. See <see cref="Transfer(IReadOnlyList{TransferItem}, string, bool, IOperationProgress)"/>.
    /// </summary>
    public static void Transfer(IReadOnlyList<string> sources, string target, bool move, IOperationProgress progress)
        => Transfer(sources.Select(source => new TransferItem(source)).ToList(), target, move, progress);

    /// <summary>
    /// Copies or moves <paramref name="items"/> into the folder <paramref name="target"/>, each under its
    /// <see cref="TransferItem.Name"/>, doing what its <see cref="TransferItem.OnConflict"/> says when that name is
    /// taken. Never throws for one item: it is reported to <paramref name="progress"/>. A move on the same drive is a
    /// rename; across drives, a copy that deletes the source once everything in it was copied.
    /// </summary>
    public static void Transfer(IReadOnlyList<TransferItem> items, string target, bool move, IOperationProgress progress)
    {
        var token = progress.CancellationToken;
        target = Locations.Normalize(target);

        // Measured up front, so the Action center shows a percentage, the speed and the time left
        var sizes = items.Select(item => Measure(item.Source, token)).ToList();
        progress.SetTotal(sizes.Sum(size => size.Items), sizes.Sum(size => size.Bytes));

        IReadOnlyList<string> mounts = move ? SystemLocations.GetMountPoints() : [];
        var targetMount = move ? SystemLocations.MountPointOf(target, mounts) : null;

        for (var i = 0; i < items.Count; i++)
        {
            if (token.IsCancellationRequested)
                return;

            var item = items[i];
            var source = Locations.Normalize(item.Source);
            var (count, bytes) = sizes[i];
            var ownName = IOPath.GetFileName(source);
            var name = string.IsNullOrEmpty(item.Name) ? ownName : item.Name;
            var parent = ParentOf(source);
            progress.Begin(name);

            if (!Exists(source))
            {
                Skip(progress, name, "It doesn't exist anymore", count, bytes);
                continue;
            }

            if (IsRealDirectory(source) && IsSameOrInside(target, source))
            {
                Skip(progress, name, "A folder can't be put inside itself", count, bytes);
                continue;
            }

            // Moved where it already is, under the same name: nothing to do
            if (move && parent == target && name == ownName)
            {
                progress.Advance(count, bytes);
                continue;
            }

            var destination = IOPath.Combine(target, name);
            var replace = false;
            if (Exists(destination))
            {
                switch (item.OnConflict)
                {
                    case NameConflict.Skip:
                        progress.Advance(count, bytes);
                        continue;

                    case NameConflict.Replace when destination == source:
                        // Pasted where it already is: replacing it with itself leaves it as it is
                        progress.Advance(count, bytes);
                        continue;

                    case NameConflict.Replace when IsSameOrInside(source, destination):
                        Skip(progress, name, "It can't replace the folder it is in", count, bytes);
                        continue;

                    case NameConflict.Replace:
                        replace = true;
                        break;

                    default:
                        destination = UniquePath(target, name);
                        break;
                }
            }

            var sameDrive = move && SystemLocations.MountPointOf(parent, mounts) == targetMount;
            try
            {
                if (replace)
                {
                    ReplaceEntry(source, destination, move, sameDrive, progress, token, count, bytes);
                    continue;
                }

                if (sameDrive && TryRename(source, destination))
                {
                    progress.Advance(count, bytes);
                    continue;
                }

                // Across drives: the source goes only when all of it arrived
                if (CopyEntry(source, destination, progress, token) && move)
                    DeleteTree(source);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                progress.Fail(name, ex.Message);
            }
        }
    }

    /// <summary>Renames <paramref name="path"/> in its folder. Returns what went wrong, or null.</summary>
    public static string? Rename(string path, string newName)
    {
        if (ValidateName(newName) is { } problem)
            return problem;

        var destination = IOPath.Combine(ParentOf(path), newName);
        if (Exists(destination))
            return $"An item named “{newName}” already exists in this folder.";

        try
        {
            MoveEntry(path, destination);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ex.Message;
        }
    }

    /// <summary>
    /// Packs <paramref name="sources"/> into a new zip file at <paramref name="archivePath"/>, each under its own name.
    /// Linked folders are stored empty, not followed. A canceled or failed archive is deleted.
    /// </summary>
    public static void Compress(IReadOnlyList<string> sources, string archivePath, IOperationProgress progress)
    {
        var token = progress.CancellationToken;
        var sizes = sources.Select(source => Measure(source, token)).ToList();
        progress.SetTotal(sizes.Sum(size => size.Items), sizes.Sum(size => size.Bytes));

        var created = false;
        var complete = false;
        try
        {
            using (var stream = new FileStream(archivePath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                created = true;
                using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
                foreach (var source in sources)
                {
                    if (token.IsCancellationRequested)
                        break;

                    var name = IOPath.GetFileName(Locations.Normalize(source));
                    progress.Begin(name);
                    AddToArchive(archive, source, name, progress, token);
                }
            }

            complete = !token.IsCancellationRequested;
        }
        finally
        {
            // A half-written archive can't be opened: don't leave it behind
            if (created && !complete)
                TryDelete(archivePath);
        }
    }

    /// <summary>Opens the default email app with <paramref name="files"/> attached. Returns what went wrong, or null.</summary>
    public static string? Email(IReadOnlyList<string> files)
    {
        var start = new ProcessStartInfo("xdg-email") { UseShellExecute = false };
        foreach (var file in files)
        {
            start.ArgumentList.Add("--attach");
            start.ArgumentList.Add(file);
        }

        try
        {
            Process.Start(start)?.Dispose();
            return null;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            Trace.WriteLine($"Can't start xdg-email: {ex.Message}");
            return "No email app could be opened. Sending files by email needs xdg-email (from xdg-utils) "
                   + "and a default email app.";
        }
    }

    /// <summary>Why <paramref name="name"/> can't name a file or folder, or null when it can.</summary>
    public static string? ValidateName(string name)
    {
        if (name.Length == 0)
            return "The name can't be empty.";

        if (name is "." or "..")
            return $"“{name}” can't be used as a name.";

        if (name.Contains('/') || name.Contains('\0'))
            return "A name can't contain “/”.";

        if (Encoding.UTF8.GetByteCount(name) > 255)
            return "The name is too long.";

        return null;
    }

    /// <summary>Something is at <paramref name="path"/>: a file, a folder, or a symlink, even a broken one.</summary>
    public static bool Exists(string path)
    {
        if (IOFile.Exists(path) || Directory.Exists(path))
            return true;

        try
        {
            return new FileInfo(path).LinkTarget is not null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>A folder, or a link to one.</summary>
    public static bool IsDirectory(string path) => Directory.Exists(path);

    /// <summary><paramref name="name"/> in <paramref name="folder"/>; "name (2).ext", "name (3).ext"… while that is taken.</summary>
    public static string UniquePath(string folder, string name)
    {
        var path = IOPath.Combine(folder, name);
        for (var number = 2; Exists(path); number++)
            path = IOPath.Combine(folder, NumberedName(name, number));

        return path;
    }

    /// <summary>"report.pdf", 2 → "report (2).pdf". ".bashrc" has no extension: ".bashrc (2)".</summary>
    public static string NumberedName(string name, int number)
    {
        var extension = IOPath.GetExtension(name);
        return extension.Length <= 1 || extension.Length == name.Length
            ? $"{name} ({number})"
            : $"{name[..^extension.Length]} ({number}){extension}";
    }

    /// <summary>The folder <paramref name="path"/> is in. "/" for "/" itself.</summary>
    public static string ParentOf(string path)
        => IOPath.GetDirectoryName(Locations.Normalize(path)) is { Length: > 0 } parent ? parent : "/";

    /// <summary><paramref name="path"/> is <paramref name="folder"/> or somewhere inside it.</summary>
    public static bool IsSameOrInside(string path, string folder)
    {
        path = Locations.Normalize(path);
        folder = Locations.Normalize(folder);
        return path == folder || path.StartsWith(folder == "/" ? "/" : folder + "/", StringComparison.Ordinal);
    }

    /// <summary>A rename: the entry itself, a symlink included, never what it points to.</summary>
    internal static void MoveEntry(string source, string destination)
    {
        // Directory.Exists follows links: a link to a folder is renamed like one (rename(2) moves the link)
        if (Directory.Exists(source))
            Directory.Move(source, destination);
        else
            IOFile.Move(source, destination);
    }

    /// <summary>
    /// Puts <paramref name="source"/> in place of what is at <paramref name="destination"/>. It arrives under a hidden
    /// temporary name next to it first; only once all of it is there is the old item deleted and the new one renamed
    /// into its place. So a copy that fails or is canceled leaves the old item as it was.
    /// </summary>
    private static void ReplaceEntry(string source, string destination, bool move, bool sameDrive,
        IOperationProgress progress, CancellationToken token, long items, long bytes)
    {
        var staging = UniquePath(ParentOf(destination), $".{IOPath.GetFileName(destination)}.replacing");

        // Same drive: the source itself is renamed in, so it can be put back if the swap fails
        var renamed = sameDrive && TryRename(source, staging);
        if (renamed)
        {
            progress.Advance(items, bytes);
        }
        else
        {
            bool complete;
            try
            {
                complete = CopyEntry(source, staging, progress, token);
            }
            catch
            {
                TryDeleteTree(staging);
                throw;
            }

            // Some of it couldn't be copied (already reported): the old item stays
            if (!complete)
            {
                TryDeleteTree(staging);
                return;
            }
        }

        try
        {
            DeleteTree(destination);
            MoveEntry(staging, destination);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (renamed)
                TryMoveBack(staging, source);
            else
                TryDeleteTree(staging);

            throw;
        }

        // Across drives: the source goes only now that it replaced the old item
        if (move && !renamed)
            DeleteTree(source);
    }

    private static void TryMoveBack(string staging, string source)
    {
        try
        {
            MoveEntry(staging, source);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.WriteLine($"Can't move '{staging}' back to '{source}': {ex.Message}");
        }
    }

    private static void TryDeleteTree(string path)
    {
        try
        {
            if (Exists(path))
                DeleteTree(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.WriteLine($"Can't delete '{path}': {ex.Message}");
        }
    }

    private static void Skip(IOperationProgress progress, string name, string reason, long items, long bytes)
    {
        progress.Fail(name, reason);
        progress.Advance(items, bytes);
    }

    /// <summary>Same drive: false when the rename didn't happen (e.g. a bind mount), so the caller copies instead.</summary>
    private static bool TryRename(string source, string destination)
    {
        try
        {
            MoveEntry(source, destination);
            return true;
        }
        catch (IOException ex)
        {
            Trace.WriteLine($"Can't rename '{source}' to '{destination}', copying instead: {ex.Message}");
            return false;
        }
    }

    private static bool IsRealDirectory(string path)
    {
        try
        {
            return Directory.Exists(path) && new DirectoryInfo(path).LinkTarget is null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>How many entries (the root included) and file bytes a copy of <paramref name="path"/> handles.</summary>
    private static (long Items, long Bytes) Measure(string path, CancellationToken token)
    {
        try
        {
            var info = new FileInfo(path);
            if (info.LinkTarget is not null)
                return (1, 0);

            if (!Directory.Exists(path))
                return (1, info.Exists ? info.Length : 0);

            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = 0,
            };

            var walk = new FileSystemEnumerable<long>(path,
                (ref FileSystemEntry entry) =>
                    entry.IsDirectory || (entry.Attributes & FileAttributes.ReparsePoint) != 0 ? 0 : entry.Length,
                options)
            {
                ShouldRecursePredicate = (ref FileSystemEntry entry) =>
                    (entry.Attributes & FileAttributes.ReparsePoint) == 0,
            };

            long items = 1, bytes = 0;
            foreach (var length in walk)
            {
                token.ThrowIfCancellationRequested();
                items++;
                bytes += length;
            }

            return (items, bytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (1, 0);
        }
    }

    /// <summary>Copies one entry and everything in it. True when all of it was copied.</summary>
    private static bool CopyEntry(string source, string destination, IOperationProgress progress,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

        var info = new FileInfo(source);
        if (info.LinkTarget is { } link)
        {
            IOFile.CreateSymbolicLink(destination, link);
            progress.Advance();
            return true;
        }

        if (!Directory.Exists(source))
        {
            CopyFile(source, destination, progress, token);
            progress.Advance();
            return true;
        }

        Directory.CreateDirectory(destination);
        progress.Advance();

        string[] children;
        try
        {
            children = Directory.GetFileSystemEntries(source);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            progress.Fail(source, ex.Message);
            return false;
        }

        var complete = true;
        foreach (var child in children)
        {
            try
            {
                complete &= CopyEntry(child, IOPath.Combine(destination, IOPath.GetFileName(child)), progress, token);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                progress.Fail(child, ex.Message);
                progress.Advance();
                complete = false;
            }
        }

        // Last: a read-only mode on the folder would have kept its entries from being written
        CopyAttributes(source, destination);
        return complete;
    }

    private static void CopyFile(string source, string destination, IOperationProgress progress,
        CancellationToken token)
    {
        var created = false;
        try
        {
            using (var input = new FileStream(source, FileMode.Open, FileAccess.Read,
                       FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.SequentialScan))
            using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1))
            {
                created = true;
                var buffer = new byte[BufferSize];
                int read;
                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    token.ThrowIfCancellationRequested();
                    output.Write(buffer, 0, read);
                    progress.Advance(0, read);
                }
            }

            CopyAttributes(source, destination);
        }
        catch when (created)
        {
            // A half-copied file looks complete in a folder listing: don't leave it behind
            TryDelete(destination);
            throw;
        }
    }

    /// <summary>Times and mode, as cp -p would keep them. What can't be set is left as created.</summary>
    private static void CopyAttributes(string source, string destination)
    {
        try
        {
            IOFile.SetLastWriteTimeUtc(destination, IOFile.GetLastWriteTimeUtc(source));
            if (!OperatingSystem.IsWindows())
                IOFile.SetUnixFileMode(destination, IOFile.GetUnixFileMode(source));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Trace.WriteLine($"Can't copy the attributes of '{source}': {ex.Message}");
        }
    }

    private static void AddToArchive(ZipArchive archive, string path, string entryName, IOperationProgress progress,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

        try
        {
            var info = new FileInfo(path);
            if (Directory.Exists(path))
            {
                var folder = archive.CreateEntry(entryName + "/");
                SetEntryAttributes(folder, path, DirectoryType);
                progress.Advance();

                // A linked folder: stored as an empty folder, never followed (it may point to its own parent)
                if (info.LinkTarget is not null)
                    return;

                foreach (var child in Directory.GetFileSystemEntries(path))
                    AddToArchive(archive, child, entryName + "/" + IOPath.GetFileName(child), progress, token);

                return;
            }

            // Opened first: an entry for a file that can't be read would be an empty file in the archive
            using var input = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.SequentialScan);

            var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
            SetEntryAttributes(entry, path, RegularFileType);

            using (var output = entry.Open())
            {
                var buffer = new byte[BufferSize];
                int read;
                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    token.ThrowIfCancellationRequested();
                    output.Write(buffer, 0, read);
                    progress.Advance(0, read);
                }
            }

            progress.Advance();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            progress.Fail(entryName, ex.Message);
            progress.Advance();
        }
    }

    /// <summary>The modification time, and the Unix mode where unzip and file managers read it.</summary>
    private static void SetEntryAttributes(ZipArchiveEntry entry, string path, UnixFileMode type)
    {
        try
        {
            // Zip stores times from 1980 to 2107 only
            var modified = IOFile.GetLastWriteTime(path);
            if (modified.Year is >= 1980 and <= 2107)
                entry.LastWriteTime = modified;

            if (!OperatingSystem.IsWindows())
                entry.ExternalAttributes = (int)(type | IOFile.GetUnixFileMode(path)) << 16;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Stored with the archive's defaults
        }
    }

    /// <summary>Symlinks are deleted, never followed.</summary>
    internal static void DeleteTree(string path)
    {
        var info = new FileInfo(path);
        if (info.LinkTarget is not null || !Directory.Exists(path))
        {
            IOFile.Delete(path);
            return;
        }

        foreach (var child in Directory.GetFileSystemEntries(path))
            DeleteTree(child);

        Directory.Delete(path);
    }

    private static void TryDelete(string path)
    {
        try
        {
            IOFile.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.WriteLine($"Can't delete '{path}': {ex.Message}");
        }
    }
}
