using System.Globalization;
using File.Commander.Presentation.ViewModels.Helpers;
using Material.Icons;

namespace File.Commander.Presentation.ViewModels.Browser;

public sealed class FileEntryViewModel
{
    private FileEntryViewModel(string name, string fullPath, bool isDirectory, long? size, DateTime? modified)
    {
        Name = name;
        FullPath = fullPath;
        IsDirectory = isDirectory;
        Icon = IconFor(name, isDirectory);
        SizeText = size is { } bytes ? SizeFormatter.Format(bytes) : string.Empty;
        ModifiedText = modified?.ToString("g", CultureInfo.CurrentCulture) ?? string.Empty;
    }

    public string Name { get; }

    public string FullPath { get; }

    public bool IsDirectory { get; }

    public MaterialIconKind Icon { get; }

    public string SizeText { get; }

    public string ModifiedText { get; }

    /// <summary>Folders first, then by name.</summary>
    public static IComparer<FileEntryViewModel> Comparer { get; } = Comparer<FileEntryViewModel>.Create((a, b) =>
        a.IsDirectory != b.IsDirectory
            ? a.IsDirectory ? -1 : 1
            : string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));

    public static FileEntryViewModel From(FileSystemInfo info)
    {
        var isDirectory = info is DirectoryInfo;
        long? size = null;
        DateTime? modified = null;

        try
        {
            modified = info.LastWriteTime;
            if (info is FileInfo file)
                size = file.Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Broken symlink or a file that vanished: still list it, just without details
        }

        return new FileEntryViewModel(info.Name, info.FullName, isDirectory, size, modified);
    }

    private static MaterialIconKind IconFor(string name, bool isDirectory)
    {
        if (isDirectory)
            return MaterialIconKind.Folder;

        return IOPath.GetExtension(name).ToLowerInvariant() switch
        {
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".bmp" or ".svg" or ".ico" => MaterialIconKind.FileImageOutline,
            ".mp4" or ".mkv" or ".webm" or ".avi" or ".mov" => MaterialIconKind.FileVideoOutline,
            ".mp3" or ".flac" or ".ogg" or ".opus" or ".wav" or ".m4a" => MaterialIconKind.FileMusicOutline,
            ".zip" or ".tar" or ".gz" or ".xz" or ".zst" or ".bz2" or ".7z" or ".rar" => MaterialIconKind.FolderZipOutline,
            ".pdf" => MaterialIconKind.FilePdfBox,
            ".txt" or ".md" or ".log" or ".doc" or ".docx" or ".odt" => MaterialIconKind.FileDocumentOutline,
            ".cs" or ".axaml" or ".xml" or ".json" or ".sh" or ".py" or ".js" or ".ts" or ".c" or ".h" or ".cpp" or ".rs" or ".go"
                => MaterialIconKind.FileCodeOutline,
            _ => MaterialIconKind.FileOutline,
        };
    }
}
