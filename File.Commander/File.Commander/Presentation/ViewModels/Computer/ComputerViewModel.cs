using System.Globalization;
using File.Commander.Presentation.Services;
using File.Commander.Presentation.ViewModels.Helpers;
using Material.Icons;

namespace File.Commander.Presentation.ViewModels.Computer;

public sealed class ComputerViewModel
{
    public ComputerViewModel(IReadOnlyList<UserDirectory> directories, IReadOnlyList<Volume> volumes)
    {
        Directories = directories.Select(d => new DirectoryCardViewModel(d)).ToList();
        Disks = volumes.Select(v => new DiskCardViewModel(v)).ToList();
    }
 
    public IReadOnlyList<DirectoryCardViewModel> Directories { get; }
 
    public IReadOnlyList<DiskCardViewModel> Disks { get; }
 
    public string ItemCountText
    {
        get
        {
            var count = Directories.Count + Disks.Count;
            return count == 1 ? "1 item" : $"{count} items";
        }
    }
}
 
public sealed class DirectoryCardViewModel
{
    public DirectoryCardViewModel(UserDirectory directory)
    {
        Name = directory.Name;
        Location = directory.Location;
        Badge = LocationIcons.ForBadge(directory.Kind);
    }
 
    public string Name { get; }
 
    public string Location { get; }
 
    public MaterialIconKind Badge { get; }
}
 
public sealed class DiskCardViewModel
{
    private const double AlmostFullPercent = 80;
 
    public DiskCardViewModel(Volume volume)
    {
        Name = volume.Name;
        MountPoint = volume.MountPoint;
        Kind = volume.Kind;
        UsedPercent = volume.TotalBytes > 0 ? volume.UsedBytes * 100.0 / volume.TotalBytes : 0;
        UsageText = $"{FormatSize(volume.UsedBytes)}/{FormatSize(volume.TotalBytes)}";
    }
 
    public string Name { get; }
 
    public string MountPoint { get; }
 
    public VolumeKind Kind { get; }
 
    public double UsedPercent { get; }
 
    public string UsageText { get; }
 
    public bool IsOptical => Kind == VolumeKind.Optical;
 
    public bool IsRemovable => Kind == VolumeKind.Removable;
 
    /// <summary>Switches the usage bar to the orange/yellow gradient.</summary>
    public bool IsAlmostFull => !IsOptical && UsedPercent >= AlmostFullPercent;
 
    public bool HasGlyph => Kind is VolumeKind.System or VolumeKind.Removable;
 
    public MaterialIconKind Glyph => Kind == VolumeKind.Removable ? MaterialIconKind.Eject : MaterialIconKind.Linux;
 
    private static string FormatSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB", "PB" };
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
 
        return $"{value.ToString("0.#", CultureInfo.CurrentCulture)} {units[unit]}";
    }
}