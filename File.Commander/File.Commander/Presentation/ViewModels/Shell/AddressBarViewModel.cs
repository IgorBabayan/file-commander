using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using File.Commander.Presentation.Services;
using File.Commander.Presentation.ViewModels.Helpers;
using File.Commander.Presentation.ViewModels.Pages;
using Material.Icons;

namespace File.Commander.Presentation.ViewModels.Shell;

/// <summary>
/// Explorer-style address bar: clickable breadcrumbs with a folder drop-down after each one,
/// and an editable path box when you click the empty part of the bar.
/// </summary>
public sealed partial class AddressBarViewModel(INavigator navigator) : ViewModelBase
{
    private static readonly string[] VirtualLocations =
        [Locations.Computer, Locations.Recent, Locations.Trash, Locations.Network];

    private string _location = Locations.Computer;

    /// <summary>Used to pick the breadcrumb root. Refreshed by the shell whenever the Computer page is built.</summary>
    internal IReadOnlyList<Volume> Volumes { get; set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<AddressSegmentViewModel> Segments { get; set; } = [];

    [ObservableProperty]
    public partial bool IsEditing { get; set; }

    [ObservableProperty]
    public partial string EditText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEditError))]
    public partial string? EditError { get; set; }

    public bool HasEditError => EditError is not null;

    partial void OnEditTextChanged(string value) => EditError = null;

    /// <summary>Called by the shell after every navigation.</summary>
    public void Update(string location)
    {
        _location = location;
        IsEditing = false;
        EditError = null;
        Segments = Build(location);
    }

    [RelayCommand]
    public void BeginEdit()
    {
        EditText = _location;
        EditError = null;
        IsEditing = true;
    }

    [RelayCommand]
    public void CancelEdit()
    {
        IsEditing = false;
        EditError = null;
    }

    [RelayCommand]
    public void CommitEdit()
    {
        if (Resolve(EditText) is not { } target)
        {
            EditError = $"Can't find \"{EditText.Trim()}\". Check the path and try again.";
            return;
        }

        IsEditing = false;
        navigator.Navigate(target);
    }

    /// <summary>Accepts absolute paths, ~, paths relative to the current folder and virtual locations.</summary>
    private string? Resolve(string input)
    {
        var text = input.Trim();
        if (text.Length == 0)
            return null;

        if (Locations.IsVirtual(text))
            return VirtualLocations.FirstOrDefault(v => string.Equals(v, text, StringComparison.OrdinalIgnoreCase));

        var home = SystemLocations.HomeDirectory;
        if (text == "~")
            text = home;
        else if (text.StartsWith("~/", StringComparison.Ordinal))
            text = home + text[1..];

        try
        {
            var basePath = Locations.IsVirtual(_location) ? home : _location;
            var full = IOPath.GetFullPath(text, basePath);
            return Directory.Exists(full) ? full : null;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException)
        {
            return null;
        }
    }

    private IReadOnlyList<AddressSegmentViewModel> Build(string location)
    {
        switch (location)
        {
            case Locations.Recent:
                return Finish([Segment("Recent", location, MaterialIconKind.ClockOutline, null)]);
            case Locations.Trash:
                return Finish([Segment("Trash", location, MaterialIconKind.TrashCanOutline, null)]);
            case Locations.Network:
                return Finish([Segment("Network", location, MaterialIconKind.LanConnect, null)]);
        }

        var segments = new List<AddressSegmentViewModel>
        {
            Segment("Computer", Locations.Computer, MaterialIconKind.Monitor, ListComputer)
        };

        if (location == Locations.Computer)
            return Finish(segments);

        var (rootName, rootPath, rootIcon) = FindRoot(location);
        segments.Add(Segment(rootName, rootPath, rootIcon, () => ListSubdirectories(rootPath)));

        var relative = IOPath.GetRelativePath(rootPath, location);
        if (relative != ".")
        {
            var current = rootPath;
            foreach (var part in relative.Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                current = IOPath.Combine(current, part);
                var path = current;
                segments.Add(Segment(part, path, null, () => ListSubdirectories(path)));
            }
        }

        return Finish(segments);
    }

    private AddressSegmentViewModel Segment(string name, string location, MaterialIconKind? icon,
        Func<IReadOnlyList<AddressTarget>>? listChildren)
        => new(name, location, icon, navigator, listChildren);

    private static IReadOnlyList<AddressSegmentViewModel> Finish(List<AddressSegmentViewModel> segments)
    {
        // Each drop-down highlights the folder that comes next in the path, like Explorer does
        for (var i = 0; i < segments.Count - 1; i++)
            segments[i].ActiveChildLocation = segments[i + 1].Location;

        segments[^1].IsCurrent = true;
        return segments;
    }

    /// <summary>Home wins over the volume it lives on, so ~/Documents reads "Computer › Home › Documents".</summary>
    private (string Name, string Path, MaterialIconKind Icon) FindRoot(string location)
    {
        var home = SystemLocations.HomeDirectory;
        if (IsUnder(location, home))
            return ("Home", Locations.Normalize(home), MaterialIconKind.HomeOutline);

        var volume = Volumes
            .Where(v => IsUnder(location, v.MountPoint))
            .MaxBy(v => v.MountPoint.Length);

        return volume is not null
            ? (volume.Name, Locations.Normalize(volume.MountPoint), LocationIcons.ForSidebar(volume.Kind))
            : ("/", "/", MaterialIconKind.Harddisk);
    }

    private IReadOnlyList<AddressTarget> ListComputer()
    {
        var targets = new List<AddressTarget> { new("Home", SystemLocations.HomeDirectory, MaterialIconKind.HomeOutline) };
        targets.AddRange(Volumes.Select(v => new AddressTarget(v.Name, v.MountPoint, LocationIcons.ForSidebar(v.Kind))));
        return targets;
    }

    private static IReadOnlyList<AddressTarget> ListSubdirectories(string path)
    {
        // Default options: hidden folders skipped, unreadable folders give an empty list instead of throwing
        return new DirectoryInfo(path)
            .EnumerateDirectories("*", new EnumerationOptions())
            .Select(d => new AddressTarget(d.Name, d.FullName, MaterialIconKind.FolderOutline))
            .OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static bool IsUnder(string path, string root)
    {
        path = Locations.Normalize(path);
        root = Locations.Normalize(root);
        return root == "/"
            ? path.StartsWith('/')
            : path == root || path.StartsWith(root + "/", StringComparison.Ordinal);
    }
}

public readonly record struct AddressTarget(string Name, string Location, MaterialIconKind Icon);

/// <summary>One breadcrumb plus its drop-down of child folders.</summary>
public sealed partial class AddressSegmentViewModel : ObservableObject
{
    private readonly INavigator _navigator;
    private readonly Func<IReadOnlyList<AddressTarget>>? _listChildren;

    public AddressSegmentViewModel(string name, string location, MaterialIconKind? icon,
        INavigator navigator, Func<IReadOnlyList<AddressTarget>>? listChildren)
    {
        Name = name;
        Location = location;
        Icon = icon ?? MaterialIconKind.Folder;
        HasIcon = icon is not null;
        _navigator = navigator;
        _listChildren = listChildren;
    }

    public string Name { get; }

    public string Location { get; }

    public MaterialIconKind Icon { get; }

    public bool HasIcon { get; }

    public bool HasDropDown => _listChildren is not null;

    /// <summary>The last breadcrumb: the folder you're in.</summary>
    public bool IsCurrent { get; internal set; }

    /// <summary>The next breadcrumb's location, shown in bold in the drop-down.</summary>
    public string? ActiveChildLocation { get; internal set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDropDownEmpty))]
    public partial IReadOnlyList<AddressChildViewModel> Children { get; set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDropDownEmpty))]
    public partial bool IsLoadingChildren { get; set; }

    [ObservableProperty]
    public partial bool IsDropDownOpen { get; set; }

    public bool IsDropDownEmpty => !IsLoadingChildren && Children.Count == 0;

    // Read on every opening, so the list is never stale
    partial void OnIsDropDownOpenChanged(bool value)
    {
        if (value)
            _ = LoadChildrenAsync();
    }

    [RelayCommand]
    private void Navigate() => _navigator.Navigate(Location);

    private async Task LoadChildrenAsync()
    {
        if (_listChildren is null)
            return;

        IsLoadingChildren = true;
        try
        {
            var targets = await Task.Run(_listChildren);
            Children = targets
                .Select(t => new AddressChildViewModel(t,
                    ActiveChildLocation is { } active && Locations.AreEqual(active, t.Location), Open))
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.WriteLine($"Can't list '{Location}': {ex.Message}");
            Children = [];
        }
        finally
        {
            IsLoadingChildren = false;
        }
    }

    private void Open(string location)
    {
        IsDropDownOpen = false;
        _navigator.Navigate(location);
    }
}

public sealed class AddressChildViewModel(AddressTarget target, bool isActive, Action<string> open)
{
    public string Name => target.Name;

    public string Location => target.Location;

    public MaterialIconKind Icon => target.Icon;

    /// <summary>The folder that comes next in the current path.</summary>
    public bool IsActive { get; } = isActive;

    public IRelayCommand OpenCommand { get; } = new RelayCommand(() => open(target.Location));
}
