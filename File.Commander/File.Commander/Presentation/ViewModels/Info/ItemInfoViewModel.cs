using System.Globalization;
using System.IO.Enumeration;
using Avalonia;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Material.Icons;

namespace File.Commander.Presentation.ViewModels.Info;

/// <summary>A row of the Sharing &amp; Permissions table: who, and what they may do.</summary>
public sealed record PermissionRowViewModel(string Name, MaterialIconKind Icon, string Privilege);

/// <summary>
/// Everything the info panel shows about one file or folder, like Finder's Get Info.
/// What is slow to read (a folder's size, an image preview, the owner) loads in the background
/// and stops when this is disposed, i.e. as soon as another item is selected.
/// </summary>
public sealed partial class ItemInfoViewModel : ObservableObject, IDisposable
{
    // Decoders Avalonia (Skia) ships with. SVG would need another package: it keeps its icon.
    private static readonly HashSet<string> PreviewExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".ico" };

    private const long MaxPreviewBytes = 64L * 1024 * 1024;
    private const int MaxPreviewWidth = 600;

    // Holding an arrow key selects many items in a row: don't start reading for each of them
    private static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(120);

    private readonly CancellationTokenSource _cts = new();
    private bool _disposed;

    public ItemInfoViewModel(FileEntryViewModel entry)
    {
        Entry = entry;
        Name = entry.Name;
        FullPath = entry.FullPath;
        Icon = entry.Icon;
        IsDirectory = entry.IsDirectory;
        KindText = entry.TypeText;
        WhereText = IOPath.GetDirectoryName(entry.FullPath) ?? string.Empty;
        ModifiedText = FormatLongDate(entry.Modified);
        CreatedText = FormatLongDate(entry.Created);
        AccessedText = FormatLongDate(entry.Accessed);
        HeaderModifiedText = ModifiedText.Length == 0 ? string.Empty : $"Modified: {ModifiedText}";

        var extension = IOPath.GetExtension(entry.Name);
        // ".bashrc" is a name, not an extension
        ExtensionText = entry.IsDirectory || extension.Length <= 1 || extension.Length == entry.Name.Length
            ? "None"
            : extension;
        HiddenText = entry.IsHidden ? "Yes" : "No";

        LinkTarget = ReadLinkTarget(entry);

        if (entry.Permissions is { } mode)
        {
            HasPermissions = true;
            ModeText = $"{entry.PermissionsText} ({Convert.ToString((int)mode & 0xFFF, 8).PadLeft(3, '0')})";
            PermissionRows = BuildRows(mode, owner: "Owner", group: "Group");
        }

        if (entry.IsDirectory)
        {
            SizeText = "Calculating…";
            HeaderSizeText = "…";
        }
        else if (entry.Size is { } bytes)
        {
            SizeText = $"{SizeFormatter.Format(bytes)} ({bytes.ToString("N0", CultureInfo.CurrentCulture)} bytes)";
            HeaderSizeText = SizeFormatter.Format(bytes);
        }

        _ = LoadAsync(_cts.Token); // never throws
    }

    public FileEntryViewModel Entry { get; }

    public string Name { get; }

    public string FullPath { get; }

    public MaterialIconKind Icon { get; }

    public bool IsDirectory { get; }

    /// <summary>"Folder", "PNG image"…</summary>
    public string KindText { get; }

    /// <summary>The folder it is in.</summary>
    public string WhereText { get; }

    public string CreatedText { get; }

    public string ModifiedText { get; }

    public string AccessedText { get; }

    /// <summary>"Modified: …" under the name.</summary>
    public string HeaderModifiedText { get; }

    public string ExtensionText { get; }

    public string HiddenText { get; }

    /// <summary>Where a symlink points. Null for anything else.</summary>
    public string? LinkTarget { get; }

    public bool HasLinkTarget => LinkTarget is not null;

    /// <summary>False where the mode can't be read, and on Windows: the section is then hidden.</summary>
    public bool HasPermissions { get; }

    /// <summary>ls-style plus octal, e.g. "-rw-r--r-- (644)".</summary>
    public string ModeText { get; } = string.Empty;

    /// <summary>A file: its size. A folder: the size of everything in it, once counted.</summary>
    [ObservableProperty]
    public partial string SizeText { get; set; } = string.Empty;

    /// <summary>The short size shown next to the name.</summary>
    [ObservableProperty]
    public partial string HeaderSizeText { get; set; } = string.Empty;

    /// <summary>Shown instead of the icon once decoded. Images only.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPreview), nameof(HasIcon))]
    public partial Bitmap? Preview { get; set; }

    public bool HasPreview => Preview is not null;

    public bool HasIcon => Preview is null;

    /// <summary>"1920 × 1080". Images only.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDimensions), nameof(HasMoreInfo))]
    public partial string? DimensionsText { get; set; }

    public bool HasDimensions => DimensionsText is not null;

    public bool HasMoreInfo => HasDimensions || HasLinkTarget;

    /// <summary>"You can read and write", like Finder. Empty until known.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAccessSummary))]
    public partial string AccessSummary { get; set; } = string.Empty;

    public bool HasAccessSummary => AccessSummary.Length > 0;

    /// <summary>Owner, group, everyone. Named "Owner" and "Group" until the real names are read.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<PermissionRowViewModel> PermissionRows { get; set; } = [];

    private async Task LoadAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(SettleDelay, token);

            var path = FullPath;
            var mode = Entry.Permissions;

            if (mode is { } bits)
            {
                var (ownership, access) = await Task.Run(
                    () => (UnixFileAccess.GetOwnership(path), UnixFileAccess.GetEffectiveAccess(path)), token);

                if (ownership is { } names)
                    PermissionRows = BuildRows(bits, names.Owner, names.Group);

                if (access is { } can)
                    AccessSummary = (can.CanRead, can.CanWrite) switch
                    {
                        (true, true) => "You can read and write",
                        (true, false) => "You can only read",
                        (false, true) => "You can only write",
                        _ => "You have no access",
                    };
            }

            if (IsDirectory)
                await LoadFolderSizeAsync(path, token);
            else if (PreviewExtensions.Contains(IOPath.GetExtension(path)) && Entry.Size is <= MaxPreviewBytes)
                await LoadPreviewAsync(path, token);
        }
        catch (OperationCanceledException)
        {
            // Another item was selected, or the panel's page was left
        }
    }

    private async Task LoadFolderSizeAsync(string path, CancellationToken token)
    {
        // Progress<T> was created on the UI thread, so reports land there
        var progress = new Progress<(long Bytes, long Items)>(p =>
        {
            if (!token.IsCancellationRequested)
                SizeText = $"Calculating… {SizeFormatter.Format(p.Bytes)} so far";
        });

        var (bytes, items, complete) = await Task.Run(() => MeasureFolder(path, progress, token), token);

        var itemsText = items == 1 ? "1 item" : $"{items.ToString("N0", CultureInfo.CurrentCulture)} items";
        SizeText = complete
            ? $"{SizeFormatter.Format(bytes)} for {itemsText}"
            : $"{SizeFormatter.Format(bytes)} for {itemsText} (some folders couldn't be read)";
        HeaderSizeText = SizeFormatter.Format(bytes);
    }

    /// <summary>Walks the whole subtree. Symlinked folders aren't entered, so nothing is counted twice or forever.</summary>
    private static (long Bytes, long Items, bool Complete) MeasureFolder(string path,
        IProgress<(long Bytes, long Items)> progress, CancellationToken token)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = 0,
        };

        long bytes = 0, items = 0;
        var reported = Stopwatch.StartNew();

        try
        {
            var walk = new FileSystemEnumerable<long>(path,
                (ref entry) => entry.IsDirectory ? 0 : entry.Length,
                options)
            {
                ShouldRecursePredicate = (ref entry) =>
                    (entry.Attributes & FileAttributes.ReparsePoint) == 0,
            };

            foreach (var length in walk)
            {
                token.ThrowIfCancellationRequested();
                bytes += length;
                items++;

                if (reported.ElapsedMilliseconds >= 250)
                {
                    progress.Report((bytes, items));
                    reported.Restart();
                }
            }

            return (bytes, items, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (bytes, items, false);
        }
    }

    private async Task LoadPreviewAsync(string path, CancellationToken token)
    {
        var decoded = await Task.Run(() => DecodePreview(path), token);
        if (decoded is not { } result)
            return;

        // Selection moved on while decoding: nobody will show it
        if (token.IsCancellationRequested || _disposed)
        {
            result.Bitmap.Dispose();
            return;
        }

        Preview = result.Bitmap;
        DimensionsText = $"{result.Size.Width} × {result.Size.Height}";
    }

    /// <summary>Decodes once to learn the real size, then keeps a copy no wider than the panel needs.</summary>
    private static (Bitmap Bitmap, PixelSize Size)? DecodePreview(string path)
    {
        try
        {
            using var stream = IOFile.OpenRead(path);
            var full = new Bitmap(stream);
            var size = full.PixelSize;

            if (size.Width <= MaxPreviewWidth)
                return (full, size);

            using (full)
            {
                var height = Math.Max(1, (int)Math.Round((double)size.Height * MaxPreviewWidth / size.Width));
                return (full.CreateScaledBitmap(new PixelSize(MaxPreviewWidth, height)), size);
            }
        }
        catch (Exception ex)
        {
            // Not really an image, truncated, unsupported variant, gone…: keep the icon
            Trace.WriteLine($"No preview for '{path}': {ex.Message}");
            return null;
        }
    }

    private static string? ReadLinkTarget(FileEntryViewModel entry)
    {
        if (!entry.IsSymlink)
            return null;

        try
        {
            FileSystemInfo info = entry.IsDirectory ? new DirectoryInfo(entry.FullPath) : new FileInfo(entry.FullPath);
            return info.LinkTarget;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static IReadOnlyList<PermissionRowViewModel> BuildRows(UnixFileMode mode, string owner, string group) =>
    [
        new(owner, MaterialIconKind.Account,
            Privilege(mode.HasFlag(UnixFileMode.UserRead), mode.HasFlag(UnixFileMode.UserWrite))),
        new(group, MaterialIconKind.AccountMultiple,
            Privilege(mode.HasFlag(UnixFileMode.GroupRead), mode.HasFlag(UnixFileMode.GroupWrite))),
        new("everyone", MaterialIconKind.AccountGroup,
            Privilege(mode.HasFlag(UnixFileMode.OtherRead), mode.HasFlag(UnixFileMode.OtherWrite))),
    ];

    private static string Privilege(bool read, bool write) => (read, write) switch
    {
        (true, true) => "Read & Write",
        (true, false) => "Read only",
        (false, true) => "Write only",
        _ => "No access",
    };

    /// <summary>Finder style: "Tuesday, September 10, 2019 at 9:41 AM" in the current culture.</summary>
    private static string FormatLongDate(DateTime? value) => value is { } date
        ? $"{date.ToString("D", CultureInfo.CurrentCulture)} at {date.ToString("t", CultureInfo.CurrentCulture)}"
        : string.Empty;

    /// <summary>UI thread only. Stops the background reads and frees the preview.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        // Not disposed: LoadAsync may still be between awaits holding the token
        _cts.Cancel();

        var preview = Preview;
        Preview = null;
        preview?.Dispose();
    }
}
