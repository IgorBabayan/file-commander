using System.Collections.ObjectModel;
using File.Commander.Presentation.Services;
using File.Commander.Presentation.ViewModels.Helpers;
using Material.Icons;

namespace File.Commander.Presentation.ViewModels.Shell;

public partial class SidebarViewModel : ViewModelBase
{
    private const string COMPUTER_LOCATION = "computer://";
    private const string RECENT_LOCATION = "recent://";
    private const string TRASH_LOCATION = "trash://";
    private const string NETWORK_LOCATION = "network://";
 
    private SidebarEntry? _selectedEntry;
 
    public SidebarViewModel(IReadOnlyList<UserDirectory> directories, IReadOnlyList<Volume> volumes)
    {
        var computer = new SidebarItem("Computer", MaterialIconKind.Monitor, COMPUTER_LOCATION);
 
        var entries = new List<SidebarEntry>
        {
            new SidebarHeader("Quick access"),
            new SidebarItem("Recent", MaterialIconKind.ClockOutline, RECENT_LOCATION),
            new SidebarItem("Home", MaterialIconKind.HomeOutline, SystemLocations.HomeDirectory),
        };
 
        entries.AddRange(directories
            .Where(d => d.Kind != UserDirectoryKind.Downloads)
            .Select(ToItem));
 
        entries.Add(new SidebarItem("Trash", MaterialIconKind.TrashCanOutline, TRASH_LOCATION));
 
        entries.AddRange(directories
            .Where(d => d.Kind == UserDirectoryKind.Downloads)
            .Select(ToItem));
 
        entries.Add(new SidebarHeader("Partitions"));
        entries.Add(computer);
        entries.AddRange(volumes.Select(v => new SidebarItem(
            v.Name,
            LocationIcons.ForSidebar(v.Kind),
            v.MountPoint,
            canEject: v.Kind is VolumeKind.Removable or VolumeKind.Optical)));
 
        entries.Add(new SidebarHeader("Network"));
        entries.Add(new SidebarItem("Network", MaterialIconKind.LanConnect, NETWORK_LOCATION));
 
        Entries = new ObservableCollection<SidebarEntry>(entries);
        _selectedEntry = computer;
    }
 
    public ObservableCollection<SidebarEntry> Entries { get; }
 
    public SidebarEntry? SelectedEntry
    {
        get => _selectedEntry;
        set
        {
            if (value is { IsSelectable: false } || ReferenceEquals(value, _selectedEntry))
                return;
 
            _selectedEntry = value;
            OnPropertyChanged();
        }
    }
 
    private static SidebarItem ToItem(UserDirectory directory) =>
        new(directory.Name, LocationIcons.ForSidebar(directory.Kind), directory.Location);
}