using System.Globalization;
using File.Commander.Presentation.Services;
using Material.Icons;

namespace File.Commander.Presentation.ViewModels.Browser;

public sealed class FileEntryViewModel
{
    private const string DirectoryType = "inode/directory";
    private const string UnknownType = "application/octet-stream";
    private const string EmptyType = "application/x-zerosize";

    // EnumerationOptions skip hidden (dot) files unless told otherwise
    private static readonly EnumerationOptions CountWithoutHidden = new() { IgnoreInaccessible = false };
    private static readonly EnumerationOptions CountWithHidden = new() { IgnoreInaccessible = false, AttributesToSkip = 0 };

    // Nautilus' basic types (nautilus-file.c, mime_type_map): the type's generic icon tells its family
    private static readonly Dictionary<string, string> BasicTypes = new(StringComparer.Ordinal)
    {
        ["application-x-executable"] = "Program",
        ["audio-x-generic"] = "Audio",
        ["font-x-generic"] = "Font",
        ["image-x-generic"] = "Image",
        ["package-x-generic"] = "Archive",
        ["text-html"] = "Markup",
        ["text-x-generic"] = "Text",
        ["text-x-generic-template"] = "Text",
        ["text-x-script"] = "Program",
        ["video-x-generic"] = "Video",
        ["x-office-address-book"] = "Contacts",
        ["x-office-calendar"] = "Calendar",
        ["x-office-document"] = "Document",
        ["x-office-presentation"] = "Presentation",
        ["x-office-spreadsheet"] = "Spreadsheet",
    };

    private readonly Lazy<int?>? _itemCount;

    private FileEntryViewModel(FileSystemInfo info, Details details, bool showExtension, string? name, bool countHidden)
    {
        Name = name ?? info.Name;
        NameKey = new FileNameKey(Name);
        FullPath = info.FullName;
        ParentPath = IOPath.GetDirectoryName(FullPath) ?? string.Empty;
        IsDirectory = info is DirectoryInfo;
        IsSymlink = details.LinkTarget is not null;
        IsHidden = Name.StartsWith('.');
        DisplayName = showExtension || info is DirectoryInfo ? Name : WithoutExtension(Name);

        var (icon, kind) = Describe(Name, IsDirectory);
        Icon = icon;
        TypeText = IsSymlink ? $"{kind} (link)" : kind;
        (MimeType, BasicType) = Classify(info.FullName, Name, IsDirectory, details.Size, details.Mode);

        // Counted when first asked for: only sorting by size needs it, and it reads the folder
        var path = FullPath;
        _itemCount = IsDirectory ? new Lazy<int?>(() => CountItems(path, countHidden)) : null;

        Size = details.Size;
        Modified = details.Modified;
        Created = details.Created;
        Accessed = details.Accessed;
        Permissions = details.Mode;
        SizeText = details.Size is { } bytes ? SizeFormatter.Format(bytes) : string.Empty;
        ModifiedText = FormatDate(details.Modified);
        CreatedText = FormatDate(details.Created);
        AccessedText = FormatDate(details.Accessed);
        PermissionsText = FormatMode(details.Mode, IsDirectory, IsSymlink);
    }

    /// <summary>The name on disk; for an item in the trash, the name it had before. Used for sorting.</summary>
    public string Name { get; }

    /// <summary>How <see cref="Name"/> sorts: as in GNOME Files, "file2" before "file10".</summary>
    public FileNameKey NameKey { get; }

    /// <summary>The folder the entry is in. Search results come from many; sorting keeps them together.</summary>
    public string ParentPath { get; }

    /// <summary>"inode/directory" for folders, else the shared-mime-info type. Used for sorting by type.</summary>
    public string MimeType { get; }

    /// <summary>
    /// The family of the type as GNOME Files names it: "Folder", "Archive", "Document", "Text", ..., "Other" for a
    /// known type of no family, "Binary" or "Program" for an unknown one. What sorting by type groups by.
    /// </summary>
    public string BasicType { get; }

    /// <summary>
    /// Folders: the entries in them (dot files only while hidden files are shown), null when they can't be read.
    /// Files: null. Reads the folder the first time: ask off the UI thread.
    /// </summary>
    public int? ItemCount => _itemCount?.Value;

    /// <summary>A folder whose <see cref="ItemCount"/> hasn't been read yet.</summary>
    internal bool NeedsItemCount => _itemCount is { IsValueCreated: false };

    /// <summary>What the views show: <see cref="Name"/>, without the extension when extensions are hidden.</summary>
    public string DisplayName { get; }

    public string FullPath { get; }

    public bool IsDirectory { get; }

    public bool IsSymlink { get; }

    /// <summary>A dot file. Only listed when hidden files are shown, and then drawn dimmed.</summary>
    public bool IsHidden { get; }

    public MaterialIconKind Icon { get; }

    /// <summary>"Folder", "PNG image", "JSON source", ... plus " (link)" for symlinks.</summary>
    public string TypeText { get; }

    /// <summary>Null for folders and when it can't be read. Used for sorting.</summary>
    public long? Size { get; }

    /// <summary>Null when it can't be read. Used for sorting.</summary>
    public DateTime? Modified { get; }

    /// <summary>Null when it can't be read. Used for sorting.</summary>
    public DateTime? Created { get; }

    /// <summary>Null when it can't be read. Used for sorting.</summary>
    public DateTime? Accessed { get; }

    /// <summary>Null when it can't be read, and on Windows. Used for sorting.</summary>
    public UnixFileMode? Permissions { get; }

    /// <summary>Empty for folders: their size would mean walking the whole subtree.</summary>
    public string SizeText { get; }

    public string ModifiedText { get; }

    /// <summary>Birth time where the filesystem records it; otherwise .NET falls back to the oldest known time.</summary>
    public string CreatedText { get; }

    public string AccessedText { get; }

    /// <summary>ls-style mode, e.g. "drwxr-xr-x". Empty where the mode can't be read.</summary>
    public string PermissionsText { get; }

    /// <param name="countHidden">Whether a folder's <see cref="ItemCount"/> includes its dot files.</param>
    public static FileEntryViewModel From(FileSystemInfo info, bool showExtension = true, string? name = null,
        bool countHidden = false)
        => new(info, Details.Read(info), showExtension, name, countHidden);

    /// <summary>
    /// The MIME type and its basic type (<see cref="BasicType"/>). An empty file is never read: it may be a pipe or
    /// a device, which would block, and holds nothing to tell its type by anyway.
    /// </summary>
    private static (string MimeType, string BasicType) Classify(string path, string name, bool isDirectory,
        long? size, UnixFileMode? mode)
    {
        if (isDirectory)
            return (DirectoryType, "Folder");

        var mime = MimeDatabase.Default;
        var type = size is > 0 ? mime.TypeOf(path, name) : mime.TypeOfName(name) ?? EmptyType;

        if (type == UnknownType)
            return (type, mode is { } bits && bits.HasFlag(UnixFileMode.UserExecute) ? "Program" : "Binary");

        return (type, BasicTypes.GetValueOrDefault(mime.GenericIconName(type), "Other"));
    }

    private static int? CountItems(string path, bool countHidden)
    {
        try
        {
            return Directory.EnumerateFileSystemEntries(path, "*", countHidden ? CountWithHidden : CountWithoutHidden)
                .Count();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string WithoutExtension(string name)
    {
        var extension = IOPath.GetExtension(name);

        // ".bashrc" is a name, not an extension
        return extension.Length <= 1 || extension.Length == name.Length ? name : name[..^extension.Length];
    }

    /// <summary>Everything that can fail to read. Each value is read on its own, so one failure doesn't hide the rest.</summary>
    private readonly record struct Details(
        long? Size, DateTime? Modified, DateTime? Created, DateTime? Accessed, UnixFileMode? Mode, string? LinkTarget)
    {
        public static Details Read(FileSystemInfo info) => new(
            Try(() => info is FileInfo file ? file.Length : (long?)null),
            Try(() => (DateTime?)info.LastWriteTime),
            Try(() => (DateTime?)info.CreationTime),
            Try(() => (DateTime?)info.LastAccessTime),
            OperatingSystem.IsWindows() ? null : Try(() => (UnixFileMode?)info.UnixFileMode),
            Try(() => info.LinkTarget));

        // Broken symlink or a file that vanished: still list it, just without that detail
        private static T? Try<T>(Func<T?> read)
        {
            try
            {
                return read();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return default;
            }
        }
    }

    private static string FormatDate(DateTime? value)
        => value?.ToString("g", CultureInfo.CurrentCulture) ?? string.Empty;

    private static string FormatMode(UnixFileMode? mode, bool isDirectory, bool isSymlink)
    {
        if (mode is not { } m)
            return string.Empty;

        return string.Create(10, m, (s, bits) =>
        {
            s[0] = isSymlink ? 'l' : isDirectory ? 'd' : '-';
            s[1] = bits.HasFlag(UnixFileMode.UserRead) ? 'r' : '-';
            s[2] = bits.HasFlag(UnixFileMode.UserWrite) ? 'w' : '-';
            s[3] = Exec(bits, UnixFileMode.UserExecute, UnixFileMode.SetUser, 's');
            s[4] = bits.HasFlag(UnixFileMode.GroupRead) ? 'r' : '-';
            s[5] = bits.HasFlag(UnixFileMode.GroupWrite) ? 'w' : '-';
            s[6] = Exec(bits, UnixFileMode.GroupExecute, UnixFileMode.SetGroup, 's');
            s[7] = bits.HasFlag(UnixFileMode.OtherRead) ? 'r' : '-';
            s[8] = bits.HasFlag(UnixFileMode.OtherWrite) ? 'w' : '-';
            s[9] = Exec(bits, UnixFileMode.OtherExecute, UnixFileMode.StickyBit, 't');
        });

        // As ls: 's'/'t' when the special bit and x are set, 'S'/'T' when only the special bit is
        static char Exec(UnixFileMode bits, UnixFileMode execute, UnixFileMode special, char mark)
            => (bits.HasFlag(execute), bits.HasFlag(special)) switch
            {
                (true, true) => mark,
                (false, true) => char.ToUpperInvariant(mark),
                (true, false) => 'x',
                _ => '-',
            };
    }

    /// <summary>Glyph and a type name built from the extension, e.g. ".png" → "PNG image".</summary>
    private static (MaterialIconKind Icon, string Kind) Describe(string name, bool isDirectory)
    {
        if (isDirectory)
            return (MaterialIconKind.Folder, "Folder");

        var extension = IOPath.GetExtension(name).ToLowerInvariant();

        // ".bashrc" is a name, not an extension
        if (extension.Length <= 1 || extension.Length == name.Length)
            return (MaterialIconKind.FileOutline, "File");

        var (icon, noun) = extension switch
        {
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".bmp" or ".svg" or ".ico" => (MaterialIconKind.FileImageOutline, "image"),
            ".mp4" or ".mkv" or ".webm" or ".avi" or ".mov" => (MaterialIconKind.FileVideoOutline, "video"),
            ".mp3" or ".flac" or ".ogg" or ".opus" or ".wav" or ".m4a" => (MaterialIconKind.FileMusicOutline, "audio"),
            ".zip" or ".tar" or ".gz" or ".xz" or ".zst" or ".bz2" or ".7z" or ".rar" => (MaterialIconKind.FolderZipOutline, "archive"),
            ".pdf" => (MaterialIconKind.FilePdfBox, "document"),
            ".txt" or ".md" or ".log" or ".doc" or ".docx" or ".odt" => (MaterialIconKind.FileDocumentOutline, "document"),
            ".cs" or ".axaml" or ".xml" or ".json" or ".sh" or ".py" or ".js" or ".ts" or ".c" or ".h" or ".cpp" or ".rs" or ".go"
                => (MaterialIconKind.FileCodeOutline, "source"),
            _ => (MaterialIconKind.FileOutline, "file"),
        };

        return (icon, $"{extension[1..].ToUpperInvariant()} {noun}");
    }
}
