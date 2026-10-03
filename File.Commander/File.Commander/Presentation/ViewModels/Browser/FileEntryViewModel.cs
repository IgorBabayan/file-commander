using System.Globalization;
using File.Commander.Presentation.ViewModels.Helpers;
using Material.Icons;

namespace File.Commander.Presentation.ViewModels.Browser;

public sealed class FileEntryViewModel
{
    private FileEntryViewModel(FileSystemInfo info, Details details, bool showExtension)
    {
        Name = info.Name;
        FullPath = info.FullName;
        IsDirectory = info is DirectoryInfo;
        IsSymlink = details.LinkTarget is not null;
        IsHidden = Name.StartsWith('.');
        DisplayName = showExtension || info is DirectoryInfo ? Name : WithoutExtension(Name);

        var (icon, kind) = Describe(Name, IsDirectory);
        Icon = icon;
        TypeText = IsSymlink ? $"{kind} (link)" : kind;

        Size = details.Size;
        Modified = details.Modified;
        SizeText = details.Size is { } bytes ? SizeFormatter.Format(bytes) : string.Empty;
        ModifiedText = FormatDate(details.Modified);
        CreatedText = FormatDate(details.Created);
        AccessedText = FormatDate(details.Accessed);
        PermissionsText = FormatMode(details.Mode, IsDirectory, IsSymlink);
    }

    /// <summary>The name on disk. Used for sorting.</summary>
    public string Name { get; }

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

    /// <summary>Empty for folders: their size would mean walking the whole subtree.</summary>
    public string SizeText { get; }

    public string ModifiedText { get; }

    /// <summary>Birth time where the filesystem records it; otherwise .NET falls back to the oldest known time.</summary>
    public string CreatedText { get; }

    public string AccessedText { get; }

    /// <summary>ls-style mode, e.g. "drwxr-xr-x". Empty where the mode can't be read.</summary>
    public string PermissionsText { get; }

    /// <param name="showExtension">False: <see cref="DisplayName"/> drops the extension of files.</param>
    public static FileEntryViewModel From(FileSystemInfo info, bool showExtension = true)
        => new(info, Details.Read(info), showExtension);

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
