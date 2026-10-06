using System.Collections.ObjectModel;
using System.Globalization;
using System.IO.Enumeration;
using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Material.Icons;

namespace File.Commander.Presentation.ViewModels.Dialogs;

/// <summary>One line of the Properties dialog. The value of a row that is still being counted changes.</summary>
public sealed partial class PropertyRowViewModel : ObservableObject
{
    public PropertyRowViewModel(string label, string value)
    {
        Label = label;
        Value = value;
    }

    public string Label { get; }

    [ObservableProperty]
    public partial string Value { get; set; }
}

/// <summary>
/// Properties of a sidebar item (a folder, a drive, Computer, Network or the trash) or of the files and folders
/// selected in a view. Folder and trash sizes are counted in the background; disposing the view model
/// (closing the dialog) stops the count.
/// </summary>
public sealed partial class PropertiesViewModel : ViewModelBase
{
    private readonly CancellationTokenSource _cts = new();

    private PropertiesViewModel(string name, MaterialIconKind icon)
    {
        Name = name;
        Icon = icon;
    }

    /// <summary>Raised by Close. The window closes itself.</summary>
    public event Action? CloseRequested;

    public string Name { get; }

    public MaterialIconKind Icon { get; }

    public string Title => $"{Name} — Properties";

    public ObservableCollection<PropertyRowViewModel> Rows { get; } = [];

    /// <summary>Call on the UI thread: the counts report their progress to the thread that started them.</summary>
    public static PropertiesViewModel For(SidebarItem item, bool allTrashBins) => item.Location switch
    {
        Locations.Trash => ForTrash(item, TrashBins.Find(allTrashBins)),
        Locations.Computer => ForComputer(item),
        Locations.Network => ForVirtual(item, "Network"),
        Locations.Recent => ForVirtual(item, "Recently used files"),
        _ when Locations.IsVirtual(item.Location) => ForVirtual(item, "Location"),
        _ => ForFolder(item),
    };

    /// <summary>
    /// Properties of entries of a folder view (the context menu, Alt+Enter). One entry: what it is, its size and dates.
    /// Several: how many there are of each kind and what they hold together. Call on the UI thread.
    /// </summary>
    public static PropertiesViewModel For(IReadOnlyList<FileEntryViewModel> entries)
    {
        if (entries.Count == 1)
            return ForEntry(entries[0]);

        var properties = new PropertiesViewModel(ItemsText(entries.Count), MaterialIconKind.FileMultipleOutline);

        var folders = entries.Count(entry => entry.IsDirectory);
        var files = entries.Count - folders;
        properties.Add("Type", (folders, files) switch
        {
            (0, _) => "Files",
            (_, 0) => "Folders",
            _ => $"{CountText(folders, "folder", "folders")}, {CountText(files, "file", "files")}",
        });

        var parents = entries.Select(entry => IOPath.GetDirectoryName(entry.FullPath) ?? "/").Distinct().ToList();
        properties.Add("Location", parents.Count == 1 ? parents[0] : "Several folders");

        var contents = properties.Add("Contents", "Calculating…");
        _ = properties.CountAsync(contents, entries.Select(entry => entry.FullPath).ToList(), emptyText: "Empty");
        return properties;
    }

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke();

    protected override void OnDispose()
    {
        // Not disposed: a count still running checks the token after it was cancelled
        _cts.Cancel();
        base.OnDispose();
    }

    private static PropertiesViewModel ForFolder(SidebarItem item)
    {
        var properties = new PropertiesViewModel(item.Title, item.Icon);
        var path = Locations.Normalize(item.Location);
        var volume = SystemLocations.GetVolumes().FirstOrDefault(v => Locations.AreEqual(v.MountPoint, path));

        properties.Add("Type", volume is null ? "Folder" : DriveKindText(volume.Kind));
        properties.Add("Location", path);

        if (!Directory.Exists(path))
        {
            properties.Add("Status", "This folder can't be found");
            return properties;
        }

        if (volume is not null)
        {
            // A whole drive would take minutes to count: show its usage instead
            if (DriveFormat(path) is { } format)
                properties.Add("File system", format);

            var usedPercent = volume.TotalBytes > 0 ? volume.UsedBytes * 100.0 / volume.TotalBytes : 0;
            properties.Add("Capacity", SizeFormatter.Format(volume.TotalBytes));
            properties.Add("Used", $"{SizeFormatter.Format(volume.UsedBytes)} ({usedPercent.ToString("0", CultureInfo.CurrentCulture)}%)");
            properties.Add("Free", SizeFormatter.Format(volume.FreeBytes));
        }
        else
        {
            var contents = properties.Add("Contents", "Calculating…");
            _ = properties.CountAsync(contents, [path], emptyText: "Empty folder");
        }

        try
        {
            var info = new DirectoryInfo(path);
            properties.Add("Modified", FormatDate(info.LastWriteTime));
            properties.Add("Created", FormatDate(info.CreationTime));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.WriteLine($"Can't read the dates of '{path}': {ex.Message}");
        }

        return properties;
    }

    private static PropertiesViewModel ForEntry(FileEntryViewModel entry)
    {
        var properties = new PropertiesViewModel(entry.Name, entry.Icon);
        properties.Add("Type", entry.TypeText);
        properties.Add("Location", IOPath.GetDirectoryName(entry.FullPath) ?? "/");

        if (entry.IsSymlink && LinkTarget(entry.FullPath) is { } target)
            properties.Add("Link to", target);

        if (entry.IsDirectory)
        {
            var contents = properties.Add("Contents", "Calculating…");
            _ = properties.CountAsync(contents, [entry.FullPath], emptyText: "Empty folder");
        }
        else
        {
            properties.Add("Size", entry.Size is { } size
                ? $"{SizeFormatter.Format(size)} ({size.ToString("N0", CultureInfo.CurrentCulture)} bytes)"
                : "Unknown");
        }

        if (entry.Modified is { } modified)
            properties.Add("Modified", FormatDate(modified));
        if (entry.Created is { } created)
            properties.Add("Created", FormatDate(created));
        if (entry.Accessed is { } accessed)
            properties.Add("Accessed", FormatDate(accessed));

        if (entry.PermissionsText.Length > 0)
            properties.Add("Permissions", entry.PermissionsText);
        if (UnixFileAccess.GetOwnership(entry.FullPath) is { } ownership)
            properties.Add("Owner", $"{ownership.Owner} (group {ownership.Group})");

        return properties;
    }

    private static string? LinkTarget(string path)
    {
        try
        {
            return new FileInfo(path).LinkTarget;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static PropertiesViewModel ForTrash(SidebarItem item, IReadOnlyList<string> bins)
    {
        var properties = new PropertiesViewModel(item.Title, item.Icon);
        properties.Add("Type", "Trash");
        properties.Add("Location", bins.Count == 0 ? TrashBins.HomeTrash : string.Join(Environment.NewLine, bins));

        var contents = properties.Add("Contents", "Calculating…");
        var folders = bins.Select(bin => IOPath.Combine(bin, "files")).Where(Directory.Exists).ToList();
        _ = properties.CountAsync(contents, folders, emptyText: "Empty");
        return properties;
    }

    private static PropertiesViewModel ForComputer(SidebarItem item)
    {
        var properties = new PropertiesViewModel(item.Title, item.Icon);
        var volumes = SystemLocations.GetVolumes();

        properties.Add("Type", "Computer");
        properties.Add("Name", Environment.MachineName);
        properties.Add("System", RuntimeInformation.OSDescription);
        properties.Add("Drives", volumes.Count.ToString("N0", CultureInfo.CurrentCulture));
        properties.Add("Capacity", SizeFormatter.Format(volumes.Sum(v => v.TotalBytes)));
        properties.Add("Free", SizeFormatter.Format(volumes.Sum(v => v.FreeBytes)));
        return properties;
    }

    private static PropertiesViewModel ForVirtual(SidebarItem item, string type)
    {
        var properties = new PropertiesViewModel(item.Title, item.Icon);
        properties.Add("Type", type);
        properties.Add("Location", item.Location);
        return properties;
    }

    private PropertyRowViewModel Add(string label, string value)
    {
        var row = new PropertyRowViewModel(label, value);
        Rows.Add(row);
        return row;
    }

    /// <summary>Counts everything under <paramref name="roots"/> into <paramref name="row"/>. Never throws.</summary>
    private async Task CountAsync(PropertyRowViewModel row, IReadOnlyList<string> roots, string emptyText)
    {
        var token = _cts.Token;

        // Created on the UI thread, so the reports land there
        var progress = new Progress<(long Bytes, long Items)>(p =>
        {
            if (!token.IsCancellationRequested)
                row.Value = $"Calculating… {ItemsText(p.Items)}, {SizeFormatter.Format(p.Bytes)}";
        });

        try
        {
            var (bytes, items, complete) = await Task.Run(() => Measure(roots, progress, token), token);

            row.Value = items == 0
                ? complete ? emptyText : "Can't be read"
                : complete
                    ? $"{ItemsText(items)}, {SizeFormatter.Format(bytes)}"
                    : $"{ItemsText(items)}, {SizeFormatter.Format(bytes)} (some folders couldn't be read)";
        }
        catch (OperationCanceledException)
        {
            // The dialog was closed
        }
    }

    /// <summary>Walks every subtree. Symlinked folders aren't entered, so nothing is counted twice or forever.</summary>
    private static (long Bytes, long Items, bool Complete) Measure(IReadOnlyList<string> roots,
        IProgress<(long Bytes, long Items)> progress, CancellationToken token)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = 0,
        };

        long bytes = 0, items = 0;
        var complete = true;
        var reported = Stopwatch.StartNew();

        foreach (var root in roots)
        {
            token.ThrowIfCancellationRequested();

            // A selected file: counted as itself
            if (!Directory.Exists(root))
            {
                if (TryLength(root) is { } length)
                {
                    bytes += length;
                    items++;
                }
                else
                {
                    complete = false;
                }

                continue;
            }

            try
            {
                var walk = new FileSystemEnumerable<long>(root,
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
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                complete = false;
            }
        }

        return (bytes, items, complete);
    }

    private static long? TryLength(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? info.Length : info.LinkTarget is not null ? 0 : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string CountText(int count, string one, string many)
        => count == 1 ? $"1 {one}" : $"{count.ToString("N0", CultureInfo.CurrentCulture)} {many}";

    private static string ItemsText(long items)
        => items == 1 ? "1 item" : $"{items.ToString("N0", CultureInfo.CurrentCulture)} items";

    private static string FormatDate(DateTime date) => date.ToString("f", CultureInfo.CurrentCulture);

    private static string DriveKindText(VolumeKind kind) => kind switch
    {
        VolumeKind.System => "System disk",
        VolumeKind.Removable => "Removable drive",
        VolumeKind.Optical => "Optical disc",
        _ => "Disk",
    };

    private static string? DriveFormat(string mountPoint)
    {
        try
        {
            return new DriveInfo(mountPoint).DriveFormat;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }
}
