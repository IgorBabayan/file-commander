using System.Formats.Tar;
using System.IO.Compression;
using File.Commander.Application.Operations;

namespace File.Commander.Presentation.Services;

/// <summary>
/// Extracts archives in the app, as a double click does in GNOME Files: next to the archive, into a folder named after
/// it, or straight next to it when everything in the archive is in one top-level folder or file. Nothing is
/// overwritten: a name that is taken gets a number. Zip, tar and gzip-compressed tar (.tar.gz, .tgz).
/// </summary>
/// <remarks>
/// Extracts into a hidden folder first and moves the result into place once done, so the folder shows complete, and a
/// canceled or failed extraction leaves nothing behind. Entries that would land outside it ("../", absolute paths)
/// are skipped. Links are made last, so no entry is ever written through a link from the archive.
/// </remarks>
public static class ArchiveExtractor
{
    private const int BufferSize = 1 << 20;

    private const int SymlinkType = 0xA000;
    private const int TypeMask = 0xF000;

    private enum Format { Zip, Tar, TarGz }

    // Longest first: ".tar.gz" before ".gz"
    private static readonly (string Extension, Format Format)[] Extensions =
    [
        (".tar.gz", Format.TarGz),
        (".tgz", Format.TarGz),
        (".tar", Format.Tar),
        (".zip", Format.Zip),
    ];

    /// <summary>By name only: cheap enough to call for every entry a menu is built for.</summary>
    public static bool CanExtract(string path) => FormatOf(IOPath.GetFileName(path)) is not null;

    /// <summary>"photos.tar.gz" → "photos": the folder an archive with several top-level entries is extracted into.</summary>
    public static string FolderNameFor(string archiveName)
    {
        foreach (var (extension, _) in Extensions)
        {
            if (archiveName.Length > extension.Length
                && archiveName.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                return archiveName[..^extension.Length];
        }

        return archiveName + " (extracted)";
    }

    /// <summary>
    /// Extracts <paramref name="archivePath"/> next to itself. Returns where the result went, or null when nothing was
    /// extracted (canceled). One entry that fails is reported to <paramref name="progress"/>; an archive that can't be
    /// read at all throws.
    /// </summary>
    public static string? Extract(string archivePath, IOperationProgress progress)
    {
        var token = progress.CancellationToken;
        archivePath = IOPath.GetFullPath(archivePath);
        var name = IOPath.GetFileName(archivePath);
        var format = FormatOf(name) ?? throw new NotSupportedException($"“{name}” isn't an archive that can be extracted.");
        var folder = FileOperations.ParentOf(archivePath);

        // Hidden while it is being filled: the views don't list it until it is complete
        var staging = IOPath.Combine(folder, $".{name}.extracting-{Guid.NewGuid():N}");
        Directory.CreateDirectory(staging);

        var moved = false;
        try
        {
            if (format == Format.Zip)
                ExtractZip(archivePath, staging, progress, token);
            else
                ExtractTar(archivePath, staging, format == Format.TarGz, progress, token);

            if (token.IsCancellationRequested)
                return null;

            var result = MoveIntoPlace(staging, folder, FolderNameFor(name));
            moved = true;
            return result;
        }
        finally
        {
            // Canceled or failed: the half-extracted files go. After a move only the empty staging folder is left.
            if (!moved || Directory.Exists(staging))
                TryDeleteTree(staging);
        }
    }

    private static Format? FormatOf(string name)
    {
        foreach (var (extension, format) in Extensions)
        {
            if (name.Length > extension.Length && name.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                return format;
        }

        return null;
    }

    /// <summary>One top-level entry: it goes next to the archive itself. Several: in a folder named after the archive.</summary>
    private static string MoveIntoPlace(string staging, string folder, string folderName)
    {
        var top = Directory.GetFileSystemEntries(staging);
        if (top.Length == 1)
        {
            var destination = FileOperations.UniquePath(folder, IOPath.GetFileName(top[0]));
            FileOperations.MoveEntry(top[0], destination);
            return destination;
        }

        var target = FileOperations.UniquePath(folder, folderName);
        Directory.Move(staging, target);
        return target;
    }

    // ===== Zip =====

    private static void ExtractZip(string archivePath, string root, IOperationProgress progress, CancellationToken token)
    {
        using var archive = ZipFile.OpenRead(archivePath);

        long bytes = 0;
        foreach (var entry in archive.Entries)
            bytes += entry.Length;
        progress.SetTotal(archive.Entries.Count, bytes);

        var links = new List<(string Path, string Target, string Name)>();
        var folders = new List<(string Path, UnixFileMode? Mode, DateTimeOffset Modified)>();

        foreach (var entry in archive.Entries)
        {
            if (token.IsCancellationRequested)
                return;

            var name = entry.FullName.Replace('\\', '/');
            progress.Begin(name);

            if (TargetPath(root, name) is not { } path)
            {
                Skip(progress, name, "It would be extracted outside the folder", entry.Length);
                continue;
            }

            var unixMode = (entry.ExternalAttributes >> 16) & 0xFFFF;
            var isFolder = name.EndsWith('/') || (unixMode & TypeMask) == 0x4000;
            var mode = (unixMode & 0xFFF) != 0 ? (UnixFileMode?)(unixMode & 0xFFF) : null;

            try
            {
                if (isFolder)
                {
                    Directory.CreateDirectory(path);
                    folders.Add((path, mode, entry.LastWriteTime));
                    progress.Advance();
                    continue;
                }

                if ((unixMode & TypeMask) == SymlinkType)
                {
                    // The link's target is the entry's content
                    using var reader = new StreamReader(entry.Open());
                    links.Add((path, reader.ReadToEnd(), name));
                    progress.Advance(1, entry.Length);
                    continue;
                }

                using (var input = entry.Open())
                    WriteFile(input, path, progress, token);

                SetAttributes(path, mode, entry.LastWriteTime);
                progress.Advance();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException
                                           or NotSupportedException)
            {
                progress.Fail(name, ex.Message);
                progress.Advance();
            }
        }

        if (token.IsCancellationRequested)
            return;

        CreateLinks(links, root, progress);
        FinishFolders(folders);
    }

    // ===== Tar =====

    private static void ExtractTar(string archivePath, string root, bool gzip, IOperationProgress progress,
        CancellationToken token)
    {
        using var file = new FileStream(archivePath, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, BufferSize, FileOptions.SequentialScan);
        using var decompressed = gzip ? new GZipStream(file, CompressionMode.Decompress) : null;
        using var reader = new TarReader(decompressed ?? (Stream)file);

        // A tar has no index: the share of the archive read so far drives the percentage
        progress.SetTotal(-1, file.Length);
        long reported = 0;

        var links = new List<(string Path, string Target, string Name)>();
        var hardLinks = new List<(string Path, string Target, string Name)>();
        var folders = new List<(string Path, UnixFileMode? Mode, DateTimeOffset Modified)>();

        while (reader.GetNextEntry() is { } entry)
        {
            if (token.IsCancellationRequested)
                return;

            // Metadata for the entries after it, read by TarReader itself: not an item
            if (entry.EntryType == TarEntryType.GlobalExtendedAttributes)
                continue;

            var name = entry.Name.Replace('\\', '/');
            progress.Begin(name);

            try
            {
                if (TargetPath(root, name) is not { } path)
                {
                    progress.Fail(name, "It would be extracted outside the folder");
                    continue;
                }

                var mode = (UnixFileMode?)entry.Mode;
                switch (entry.EntryType)
                {
                    case TarEntryType.Directory:
                        Directory.CreateDirectory(path);
                        folders.Add((path, mode, entry.ModificationTime));
                        break;

                    case TarEntryType.RegularFile or TarEntryType.V7RegularFile or TarEntryType.ContiguousFile:
                        WriteFile(entry.DataStream ?? Stream.Null, path, null, token);
                        SetAttributes(path, mode, entry.ModificationTime);
                        break;

                    case TarEntryType.SymbolicLink:
                        links.Add((path, entry.LinkName, name));
                        break;

                    case TarEntryType.HardLink:
                        hardLinks.Add((path, entry.LinkName, name));
                        break;

                    // Devices, fifos and the like aren't made by a user's file manager
                    default:
                        progress.Fail(name, "Special files aren't extracted");
                        break;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                progress.Fail(name, ex.Message);
            }
            finally
            {
                // Items always count; bytes as the archive is read
                progress.Advance(1, file.Position - reported);
                reported = file.Position;
            }
        }

        if (token.IsCancellationRequested)
            return;

        // A hard link is a second name for a file from the archive: a copy of it, as nothing links across folders here
        foreach (var (path, target, name) in hardLinks)
        {
            try
            {
                if (TargetPath(root, target) is { } source && IOFile.Exists(source))
                {
                    Directory.CreateDirectory(FileOperations.ParentOf(path));
                    IOFile.Copy(source, path, overwrite: true);
                }
                else
                {
                    progress.Fail(name, "The file it links to isn't in the archive");
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                progress.Fail(name, ex.Message);
            }
        }

        CreateLinks(links, root, progress);
        FinishFolders(folders);
    }

    // ===== Helpers =====

    /// <summary>
    /// Where <paramref name="entryName"/> goes under <paramref name="root"/>. Null for names that would leave it
    /// ("../x", "a/../../x") or that name nothing. A leading "/" is dropped, as tar and unzip do.
    /// </summary>
    private static string? TargetPath(string root, string entryName)
    {
        var parts = entryName.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Where(part => part != ".")
            .ToList();

        if (parts.Count == 0 || parts.Any(part => part == ".." || part.Contains('\0')))
            return null;

        return IOPath.Combine(root, string.Join('/', parts));
    }

    /// <param name="progress">Null: bytes are counted by the caller.</param>
    private static void WriteFile(Stream input, string path, IOperationProgress? progress, CancellationToken token)
    {
        Directory.CreateDirectory(FileOperations.ParentOf(path));

        // Create, not CreateNew: an archive may hold the same name twice, the last one wins as with unzip
        using var output = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1);
        var buffer = new byte[BufferSize];
        int read;
        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            token.ThrowIfCancellationRequested();
            output.Write(buffer, 0, read);
            progress?.Advance(0, read);
        }
    }

    /// <summary>Made once every file is written, so nothing is written through them.</summary>
    private static void CreateLinks(List<(string Path, string Target, string Name)> links, string root,
        IOperationProgress progress)
    {
        foreach (var (path, target, name) in links)
        {
            try
            {
                if (FileOperations.Exists(path))
                {
                    progress.Fail(name, "Another item in the archive has the same name");
                    continue;
                }

                Directory.CreateDirectory(FileOperations.ParentOf(path));
                IOFile.CreateSymbolicLink(path, target);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                progress.Fail(name, ex.Message);
            }
        }
    }

    /// <summary>Deepest first, and last: a read-only mode would have kept the folder's entries from being written.</summary>
    private static void FinishFolders(List<(string Path, UnixFileMode? Mode, DateTimeOffset Modified)> folders)
    {
        foreach (var (path, mode, modified) in folders.OrderByDescending(folder => folder.Path.Length))
        {
            // Never lock the owner out of a folder they have to be able to delete
            SetAttributes(path, mode | UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
                modified);
        }
    }

    /// <summary>The stored mode and time. What can't be set is left as created.</summary>
    private static void SetAttributes(string path, UnixFileMode? mode, DateTimeOffset modified)
    {
        try
        {
            if (modified.Year >= 1980)
                IOFile.SetLastWriteTimeUtc(path, modified.UtcDateTime);

            // Only the permission bits: set-user-ID and the like from an archive aren't kept
            if (mode is { } bits && !OperatingSystem.IsWindows())
                IOFile.SetUnixFileMode(path, bits & (UnixFileMode)0x1FF);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Trace.WriteLine($"Can't set the attributes of '{path}': {ex.Message}");
        }
    }

    private static void Skip(IOperationProgress progress, string name, string reason, long bytes)
    {
        progress.Fail(name, reason);
        progress.Advance(1, bytes);
    }

    private static void TryDeleteTree(string path)
    {
        try
        {
            FileOperations.DeleteTree(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.WriteLine($"Can't delete '{path}': {ex.Message}");
        }
    }
}
