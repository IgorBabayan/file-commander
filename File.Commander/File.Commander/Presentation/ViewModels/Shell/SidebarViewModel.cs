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

    // The stored order (SidebarSettings.ItemOrder); includes items that are hidden now
    private IReadOnlyList<string> _order;

    // Entries.Move may make the ListBox write a different (or no) selection: not a click
    private bool _moving;

    public SidebarViewModel(IReadOnlyList<UserDirectory> directories, IReadOnlyList<Volume> volumes,
        SidebarSettings visibility)
    {
        _directories = directories;
        _volumes = volumes;
        _order = visibility.ItemOrder ?? [];
        Entries = new ObservableCollection<SidebarEntry>(Build(visibility));
    }

    /// <summary>The user picked an item. The argument is its location.</summary>
    public event EventHandler<string>? NavigationRequested;

    /// <summary>
    /// A drag ended with a new order. The argument is the new <see cref="SidebarSettings.ItemOrder"/>,
    /// already shown: store it.
    /// </summary>
    public event EventHandler<IReadOnlyList<string>>? OrderChanged;

    public ObservableCollection<SidebarEntry> Entries { get; }

    /// <summary>Bound to the ListBox. Setting it (i.e. a click or arrow key) navigates.</summary>
    public SidebarEntry? SelectedEntry
    {
        get => _selectedEntry;
        set
        {
            if (_moving || value is { IsSelectable: false } || ReferenceEquals(value, _selectedEntry))
                return;

            _selectedEntry = value;
            OnPropertyChanged();

            if (value is SidebarItem item)
                NavigationRequested?.Invoke(this, item.Location);
        }
    }

    /// <summary>
    /// Shows only the entries <paramref name="visibility"/> allows, in its order. Clears the highlight:
    /// call <see cref="Select"/> afterwards.
    /// </summary>
    public void Apply(SidebarSettings visibility)
    {
        _order = visibility.ItemOrder ?? [];

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

    /// <summary>
    /// While dragging: puts <paramref name="item"/> where <paramref name="target"/> is.
    /// Items only move inside their own section. Keeps the highlight, doesn't navigate.
    /// </summary>
    /// <returns>False when nothing moved.</returns>
    public bool MoveTo(SidebarItem item, SidebarItem target)
    {
        if (ReferenceEquals(item, target))
            return false;

        var from = Entries.IndexOf(item);
        var to = Entries.IndexOf(target);
        if (from < 0 || to < 0 || !ReferenceEquals(SectionOf(from), SectionOf(to)))
            return false;

        var selected = _selectedEntry;
        _moving = true;
        try
        {
            Entries.Move(from, to);
        }
        finally
        {
            _moving = false;
        }

        // Puts the ListBox selection back on the highlighted item, whatever it did during the move
        _selectedEntry = selected;
        OnPropertyChanged(nameof(SelectedEntry));
        return true;
    }

    /// <summary>A drag ended: raises <see cref="OrderChanged"/> if the order differs from the stored one.</summary>
    public void CommitOrder()
    {
        var shown = Entries.OfType<SidebarItem>().Select(i => i.Location).ToList();
        var order = Merge(_order, shown);
        if (order.SequenceEqual(_order, StringComparer.Ordinal))
            return;

        _order = order;
        OrderChanged?.Invoke(this, order);
    }

    // The header the entry at index is under
    private SidebarHeader? SectionOf(int index)
    {
        for (var i = index; i >= 0; i--)
        {
            if (Entries[i] is SidebarHeader header)
                return header;
        }

        return null;
    }

    /// <summary>
    /// <paramref name="shown"/> in its order, plus the stored items that are hidden now (switched
    /// off in Settings, a drive that's unplugged), each kept right after the item it followed.
    /// </summary>
    private static List<string> Merge(IReadOnlyList<string> stored, List<string> shown)
    {
        var result = new List<string>(shown);
        var visible = new HashSet<string>(shown, StringComparer.Ordinal);
        string? previous = null;

        foreach (var location in stored)
        {
            if (!visible.Contains(location) && !result.Contains(location, StringComparer.Ordinal))
            {
                var at = previous is null ? 0 : result.IndexOf(previous) + 1;
                result.Insert(at, location);
            }

            previous = location;
        }

        return result;
    }

    // A header is shown only when something is left under it
    private List<SidebarEntry> Build(SidebarSettings visibility)
    {
        var quickAccess = new List<SidebarItem>();
        if (visibility.ShowRecent)
            quickAccess.Add(new SidebarItem("Recent", MaterialIconKind.ClockOutline, Locations.Recent));
        if (visibility.ShowHome)
            quickAccess.Add(new SidebarItem("Home", MaterialIconKind.HomeOutline, SystemLocations.HomeDirectory));
        if (visibility.ShowUserFolders)
            quickAccess.AddRange(_directories.Select(ToItem));
        if (visibility.ShowTrash)
            quickAccess.Add(new SidebarItem("Trash", MaterialIconKind.TrashCanOutline, Locations.Trash));

        var partitions = new List<SidebarItem>();
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

    private void AddSection(List<SidebarEntry> entries, string title, List<SidebarItem> items)
    {
        if (items.Count == 0)
            return;

        entries.Add(new SidebarHeader(title));
        entries.AddRange(Arrange(items));
    }

    // Stored order first; unknown items keep their built-in order after them (OrderBy is stable)
    private IEnumerable<SidebarItem> Arrange(List<SidebarItem> items)
    {
        if (_order.Count == 0)
            return items;

        var rank = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < _order.Count; i++)
            rank.TryAdd(_order[i], i);

        return items.OrderBy(item => rank.GetValueOrDefault(item.Location, int.MaxValue));
    }

    private static SidebarItem ToItem(UserDirectory directory) =>
        new(directory.Name, LocationIcons.ForSidebar(directory.Kind), directory.Location);
}
