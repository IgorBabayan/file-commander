using System.Collections.ObjectModel;
using Material.Icons;

namespace File.Commander.Presentation.ViewModels.Shell;

public partial class SidebarViewModel : ViewModelBase
{
    private readonly IReadOnlyList<UserDirectory> _directories;
    private readonly IReadOnlyList<Volume> _volumes;
    private SidebarEntry? _selectedEntry;

    // The stored order (SidebarSettings.ItemOrder); includes items that are hidden now
    private IReadOnlyList<string> _order;

    // The stored favorites (SidebarSettings.Favorites), in the order they were added
    private IReadOnlyList<string> _favorites;

    // Names given with Rename (SidebarSettings.FavoriteNames), by location
    private IReadOnlyDictionary<string, string> _favoriteNames;

    // What Build shows: kept so the entries can be rebuilt when a favorite is added or removed
    private SidebarSettings _visibility;

    // Entries.Move or a rebuild may make the ListBox write a different (or no) selection: not a click
    private bool _moving;

    public SidebarViewModel(IReadOnlyList<UserDirectory> directories, IReadOnlyList<Volume> volumes,
        SidebarSettings visibility)
    {
        _directories = directories;
        _volumes = volumes;
        _visibility = visibility;
        _order = visibility.ItemOrder ?? [];
        _favorites = visibility.Favorites ?? [];
        _favoriteNames = visibility.FavoriteNames ?? new Dictionary<string, string>();
        Entries = new ObservableCollection<SidebarEntry>(Build(visibility));
    }

    /// <summary>The user picked an item. The argument is its location.</summary>
    public event EventHandler<string>? NavigationRequested;

    /// <summary>
    /// A drag ended with a new order. The argument is the new <see cref="SidebarSettings.ItemOrder"/>,
    /// already shown: store it.
    /// </summary>
    public event EventHandler<IReadOnlyList<string>>? OrderChanged;

    /// <summary>
    /// A folder was added to or removed from "Favorites". The argument is the new
    /// <see cref="SidebarSettings.Favorites"/>, already shown: store it.
    /// </summary>
    public event EventHandler<IReadOnlyList<string>>? FavoritesChanged;

    public ObservableCollection<SidebarEntry> Entries { get; }

    /// <summary>
    /// The current <see cref="SidebarSettings.FavoriteNames"/>: read it when <see cref="FavoritesChanged"/> is raised,
    /// so a rename is stored with the list. A new instance after every change.
    /// </summary>
    public IReadOnlyDictionary<string, string> FavoriteNames => _favoriteNames;

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
        _visibility = visibility;
        _order = visibility.ItemOrder ?? [];
        _favorites = visibility.Favorites ?? [];
        _favoriteNames = visibility.FavoriteNames ?? new Dictionary<string, string>();

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
        // A favorite can also be in Quick access: keep the one that was clicked
        if (_selectedEntry is SidebarItem current && Locations.AreEqual(current.Location, location))
            return;

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

    /// <summary>True when dropping <paramref name="paths"/> would add at least one folder to "Favorites".</summary>
    public bool CanAddFavorites(IEnumerable<string> paths) => NewFavorites(paths).Any();

    /// <summary>
    /// Adds the folders among <paramref name="paths"/> that aren't favorites yet to "Favorites" (the section
    /// appears with the first one) and raises <see cref="FavoritesChanged"/>. Files and missing paths are skipped.
    /// Keeps the highlight, doesn't navigate.
    /// </summary>
    /// <returns>False when nothing was added.</returns>
    public bool AddFavorites(IEnumerable<string> paths)
    {
        var added = NewFavorites(paths).ToList();
        if (added.Count == 0)
            return false;

        UpdateFavorites([.. _favorites, .. added]);
        return true;
    }

    /// <summary>Takes <paramref name="item"/> off "Favorites"; the section goes away with the last one.</summary>
    /// <returns>False when <paramref name="item"/> isn't a favorite.</returns>
    public bool RemoveFavorite(SidebarItem item)
    {
        if (!item.IsFavorite)
            return false;

        var favorites = _favorites.Where(f => !Locations.AreEqual(f, item.Location)).ToList();
        if (favorites.Count == _favorites.Count)
            return false;

        // Added again later, it starts with its folder's name
        _favoriteNames = WithName(item.Location, null);
        UpdateFavorites(favorites);
        return true;
    }

    /// <summary>
    /// Gives <paramref name="item"/> the name <paramref name="name"/> on the sidebar; the folder itself isn't renamed.
    /// An empty name, or the folder's own, goes back to the folder's name. Raises <see cref="FavoritesChanged"/>.
    /// </summary>
    /// <returns>False when <paramref name="item"/> isn't a favorite or the name didn't change.</returns>
    public bool RenameFavorite(SidebarItem item, string? name)
    {
        if (!item.IsFavorite)
            return false;

        var trimmed = name?.Trim() ?? string.Empty;
        var custom = trimmed.Length == 0 || trimmed == FolderName(item.Location) ? null : trimmed;
        if (custom == (_favoriteNames.TryGetValue(item.Location, out var stored) ? stored : null))
            return false;

        _favoriteNames = WithName(item.Location, custom);
        UpdateFavorites(_favorites);
        return true;
    }

    /// <summary>Which context menu <paramref name="item"/> gets. A favorite is a favorite wherever it points.</summary>
    public static SidebarItemKind KindOf(SidebarItem item)
    {
        if (item.IsFavorite)
            return SidebarItemKind.Favorite;

        if (Locations.AreEqual(item.Location, Locations.Recent))
            return SidebarItemKind.Recent;

        return Locations.AreEqual(item.Location, Locations.Trash) ? SidebarItemKind.Trash : SidebarItemKind.Regular;
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

    // Existing folders that aren't favorites yet, normalized, each once
    private IEnumerable<string> NewFavorites(IEnumerable<string> paths)
    {
        var seen = new List<string>();
        foreach (var path in paths)
        {
            if (Locations.IsVirtual(path))
                continue;

            var location = Locations.Normalize(path);
            if (_favorites.Any(f => Locations.AreEqual(f, location))
                || seen.Contains(location, StringComparer.Ordinal)
                || !Directory.Exists(location))
                continue;

            seen.Add(location);
            yield return location;
        }
    }

    private void UpdateFavorites(IReadOnlyList<string> favorites)
    {
        _favorites = favorites;
        _visibility = _visibility with { Favorites = favorites };
        Rebuild();
        FavoritesChanged?.Invoke(this, favorites);
    }

    /// <summary>Shows the entries again (a section came or went), keeping the highlight. Doesn't navigate.</summary>
    private void Rebuild()
    {
        var selected = (_selectedEntry as SidebarItem)?.Location;

        _moving = true;
        try
        {
            Entries.Clear();
            foreach (var entry in Build(_visibility))
                Entries.Add(entry);
        }
        finally
        {
            _moving = false;
        }

        _selectedEntry = selected is null
            ? null
            : Entries.OfType<SidebarItem>().FirstOrDefault(i => Locations.AreEqual(i.Location, selected));
        OnPropertyChanged(nameof(SelectedEntry));
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

        var favorites = _favorites.Select(ToFavorite).ToList();

        var entries = new List<SidebarEntry>();
        AddSection(entries, "Favorites", favorites);
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

    private SidebarItem ToFavorite(string location)
    {
        var name = _favoriteNames.TryGetValue(location, out var custom) ? custom : FolderName(location);
        return new SidebarItem(name, MaterialIconKind.FolderStarOutline, location, isFavorite: true);
    }

    // "/" for the root
    private static string FolderName(string location)
    {
        var name = IOPath.GetFileName(location);
        return name.Length == 0 ? location : name;
    }

    // A copy of the names with location's set (or removed, for null)
    private IReadOnlyDictionary<string, string> WithName(string location, string? name)
    {
        var names = new Dictionary<string, string>(_favoriteNames, StringComparer.Ordinal);
        if (name is null)
            names.Remove(location);
        else
            names[location] = name;

        return names;
    }

    private static SidebarItem ToItem(UserDirectory directory) =>
        new(directory.Name, LocationIcons.ForSidebar(directory.Kind), directory.Location);
}
