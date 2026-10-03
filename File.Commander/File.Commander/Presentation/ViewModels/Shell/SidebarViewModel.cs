using System.Collections.ObjectModel;
using File.Commander.Presentation.Services;
using File.Commander.Presentation.ViewModels.Helpers;
using Material.Icons;

namespace File.Commander.Presentation.ViewModels.Shell;

public partial class SidebarViewModel : ViewModelBase
{
    private readonly IReadOnlyList<UserDirectory> _directories;
    private readonly IReadOnlyList<Volume> _volumes;
    private SidebarEntry? _selectedEntry;

    public SidebarViewModel(IReadOnlyList<UserDirectory> directories, IReadOnlyList<Volume> volumes,
        SidebarSettings visibility)
    {
        _directories = directories;
        _volumes = volumes;
        Entries = new ObservableCollection<SidebarEntry>(Build(visibility));
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
    /// Shows only the entries <paramref name="visibility"/> allows. Clears the highlight:
    /// call <see cref="Select"/> afterwards.
    /// </summary>
    public void Apply(SidebarSettings visibility)
    {
        Entries.Clear();
        foreach (var entry in Build(visibility))
            Entries.Add(entry);

        _selectedEntry = null;
        OnPropertyChanged(nameof(SelectedEntry));
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

    // A header is shown only when something is left under it
    private List<SidebarEntry> Build(SidebarSettings visibility)
    {
        var quickAccess = new List<SidebarEntry>();
        if (visibility.ShowRecent)
            quickAccess.Add(new SidebarItem("Recent", MaterialIconKind.ClockOutline, Locations.Recent));
        if (visibility.ShowHome)
            quickAccess.Add(new SidebarItem("Home", MaterialIconKind.HomeOutline, SystemLocations.HomeDirectory));
        if (visibility.ShowUserFolders)
            quickAccess.AddRange(_directories.Select(ToItem));
        if (visibility.ShowTrash)
            quickAccess.Add(new SidebarItem("Trash", MaterialIconKind.TrashCanOutline, Locations.Trash));

        var partitions = new List<SidebarEntry>();
        if (visibility.ShowComputer)
            partitions.Add(new SidebarItem("Computer", MaterialIconKind.Monitor, Locations.Computer));
        if (visibility.ShowPartitions)
        {
            partitions.AddRange(_volumes.Select(v => new SidebarItem(
                v.Name,
                LocationIcons.ForSidebar(v.Kind),
                v.MountPoint,
                canEject: v.Kind is VolumeKind.Removable or VolumeKind.Optical)));
        }

        var entries = new List<SidebarEntry>();
        AddSection(entries, "Quick access", quickAccess);
        AddSection(entries, "Partitions", partitions);
        if (visibility.ShowNetwork)
            AddSection(entries, "Network", [new SidebarItem("Network", MaterialIconKind.LanConnect, Locations.Network)]);

        return entries;
    }

    private static void AddSection(List<SidebarEntry> entries, string title, List<SidebarEntry> items)
    {
        if (items.Count == 0)
            return;

        entries.Add(new SidebarHeader(title));
        entries.AddRange(items);
    }

    private static SidebarItem ToItem(UserDirectory directory) =>
        new(directory.Name, LocationIcons.ForSidebar(directory.Kind), directory.Location);
}
