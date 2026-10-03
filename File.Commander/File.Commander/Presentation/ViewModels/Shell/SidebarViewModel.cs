using System.Collections.ObjectModel;
using File.Commander.Presentation.Services;
using File.Commander.Presentation.ViewModels.Helpers;
using Material.Icons;

namespace File.Commander.Presentation.ViewModels.Shell;

public partial class SidebarViewModel : ViewModelBase
{
    private SidebarEntry? _selectedEntry;

    public SidebarViewModel(IReadOnlyList<UserDirectory> directories, IReadOnlyList<Volume> volumes)
    {
        var entries = new List<SidebarEntry>
        {
            new SidebarHeader("Quick access"),
            new SidebarItem("Recent", MaterialIconKind.ClockOutline, Locations.Recent),
            new SidebarItem("Home", MaterialIconKind.HomeOutline, SystemLocations.HomeDirectory),
        };

        entries.AddRange(directories.Select(ToItem));
        entries.Add(new SidebarItem("Trash", MaterialIconKind.TrashCanOutline, Locations.Trash));

        entries.Add(new SidebarHeader("Partitions"));
        entries.Add(new SidebarItem("Computer", MaterialIconKind.Monitor, Locations.Computer));
        entries.AddRange(volumes.Select(v => new SidebarItem(
            v.Name,
            LocationIcons.ForSidebar(v.Kind),
            v.MountPoint,
            canEject: v.Kind is VolumeKind.Removable or VolumeKind.Optical)));

        entries.Add(new SidebarHeader("Network"));
        entries.Add(new SidebarItem("Network", MaterialIconKind.LanConnect, Locations.Network));

        Entries = new ObservableCollection<SidebarEntry>(entries);
    }

    /// <summary>The user picked an item. The argument is its location.</summary>
    public event EventHandler<string>? NavigationRequested;

    public ObservableCollection<SidebarEntry> Entries { get; }

    /// <summary>Bound to the ListBox. Setting it (i.e. a click or arrow key) navigates.</summary>
    public SidebarEntry? SelectedEntry
    {
        get => _selectedEntry;
        set
        {
            if (value is { IsSelectable: false } || ReferenceEquals(value, _selectedEntry))
                return;

            _selectedEntry = value;
            OnPropertyChanged();

            if (value is SidebarItem item)
                NavigationRequested?.Invoke(this, item.Location);
        }
    }

    /// <summary>
    /// Highlights the item for <paramref name="location"/>, or nothing if no item matches
    /// (e.g. a subfolder). Doesn't raise <see cref="NavigationRequested"/>.
    /// </summary>
    public void Select(string location)
    {
        var item = Entries.OfType<SidebarItem>().FirstOrDefault(i => Locations.AreEqual(i.Location, location));
        if (ReferenceEquals(item, _selectedEntry))
            return;

        _selectedEntry = item;
        OnPropertyChanged(nameof(SelectedEntry));
    }

    private static SidebarItem ToItem(UserDirectory directory) =>
        new(directory.Name, LocationIcons.ForSidebar(directory.Kind), directory.Location);
}
