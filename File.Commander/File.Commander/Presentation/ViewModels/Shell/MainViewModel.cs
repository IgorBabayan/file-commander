using CommunityToolkit.Mvvm.Input;
using File.Commander.Application.Path;
using File.Commander.Presentation.Services;
using File.Commander.Presentation.ViewModels.Browser;
using File.Commander.Presentation.ViewModels.Computer;
using File.Commander.Presentation.ViewModels.Pages;
using Material.Icons;

namespace File.Commander.Presentation.ViewModels.Shell;

public partial class MainViewModel : ViewModelBase, INavigator
{
    private readonly Stack<string> _back = new();
    private readonly Stack<string> _forward = new();
    private PageViewModel _currentPage;
    private DirectoryViewMode _viewMode = DirectoryViewMode.List;
    private FileSortMode _sortMode = FileSortMode.NameAscending;
    private bool _showHiddenFiles;
    private readonly FileColumnsViewModel _columns = new();

    public SidebarViewModel Sidebar { get; }

    public AddressBarViewModel AddressBar { get; }

    /// <summary>Layout of folder pages. Kept across navigation.</summary>
    public DirectoryViewMode ViewMode
    {
        get => _viewMode;
        private set
        {
            if (!SetProperty(ref _viewMode, value))
                return;

            OnPropertyChanged(nameof(IsGridView));
            OnPropertyChanged(nameof(IsListView));
            OnPropertyChanged(nameof(IsTreeView));

            if (CurrentPage is DirectoryViewModel directory)
                directory.ViewMode = value;
        }
    }

    public bool IsGridView => ViewMode == DirectoryViewMode.Grid;

    public bool IsListView => ViewMode == DirectoryViewMode.List;

    public bool IsTreeView => ViewMode == DirectoryViewMode.Tree;

    /// <summary>Order of folder pages. Kept across navigation.</summary>
    public FileSortMode SortMode
    {
        get => _sortMode;
        private set
        {
            if (!SetProperty(ref _sortMode, value))
                return;

            OnPropertyChanged(nameof(IsSortedAToZ));
            OnPropertyChanged(nameof(IsSortedZToA));
            OnPropertyChanged(nameof(IsSortedNewestFirst));
            OnPropertyChanged(nameof(IsSortedOldestFirst));
            OnPropertyChanged(nameof(IsSortedBySize));
            OnPropertyChanged(nameof(IsSortedByType));

            if (CurrentPage is DirectoryViewModel directory)
                directory.SortMode = value;
        }
    }

    // Bound two-way to the sort menu's radio buttons. A radio button that gets unchecked
    // (because another one was picked) writes false, which is ignored.
    public bool IsSortedAToZ
    {
        get => SortMode == FileSortMode.NameAscending;
        set => PickSort(value, FileSortMode.NameAscending);
    }

    public bool IsSortedZToA
    {
        get => SortMode == FileSortMode.NameDescending;
        set => PickSort(value, FileSortMode.NameDescending);
    }

    public bool IsSortedNewestFirst
    {
        get => SortMode == FileSortMode.NewestFirst;
        set => PickSort(value, FileSortMode.NewestFirst);
    }

    public bool IsSortedOldestFirst
    {
        get => SortMode == FileSortMode.OldestFirst;
        set => PickSort(value, FileSortMode.OldestFirst);
    }

    public bool IsSortedBySize
    {
        get => SortMode == FileSortMode.LargestFirst;
        set => PickSort(value, FileSortMode.LargestFirst);
    }

    public bool IsSortedByType
    {
        get => SortMode == FileSortMode.Type;
        set => PickSort(value, FileSortMode.Type);
    }

    /// <summary>Whether folder pages list dot files. Kept across navigation. Changing it reloads the current folder.</summary>
    public bool ShowHiddenFiles
    {
        get => _showHiddenFiles;
        set
        {
            if (SetProperty(ref _showHiddenFiles, value) && IsFolderPage)
                Refresh();
        }
    }

    /// <summary>Only folder pages have a layout or an order to change.</summary>
    public bool IsFolderPage => CurrentPage is DirectoryViewModel;

#pragma warning disable CA1822
    public bool IsNotHyprland => !DesktopEnvironmentHelper.IsHyprland();
#pragma warning restore CA1822

    public MainViewModel()
    {
        Sidebar = new SidebarViewModel(SystemLocations.GetUserDirectories(), SystemLocations.GetVolumes());
        Sidebar.NavigationRequested += (_, location) => Navigate(location);
        AddressBar = new AddressBarViewModel(this);

        _currentPage = CreatePage(Locations.Computer);
        Sidebar.Select(Locations.Computer);
        AddressBar.Update(Locations.Computer);
    }

    /// <summary>What the content area shows. Replaced (and the old one disposed) on every navigation.</summary>
    public PageViewModel CurrentPage
    {
        get => _currentPage;
        private set => SetProperty(ref _currentPage, value);
    }

    /// <summary>Opens <paramref name="location"/> as a new history entry. Clears Forward.</summary>
    public void Navigate(string location)
    {
        location = Locations.Normalize(location);
        if (Locations.AreEqual(location, CurrentPage.Location))
            return;

        _back.Push(CurrentPage.Location);
        _forward.Clear();
        Show(location);
    }

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void GoBack()
    {
        _forward.Push(CurrentPage.Location);
        Show(_back.Pop());
    }

    private bool CanGoBack() => _back.Count > 0;

    [RelayCommand(CanExecute = nameof(CanGoForward))]
    private void GoForward()
    {
        _back.Push(CurrentPage.Location);
        Show(_forward.Pop());
    }

    private bool CanGoForward() => _forward.Count > 0;

    [RelayCommand(CanExecute = nameof(CanGoUp))]
    private void GoUp()
    {
        if (ParentOf(CurrentPage.Location) is { } parent)
            Navigate(parent);
    }

    private bool CanGoUp() => ParentOf(CurrentPage.Location) is not null;

    [RelayCommand]
    private void GoComputer() => Navigate(Locations.Computer);

    /// <summary>Rebuilds the current page in place, without a history entry.</summary>
    [RelayCommand]
    private void Refresh() => Show(CurrentPage.Location);

    [RelayCommand(CanExecute = nameof(CanChangeViewMode))]
    private void ShowGridView() => ViewMode = DirectoryViewMode.Grid;

    [RelayCommand(CanExecute = nameof(CanChangeViewMode))]
    private void ShowListView() => ViewMode = DirectoryViewMode.List;

    [RelayCommand(CanExecute = nameof(CanChangeViewMode))]
    private void ShowTreeView() => ViewMode = DirectoryViewMode.Tree;

    private bool CanChangeViewMode() => IsFolderPage;

    /// <summary>Ctrl+H. Works on any page; the choice applies to the next folder opened.</summary>
    [RelayCommand]
    private void ToggleHiddenFiles() => ShowHiddenFiles = !ShowHiddenFiles;

    private void PickSort(bool picked, FileSortMode mode)
    {
        if (picked)
            SortMode = mode;
    }

    [RelayCommand]
    public async Task Settings(CancellationToken cancellationToken = default)
    {
        /*var result = await _dialogService.ShowDialogAsync<SettingsViewModel, bool>(_settingsViewModel);*/
    }

    protected override void OnDispose()
    {
        CurrentPage.Dispose();
        base.OnDispose();
    }

    private void Show(string location)
    {
        var previous = CurrentPage;
        CurrentPage = CreatePage(location);
        previous.Dispose();

        Sidebar.Select(location);
        AddressBar.Update(location);

        GoBackCommand.NotifyCanExecuteChanged();
        GoForwardCommand.NotifyCanExecuteChanged();
        GoUpCommand.NotifyCanExecuteChanged();
        ShowGridViewCommand.NotifyCanExecuteChanged();
        ShowListViewCommand.NotifyCanExecuteChanged();
        ShowTreeViewCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(IsFolderPage));
    }

    // History stores locations, not pages: going back re-reads the folder, so it's never stale
    private PageViewModel CreatePage(string location)
    {
        switch (location)
        {
            case Locations.Computer:
            {
                // Re-query so mounted/unmounted drives and free space are current
                var volumes = SystemLocations.GetVolumes();
                AddressBar.Volumes = volumes;
                return new ComputerViewModel(SystemLocations.GetUserDirectories(), volumes, this);
            }
            case Locations.Recent:
                return new PlaceholderPageViewModel(location, "Recent", MaterialIconKind.ClockOutline);
            case Locations.Trash:
                return new PlaceholderPageViewModel(location, "Trash", MaterialIconKind.TrashCanOutline);
            case Locations.Network:
                return new PlaceholderPageViewModel(location, "Network", MaterialIconKind.LanConnect);
        }

        var directory = new DirectoryViewModel(location, ViewMode, SortMode, ShowHiddenFiles, _columns, this);
        _ = directory.LoadAsync(); // never throws, reports errors through Error
        return directory;
    }

    private static string? ParentOf(string location)
        => Locations.IsVirtual(location) ? null : IOPath.GetDirectoryName(location); // null for "/"
}
