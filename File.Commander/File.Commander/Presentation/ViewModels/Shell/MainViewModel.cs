using CommunityToolkit.Mvvm.Input;
using File.Commander.Application.Path;
using File.Commander.Application.Settings;
using File.Commander.Presentation.Services;
using File.Commander.Presentation.ViewModels.Browser;
using File.Commander.Presentation.ViewModels.Computer;
using File.Commander.Presentation.ViewModels.Pages;
using File.Commander.Presentation.ViewModels.Settings;
using Material.Icons;

namespace File.Commander.Presentation.ViewModels.Shell;

public partial class MainViewModel : ViewModelBase, INavigator
{
    private readonly Stack<string> _back = new();
    private readonly Stack<string> _forward = new();
    private PageViewModel _currentPage;
    private DirectoryViewMode _viewMode;
    private bool _showHiddenFiles;
    
    private readonly FileColumnsViewModel _columns = new();
    private readonly IDialogService _dialogService;
    private readonly ISettingsService _settings;
    private readonly IDesktopService _desktopService;

    // What the shell currently runs with; compared on every save to apply only what changed
    private AppSettings _appliedSettings;

    public SidebarViewModel Sidebar { get; }

    public AddressBarViewModel AddressBar { get; }

    public bool IsGridView => ViewMode == DirectoryViewMode.Grid;

    public bool IsListView => ViewMode == DirectoryViewMode.List;

    public bool IsTreeView => ViewMode == DirectoryViewMode.Tree;

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
            if (!SetProperty(ref _showHiddenFiles, value))
                return;

            if (IsFolderPage)
                Refresh();

            // Ctrl+H and the sort menu change the stored setting too, so Settings and the next start agree
            var current = _settings.Current;
            var basic = current.Basic!; 
            if (basic.ShowHiddenFiles != value)
                _ = SaveSettingsAsync(current with { Basic = basic with { ShowHiddenFiles = value } });
        }
    }

    /// <summary>Only folder pages have a layout or an order to change.</summary>
    public bool IsFolderPage => CurrentPage is DirectoryViewModel;
    
    /// <summary>What the content area shows. Replaced (and the old one disposed) on every navigation.</summary>
    public PageViewModel CurrentPage
    {
        get => _currentPage;
        private set => SetProperty(ref _currentPage, value);
    }

#pragma warning disable CA1822
    public bool IsNotHyprland => !DesktopEnvironmentHelper.IsHyprland();
#pragma warning restore CA1822

    public bool HasNotDesktopFile => !_desktopService.HasDesktopFile;
    
    private DirectoryViewMode ViewMode
    {
        get => _viewMode;
        set
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
    
    private FileSortMode SortMode
    {
        get;
        set
        {
            if (!SetProperty(ref field, value))
                return;

            OnPropertyChanged(nameof(IsSortedAToZ));
            OnPropertyChanged(nameof(IsSortedZToA));
            OnPropertyChanged(nameof(IsSortedNewestFirst));
            OnPropertyChanged(nameof(IsSortedOldestFirst));
            OnPropertyChanged(nameof(IsSortedBySize));
            OnPropertyChanged(nameof(IsSortedByType));

            if (CurrentPage is DirectoryViewModel directory)
                directory.Sort = FileSort.Of(value);

            // Saved like ShowHiddenFiles; the check also keeps OnSettingsChanged from saving it back
            var current = _settings.Current;
            var basic = current.Basic!;
            var stored = ToSortOrder(value);
            if (basic.SortOrder != stored)
                _ = SaveSettingsAsync(current with { Basic = basic with { SortOrder = stored } });
        }
    }

    public MainViewModel(IDialogService dialogService, ISettingsService settings, IDesktopService desktopService)
    {
        _dialogService = dialogService;
        _settings = settings;
        _desktopService = desktopService;
        _appliedSettings = settings.Current;

        var basic = _appliedSettings.Basic!;
        _showHiddenFiles = basic.ShowHiddenFiles;
        _viewMode = ToViewMode(_appliedSettings.Workspace!.DefaultView);

        Sidebar = new SidebarViewModel(SystemLocations.GetUserDirectories(), SystemLocations.GetVolumes(),
            _appliedSettings.Sidebar!);
        Sidebar.NavigationRequested += (_, location) => Navigate(location);
        AddressBar = new AddressBarViewModel(this);

        // Before the first page: it is created with this order
        SortMode = ToSortMode(basic.SortOrder);

        var start = StartLocationOf(basic.StartLocation);
        _currentPage = CreatePage(start);
        Sidebar.Select(start);
        AddressBar.Update(start);

        _settings.Changed += OnSettingsChanged;
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

    /// <summary>
    /// A click in the sort menu. Also runs when the option already was checked, so it puts back
    /// the menu's order in a folder that a header click re-sorted.
    /// </summary>
    [RelayCommand]
    private void ApplySort(FileSortMode mode)
    {
        SortMode = mode;
        if (CurrentPage is DirectoryViewModel directory)
            directory.Sort = FileSort.Of(mode);
    }

    [RelayCommand]
    private async Task Settings()
    {
        // A fresh one per opening: it reads the stored settings when created
        using var settings = new SettingsViewModel(_settings);
        await _dialogService.ShowDialogAsync<SettingsViewModel, bool>(settings);
    }

    [RelayCommand]
    private async Task CreateDesktopFile(CancellationToken cancellationToken = default)
    {
        _desktopService.BuildDesktopFile();
        await _desktopService.SaveDesktopFileAsync(cancellationToken);
    }

    /// <summary>Settings are saved as they change; this applies each save to the running window.</summary>
    private void OnSettingsChanged(object? sender, AppSettings settings)
    {
        var previous = _appliedSettings;
        _appliedSettings = settings;

        var (basic, wasBasic) = (settings.Basic, previous.Basic);
        var reload = false;

        // The field, not the property: the property would save the setting back and reload on its own
        if (basic!.ShowHiddenFiles != wasBasic!.ShowHiddenFiles
            && SetProperty(ref _showHiddenFiles, basic.ShowHiddenFiles, nameof(ShowHiddenFiles)))
            reload |= IsFolderPage;

        // Fixed for a folder page, so the page is rebuilt
        if (basic.ShowFileExtensions != wasBasic.ShowFileExtensions
            || basic.MixFilesAndFolders != wasBasic.MixFilesAndFolders
            || basic.OpenFile != wasBasic.OpenFile)
            reload |= IsFolderPage;

        if (settings.Workspace!.HideSystemDisk != previous.Workspace!.HideSystemDisk)
            reload |= CurrentPage is ComputerViewModel;

        // Only a change of the default itself: Ctrl+1/2/3 choices survive unrelated saves
        if (settings.Workspace.DefaultView != previous.Workspace.DefaultView)
            ViewMode = ToViewMode(settings.Workspace.DefaultView);

        if (basic.SortOrder != wasBasic.SortOrder)
            SortMode = ToSortMode(basic.SortOrder);

        if (settings.Sidebar != previous.Sidebar)
        {
            Sidebar.Apply(settings.Sidebar!);
            Sidebar.Select(CurrentPage.Location);
        }

        if (reload)
            Refresh();
    }

    private async Task SaveSettingsAsync(AppSettings settings)
    {
        try
        {
            await _settings.SaveAsync(settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.WriteLine($"Can't save settings: {ex.Message}");
        }
    }

    private FolderOptions CurrentFolderOptions() => new(
        ShowHidden: ShowHiddenFiles,
        ShowExtensions: _appliedSettings.Basic!.ShowFileExtensions,
        MixFilesAndFolders: _appliedSettings.Basic.MixFilesAndFolders,
        OpenOnSingleClick: _appliedSettings.Basic.OpenFile == OpenFileMode.Click);

    private static DirectoryViewMode ToViewMode(FolderViewMode mode) => mode switch
    {
        FolderViewMode.Grid => DirectoryViewMode.Grid,
        FolderViewMode.Tree => DirectoryViewMode.Tree,
        _ => DirectoryViewMode.List,
    };

    private static FileSortMode ToSortMode(FolderSortOrder order) => order switch
    {
        FolderSortOrder.NameDescending => FileSortMode.NameDescending,
        FolderSortOrder.NewestFirst => FileSortMode.NewestFirst,
        FolderSortOrder.OldestFirst => FileSortMode.OldestFirst,
        FolderSortOrder.LargestFirst => FileSortMode.LargestFirst,
        FolderSortOrder.Type => FileSortMode.Type,
        _ => FileSortMode.NameAscending,
    };

    private static FolderSortOrder ToSortOrder(FileSortMode mode) => mode switch
    {
        FileSortMode.NameDescending => FolderSortOrder.NameDescending,
        FileSortMode.NewestFirst => FolderSortOrder.NewestFirst,
        FileSortMode.OldestFirst => FolderSortOrder.OldestFirst,
        FileSortMode.LargestFirst => FolderSortOrder.LargestFirst,
        FileSortMode.Type => FolderSortOrder.Type,
        _ => FolderSortOrder.NameAscending,
    };

    private static string StartLocationOf(StartLocation start) => start switch
    {
        StartLocation.Home => SystemLocations.HomeDirectory,
        StartLocation.Recent => Locations.Recent,
        _ => Locations.Computer,
    };

    protected override void OnDispose()
    {
        _settings.Changed -= OnSettingsChanged;
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

                // Hidden from the page only: the address bar still needs it to name paths under "/"
                var shown = _appliedSettings.Workspace!.HideSystemDisk
                    ? volumes.Where(v => v.Kind != VolumeKind.System).ToList()
                    : volumes;
                return new ComputerViewModel(SystemLocations.GetUserDirectories(), shown, this);
            }
            case Locations.Recent:
                return new PlaceholderPageViewModel(location, "Recent", MaterialIconKind.ClockOutline);
            case Locations.Trash:
                return new PlaceholderPageViewModel(location, "Trash", MaterialIconKind.TrashCanOutline);
            case Locations.Network:
                return new PlaceholderPageViewModel(location, "Network", MaterialIconKind.LanConnect);
        }

        var directory = new DirectoryViewModel(location, ViewMode, FileSort.Of(SortMode), CurrentFolderOptions(), _columns, this);
        _ = directory.LoadAsync(); // never throws, reports errors through Error
        return directory;
    }

    private static string? ParentOf(string location)
        => Locations.IsVirtual(location) ? null : IOPath.GetDirectoryName(location); // null for "/"
}
