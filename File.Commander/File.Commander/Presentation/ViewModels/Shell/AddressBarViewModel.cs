using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

    private const int MaxSuggestions = 50;

    private string _location = Locations.Computer;
    private CancellationTokenSource? _suggestCts;
    private bool _suppressSuggestions;
    private int _selectedIndex = -1;

    /// <summary>
    /// A server address was typed (smb://nas/media, sftp://me@server…): the shell connects to it on the Network page.
    /// </summary>
    public event EventHandler<string>? ConnectRequested;

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

    /// <summary>Folders matching what's typed, e.g. "~/.lo" → "~/.local".</summary>
    [ObservableProperty]
    public partial IReadOnlyList<AddressSuggestionViewModel> Suggestions { get; set; } = [];

    [ObservableProperty]
    public partial bool IsSuggestionsOpen { get; set; }

    private AddressSuggestionViewModel? SelectedSuggestion =>
        _selectedIndex >= 0 && _selectedIndex < Suggestions.Count ? Suggestions[_selectedIndex] : null;

    partial void OnEditTextChanged(string value)
    {
        EditError = null;

        // Only typing suggests: filling the box with the current path on BeginEdit doesn't
        if (!_suppressSuggestions && IsEditing)
            _ = UpdateSuggestionsAsync(value);
    }

    /// <summary>Called by the shell after every navigation.</summary>
    public void Update(string location)
    {
        _location = location;
        IsEditing = false;
        EditError = null;
        CloseSuggestions();
        Segments = Build(location);
    }

    [RelayCommand]
    public void BeginEdit()
    {
        _suppressSuggestions = true;

        // Search results: the folder that was searched, not the search://… address
        EditText = SearchQuery.TryParse(_location)?.Folder ?? _location;
        _suppressSuggestions = false;

        EditError = null;
        CloseSuggestions();
        IsEditing = true;
    }

    [RelayCommand]
    public void CancelEdit()
    {
        IsEditing = false;
        EditError = null;
        CloseSuggestions();
    }

    [RelayCommand]
    public void CommitEdit()
    {
        if (NetworkLocations.IsRemoteUri(EditText))
        {
            IsEditing = false;
            CloseSuggestions();
            ConnectRequested?.Invoke(this, EditText.Trim());
            return;
        }

        if (Resolve(EditText) is not { } target)
        {
            EditError = $"Can't find \"{EditText.Trim()}\". Check the path and try again.";
            return;
        }

        IsEditing = false;
        CloseSuggestions();
        navigator.Navigate(target);
    }

    /// <summary>Up/Down. Index -1 means "back in the text box", like Explorer. Returns the new index.</summary>
    public int MoveSelection(int delta)
    {
        if (Suggestions.Count == 0)
            return -1;

        var next = _selectedIndex + delta;
        if (next < -1)
            next = Suggestions.Count - 1;
        else if (next >= Suggestions.Count)
            next = -1;

        if (SelectedSuggestion is { } previous)
            previous.IsSelected = false;

        _selectedIndex = next;

        if (SelectedSuggestion is { } current)
            current.IsSelected = true;

        return next;
    }

    /// <summary>
    /// Tab: puts the selected (or first) suggestion into the box with a trailing "/",
    /// so the next level is suggested right away. False if there is nothing to complete.
    /// </summary>
    public bool CompleteSuggestion()
    {
        if (!IsSuggestionsOpen || (SelectedSuggestion ?? Suggestions.FirstOrDefault()) is not { } suggestion)
            return false;

        EditText = suggestion.Text + "/";
        return true;
    }

    /// <summary>Enter: opens the suggestion picked with the arrows. False if none is picked.</summary>
    public bool OpenSelectedSuggestion()
    {
        if (!IsSuggestionsOpen || SelectedSuggestion is not { } suggestion)
            return false;

        OpenSuggestion(suggestion);
        return true;
    }

    public void CloseSuggestions()
    {
        _suggestCts?.Cancel();
        _suggestCts = null;
        _selectedIndex = -1;
        IsSuggestionsOpen = false;
        Suggestions = [];
    }

    protected override void OnDispose()
    {
        _suggestCts?.Cancel();
        base.OnDispose();
    }

    private void OpenSuggestion(AddressSuggestionViewModel suggestion)
    {
        IsEditing = false;
        CloseSuggestions();
        navigator.Navigate(suggestion.FullPath);
    }

    private async Task UpdateSuggestionsAsync(string text)
    {
        _suggestCts?.Cancel();
        var cts = _suggestCts = new CancellationTokenSource();
        var token = cts.Token;

        // "~/.lo" → folder "~/" (typed as is, kept in the suggestion text) + name prefix ".lo"
        var slash = text.LastIndexOf('/');
        var typedFolder = slash < 0 ? string.Empty : text[..(slash + 1)];
        var prefix = text[(slash + 1)..];

        if (Locations.IsVirtual(text) || (typedFolder.Length == 0 && prefix.Length == 0)
            || TryGetFullPath(typedFolder.Length == 0 ? "." : typedFolder) is not { } folder)
        {
            CloseSuggestions();
            return;
        }

        try
        {
            // Short pause: a fast typist doesn't trigger a directory read per key
            await Task.Delay(60, token);
            var names = await Task.Run(() => FindFolders(folder, prefix, token), token);

            if (token.IsCancellationRequested || !IsEditing)
                return;

            _selectedIndex = -1;
            Suggestions = names
                .Select(name => new AddressSuggestionViewModel(
                    typedFolder + name, IOPath.Combine(folder, name), OpenSuggestion))
                .ToList();
            IsSuggestionsOpen = Suggestions.Count > 0;
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static IReadOnlyList<string> FindFolders(string folder, string prefix, CancellationToken token)
    {
        var options = new EnumerationOptions
        {
            // Dot folders only when asked for: "~/." lists them, "~/" doesn't
            AttributesToSkip = prefix.StartsWith('.') ? 0 : FileAttributes.Hidden | FileAttributes.System,
        };

        try
        {
            var result = new List<string>();
            foreach (var directory in new DirectoryInfo(folder).EnumerateDirectories("*", options))
            {
                token.ThrowIfCancellationRequested();
                if (directory.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    result.Add(directory.Name);
            }

            // Exact-case matches first ("Doc" → Documents before docs), then alphabetical
            return result
                .OrderByDescending(name => name.StartsWith(prefix, StringComparison.Ordinal))
                .ThenBy(name => name, StringComparer.CurrentCultureIgnoreCase)
                .Take(MaxSuggestions)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return [];
        }
    }

    /// <summary>Accepts absolute paths, ~, paths relative to the current folder and virtual locations.</summary>
    private string? Resolve(string input)
    {
        var text = input.Trim();
        if (text.Length == 0)
            return null;

        if (Locations.IsVirtual(text))
            return VirtualLocations.FirstOrDefault(v => string.Equals(v, text, StringComparison.OrdinalIgnoreCase));

        return TryGetFullPath(text) is { } full && Directory.Exists(full) ? full : null;
    }

    /// <summary>Expands ~ and resolves relative paths against the current folder (home on virtual pages).</summary>
    private string? TryGetFullPath(string text)
    {
        var home = SystemLocations.HomeDirectory;
        if (text == "~")
            text = home;
        else if (text.StartsWith("~/", StringComparison.Ordinal))
            text = home + text[1..];

        try
        {
            var basePath = Locations.IsVirtual(_location) ? home : _location;
            return IOPath.GetFullPath(text, basePath);
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

        // Search results: "Computer › Home › Documents › Search “report”"; the folder breadcrumbs lead back to it
        if (SearchQuery.TryParse(location) is { } search)
        {
            var folder = new List<AddressSegmentViewModel>(Build(search.Folder));
            folder[^1].IsCurrent = false;
            folder.Add(Segment(search.ShortTitle, location, MaterialIconKind.Magnify, null));
            return Finish(folder);
        }

        // Inside a network share: "Network › media on nas › Movies", not GVfs's folder of mounts
        if (NetworkLocations.FindMount(location) is { } mount)
        {
            List<AddressSegmentViewModel> network =
            [
                Segment("Network", Locations.Network, MaterialIconKind.LanConnect, null),
                Segment(mount.Name, mount.MountPoint, LocationIcons.ForNetwork(mount.Protocol),
                    () => ListSubdirectories(mount.MountPoint)),
            ];
            return Finish(AppendFolders(network, mount.MountPoint, location));
        }

        var segments = new List<AddressSegmentViewModel>
        {
            Segment("Computer", Locations.Computer, MaterialIconKind.Monitor, ListComputer)
        };

        if (location == Locations.Computer)
            return Finish(segments);

        var (rootName, rootPath, rootIcon) = FindRoot(location);
        segments.Add(Segment(rootName, rootPath, rootIcon, () => ListSubdirectories(rootPath)));

        return Finish(AppendFolders(segments, rootPath, location));
    }

    /// <summary>A breadcrumb for each folder between <paramref name="rootPath"/> and <paramref name="location"/>.</summary>
    private List<AddressSegmentViewModel> AppendFolders(List<AddressSegmentViewModel> segments, string rootPath,
        string location)
    {
        var relative = IOPath.GetRelativePath(rootPath, location);
        if (relative == ".")
            return segments;

        var current = rootPath;
        foreach (var part in relative.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            current = IOPath.Combine(current, part);
            var path = current;
            segments.Add(Segment(part, path, null, () => ListSubdirectories(path)));
        }

        return segments;
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

/// <summary>One autocomplete row.</summary>
public sealed partial class AddressSuggestionViewModel : ObservableObject
{
    public AddressSuggestionViewModel(string text, string fullPath, Action<AddressSuggestionViewModel> open)
    {
        Text = text;
        FullPath = fullPath;
        OpenCommand = new RelayCommand(() => open(this));
    }

    /// <summary>What goes into the box, in the form the user typed it ("~/.local", not "/home/igorb/.local").</summary>
    public string Text { get; }

    public string FullPath { get; }

    /// <summary>Picked with the arrow keys.</summary>
    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    public IRelayCommand OpenCommand { get; }
}
