using CommunityToolkit.Mvvm.Input;
using File.Commander.Presentation.Services;
using File.Commander.Presentation.ViewModels.Helpers;
using File.Commander.Presentation.ViewModels.Pages;
using Material.Icons;

namespace File.Commander.Presentation.ViewModels.Computer;

public sealed class ComputerViewModel : PageViewModel
{
    public ComputerViewModel(IReadOnlyList<UserDirectory> directories, IReadOnlyList<Volume> volumes, INavigator navigator)
    {
        Directories = directories.Select(d => new DirectoryCardViewModel(d, navigator)).ToList();
        Disks = volumes.Select(v => new DiskCardViewModel(v, navigator)).ToList();
    }

    public override string Location => Locations.Computer;

    public override string Title => "Computer";

    public IReadOnlyList<DirectoryCardViewModel> Directories { get; }

    public IReadOnlyList<DiskCardViewModel> Disks { get; }

    public override string StatusText
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
    public DirectoryCardViewModel(UserDirectory directory, INavigator navigator)
    {
        Name = directory.Name;
        Location = directory.Location;
        Badge = LocationIcons.ForBadge(directory.Kind);
        OpenCommand = new RelayCommand(() => navigator.Navigate(Location));
    }

    public string Name { get; }

    public string Location { get; }

    public MaterialIconKind Badge { get; }

    public IRelayCommand OpenCommand { get; }
}

public sealed class DiskCardViewModel
{
    private const double AlmostFullPercent = 80;

    public DiskCardViewModel(Volume volume, INavigator navigator)
    {
        Name = volume.Name;
        MountPoint = volume.MountPoint;
        Kind = volume.Kind;
        UsedPercent = volume.TotalBytes > 0 ? volume.UsedBytes * 100.0 / volume.TotalBytes : 0;
        UsageText = $"{SizeFormatter.Format(volume.UsedBytes)}/{SizeFormatter.Format(volume.TotalBytes)}";
        OpenCommand = new RelayCommand(() => navigator.Navigate(MountPoint));
    }

    public string Name { get; }

    public string MountPoint { get; }

    public VolumeKind Kind { get; }

    public double UsedPercent { get; }

    public string UsageText { get; }

    public IRelayCommand OpenCommand { get; }

    public bool IsOptical => Kind == VolumeKind.Optical;

    public bool IsRemovable => Kind == VolumeKind.Removable;

    /// <summary>Switches the usage bar to the orange/yellow gradient.</summary>
    public bool IsAlmostFull => !IsOptical && UsedPercent >= AlmostFullPercent;

    public bool HasGlyph => Kind is VolumeKind.System or VolumeKind.Removable;

    public MaterialIconKind Glyph => Kind == VolumeKind.Removable ? MaterialIconKind.Eject : MaterialIconKind.Linux;
}
