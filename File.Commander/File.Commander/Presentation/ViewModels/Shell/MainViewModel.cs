using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia.Input;
using CommunityToolkit.Mvvm.Input;
using File.Commander.Application.Keyboard;
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
    private PaneViewModel _activePane;
    private bool _showHiddenFiles;
    
    private readonly FileColumnsViewModel _columns = new();
    private readonly IDialogService _dialogService;
    private readonly ISettingsService _settings;
    private readonly IDesktopService _desktopService;
    private readonly IKeymapService _keymap;

    // What the shell currently runs with; compared on every save to apply only what changed
    private AppSettings _appliedSettings;

    public SidebarViewModel Sidebar { get; }

    public AddressBarViewModel AddressBar { get; }

    /// <summary>The details panel on the right, toggled with Space.</summary>
    public InfoPanelViewModel InfoPanel { get; } = new();

    /// <summary>The views of the content area: one, or two side by side in split view.</summary>
    public ObservableCollection<PaneViewModel> Panes { get; } = [];

    /// <summary>The view the title bar, address bar and sidebar act on. Picked by clicking into a view.</summary>
    public PaneViewModel ActivePane => _activePane;

    public bool IsSplit => Panes.Count > 1;

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

            RefreshPanes(page => page is DirectoryViewModel);

            // Ctrl+H and the sort menu change the stored setting too, so Settings and the next start agree
            var current = _settings.Current;
            var basic = current.Basic!; 
            if (basic.ShowHiddenFiles != value)
                _ = SaveSettingsAsync(current with { Basic = basic with { ShowHiddenFiles = value } });
        }
    }

    /// <summary>Only folder pages have a layout or an order to change.</summary>
    public bool IsFolderPage => CurrentPage is DirectoryViewModel;
    
    /// <summary>What the active view shows. Changes on its navigation and when another view becomes active.</summary>
    public PageViewModel CurrentPage => ActivePane.CurrentPage;

#pragma warning disable CA1822
    public bool IsNotHyprland => !DesktopEnvironmentHelper.IsHyprland();
#pragma warning restore CA1822

    public bool HasNotDesktopFile => !_desktopService.HasDesktopFile;
    
    /// <summary>Layout of the active view. Each view keeps its own.</summary>
    private DirectoryViewMode ViewMode
    {
        get => ActivePane.ViewMode;
        set
        {
            if (ActivePane.ViewMode == value)
                return;

            ActivePane.ViewMode = value;
            NotifyViewModeChanged();
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

            // Empty while the constructor sets the stored order: the first page is created with it
            foreach (var pane in Panes)
            {
                if (pane.CurrentPage is DirectoryViewModel directory)
                    directory.Sort = FileSort.Of(value);
            }

            // Saved like ShowHiddenFiles; the check also keeps OnSettingsChanged from saving it back
            var current = _settings.Current;
            var basic = current.Basic!;
            var stored = ToSortOrder(value);
            if (basic.SortOrder != stored)
                _ = SaveSettingsAsync(current with { Basic = basic with { SortOrder = stored } });
        }
    }

    public MainViewModel(IDialogService dialogService, ISettingsService settings, IDesktopService desktopService,
        IKeymapService keymap)
    {
        _dialogService = dialogService;
        _settings = settings;
        _desktopService = desktopService;
        _keymap = keymap;
        _appliedSettings = settings.Current;

        var basic = _appliedSettings.Basic!;
        _showHiddenFiles = basic.ShowHiddenFiles;

        Sidebar = new SidebarViewModel(SystemLocations.GetUserDirectories(), SystemLocations.GetVolumes(),
            _appliedSettings.Sidebar!);
        Sidebar.NavigationRequested += (_, location) => Navigate(location);
        AddressBar = new AddressBarViewModel(this);

        // Before the first page: it is created with this order
        SortMode = ToSortMode(basic.SortOrder);

        var start = StartLocationOf(basic.StartLocation);
        _activePane = CreatePane(start, ToViewMode(_appliedSettings.Workspace!.DefaultView));
        Panes.Add(_activePane);
        UpdatePaneStates();
        SyncWithActivePane();

        _settings.Changed += OnSettingsChanged;
    }

    /// <summary>Opens <paramref name="location"/> in the active view as a new history entry. Clears Forward.</summary>
    public void Navigate(string location) => ActivePane.Navigate(location);

    /// <summary>Makes <paramref name="pane"/> the view the shell acts on. Called by a click or focus inside it.</summary>
    public void ActivatePane(PaneViewModel pane)
    {
        if (ReferenceEquals(pane, _activePane) || !Panes.Contains(pane))
            return;

        _activePane = pane;
        UpdatePaneStates();
        OnPropertyChanged(nameof(ActivePane));
        SyncWithActivePane();
    }

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void GoBack() => ActivePane.GoBack();

    private bool CanGoBack() => ActivePane.CanGoBack;

    [RelayCommand(CanExecute = nameof(CanGoForward))]
    private void GoForward() => ActivePane.GoForward();

    private bool CanGoForward() => ActivePane.CanGoForward;

    [RelayCommand(CanExecute = nameof(CanGoUp))]
    private void GoUp() => ActivePane.GoUp();

    private bool CanGoUp() => ActivePane.CanGoUp;

    [RelayCommand]
    private void GoComputer() => Navigate(Locations.Computer);

    /// <summary>Rebuilds the active view's page in place, without a history entry.</summary>
    [RelayCommand]
    private void Refresh() => ActivePane.Refresh();

    /// <summary>
    /// The button between the view switcher and Search (F3 by default). Splits the window into two views,
    /// the new one opening the active view's folder in the same layout with a history of its own.
    /// While split, closes the other view and keeps the active one.
    /// </summary>
    [RelayCommand]
    private void ToggleSplitView()
    {
        if (IsSplit)
        {
            foreach (var pane in Panes.Where(p => !ReferenceEquals(p, _activePane)).ToList())
                ClosePane(pane);
        }
        else
        {
            var source = ActivePane;
            var pane = CreatePane(source.Location, source.ViewMode);
            Panes.Insert(Panes.IndexOf(source) + 1, pane);
        }

        UpdatePaneStates();
        OnPropertyChanged(nameof(IsSplit));
    }

    [RelayCommand(CanExecute = nameof(CanChangeViewMode))]
    private void ShowGridView() => ViewMode = DirectoryViewMode.Grid;

    [RelayCommand(CanExecute = nameof(CanChangeViewMode))]
    private void ShowListView() => ViewMode = DirectoryViewMode.List;

    [RelayCommand(CanExecute = nameof(CanChangeViewMode))]
    private void ShowTreeView() => ViewMode = DirectoryViewMode.Tree;

    private bool CanChangeViewMode() => IsFolderPage;

    /// <summary>Space by default. Only folder pages have files to describe.</summary>
    [RelayCommand(CanExecute = nameof(IsFolderPage))]
    private void ToggleInfoPanel() => InfoPanel.IsOpen = !InfoPanel.IsOpen;

    /// <summary>Ctrl+H. Works on any page; the choice applies to the next folder opened.</summary>
    [RelayCommand]
    private void ToggleHiddenFiles() => ShowHiddenFiles = !ShowHiddenFiles;

    /// <summary>Ctrl+Alt+1…4 by default. Saved like a pick in Settings; App applies the stored theme.</summary>
    [RelayCommand]
    private void SelectTheme(AppTheme theme)
    {
        var current = _settings.Current;
        var basic = current.Basic!;
        if (basic.Theme != theme)
            _ = SaveSettingsAsync(current with { Basic = basic with { Theme = theme } });
    }

    /// <summary>
    /// Runs the action bound to this key press in Settings → Keymap.
    /// </summary>
    /// <param name="isTyping">A text box has focus: only actions that don't fight typing run.</param>
    /// <returns>True when an action ran, so the key press is consumed.</returns>
    public bool HandleKey(Key key, KeyModifiers modifiers, bool isTyping)
    {
        if (KeyChord.FromKeyPress(key, modifiers) is not { } chord)
            return false;

        if (_keymap.Current.Find(chord) is not { } action)
            return TryCloseInfoPanel(chord, isTyping);

        if (isTyping && (!action.WorksWhileTyping || !chord.IsCommandChord))
            return false;

        var (command, parameter) = CommandFor(action.Id);
        if (command is null || !command.CanExecute(parameter))
            return false;

        command.Execute(parameter);
        return true;
    }

    /// <summary>Esc closes the info panel, unless Esc is bound to something else or a text box has it.</summary>
    private bool TryCloseInfoPanel(KeyChord chord, bool isTyping)
    {
        if (isTyping || chord != new KeyChord(Key.Escape) || !InfoPanel.IsShown)
            return false;

        InfoPanel.IsOpen = false;
        return true;
    }

    private (ICommand? Command, object? Parameter) CommandFor(string actionId) => actionId switch
    {
        KeymapActions.ToggleHiddenFiles => (ToggleHiddenFilesCommand, null),
        KeymapActions.Refresh => (RefreshCommand, null),
        KeymapActions.OpenSettings => (SettingsCommand, null),
        KeymapActions.ToggleInfoPanel => (ToggleInfoPanelCommand, null),
        KeymapActions.EditPath => (AddressBar.BeginEditCommand, null),
        KeymapActions.GoBack => (GoBackCommand, null),
        KeymapActions.GoForward => (GoForwardCommand, null),
        KeymapActions.GoUp => (GoUpCommand, null),
        KeymapActions.GoComputer => (GoComputerCommand, null),
        KeymapActions.GridView => (ShowGridViewCommand, null),
        KeymapActions.ListView => (ShowListViewCommand, null),
        KeymapActions.TreeView => (ShowTreeViewCommand, null),
        KeymapActions.ToggleSplitView => (ToggleSplitViewCommand, null),
        KeymapActions.ThemeLatte => (SelectThemeCommand, AppTheme.Latte),
        KeymapActions.ThemeFrappe => (SelectThemeCommand, AppTheme.Frappe),
        KeymapActions.ThemeMacchiato => (SelectThemeCommand, AppTheme.Macchiato),
        KeymapActions.ThemeMocha => (SelectThemeCommand, AppTheme.Mocha),
        _ => (null, null),
    };

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
        
        OnPropertyChanged(nameof(HasNotDesktopFile));
    }

    /// <summary>Settings are saved as they change; this applies each save to the running window.</summary>
    private void OnSettingsChanged(object? sender, AppSettings settings)
    {
        var previous = _appliedSettings;
        _appliedSettings = settings;

        var (basic, wasBasic) = (settings.Basic, previous.Basic);
        var reloadFolders = false;
        var reloadComputer = false;

        // The field, not the property: the property would save the setting back and reload on its own
        if (basic!.ShowHiddenFiles != wasBasic!.ShowHiddenFiles
            && SetProperty(ref _showHiddenFiles, basic.ShowHiddenFiles, nameof(ShowHiddenFiles)))
            reloadFolders = true;

        // Fixed for a folder page, so the page is rebuilt
        if (basic.ShowFileExtensions != wasBasic.ShowFileExtensions
            || basic.MixFilesAndFolders != wasBasic.MixFilesAndFolders
            || basic.OpenFile != wasBasic.OpenFile)
            reloadFolders = true;

        if (settings.Workspace!.HideSystemDisk != previous.Workspace!.HideSystemDisk)
            reloadComputer = true;

        // Only a change of the default itself: Ctrl+1/2/3 choices survive unrelated saves.
        // A new default applies to every view.
        if (settings.Workspace.DefaultView != previous.Workspace.DefaultView)
        {
            var mode = ToViewMode(settings.Workspace.DefaultView);
            foreach (var pane in Panes)
                pane.ViewMode = mode;

            NotifyViewModeChanged();
        }

        if (basic.SortOrder != wasBasic.SortOrder)
            SortMode = ToSortMode(basic.SortOrder);

        if (settings.Sidebar != previous.Sidebar)
        {
            Sidebar.Apply(settings.Sidebar!);
            Sidebar.Select(CurrentPage.Location);
        }

        if (reloadFolders || reloadComputer)
            RefreshPanes(page => (reloadFolders && page is DirectoryViewModel)
                                 || (reloadComputer && page is ComputerViewModel));
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
        InfoPanel.Dispose();

        foreach (var pane in Panes)
        {
            pane.Navigated -= OnPaneNavigated;
            pane.Dispose();
        }

        base.OnDispose();
    }

    private PaneViewModel CreatePane(string location, DirectoryViewMode viewMode)
    {
        var pane = new PaneViewModel(location, viewMode, CreatePage);
        pane.Navigated += OnPaneNavigated;
        return pane;
    }

    private void ClosePane(PaneViewModel pane)
    {
        pane.Navigated -= OnPaneNavigated;
        Panes.Remove(pane);
        pane.Dispose();
    }

    /// <summary>Only the active view drives the shell; the other one keeps its own path.</summary>
    private void OnPaneNavigated(object? sender, EventArgs e)
    {
        if (ReferenceEquals(sender, _activePane))
            SyncWithActivePane();
    }

    private void UpdatePaneStates()
    {
        var split = IsSplit;
        foreach (var pane in Panes)
        {
            pane.IsSplit = split;
            pane.IsActive = ReferenceEquals(pane, _activePane);
        }
    }

    /// <summary>Points the address bar, sidebar, info panel and title bar buttons at the active view's page.</summary>
    private void SyncWithActivePane()
    {
        var page = CurrentPage;
        InfoPanel.Attach(page);
        Sidebar.Select(page.Location);
        AddressBar.Update(page.Location);

        OnPropertyChanged(nameof(CurrentPage));
        OnPropertyChanged(nameof(IsFolderPage));
        NotifyViewModeChanged();

        GoBackCommand.NotifyCanExecuteChanged();
        GoForwardCommand.NotifyCanExecuteChanged();
        GoUpCommand.NotifyCanExecuteChanged();
        ShowGridViewCommand.NotifyCanExecuteChanged();
        ShowListViewCommand.NotifyCanExecuteChanged();
        ShowTreeViewCommand.NotifyCanExecuteChanged();
        ToggleInfoPanelCommand.NotifyCanExecuteChanged();
    }

    private void NotifyViewModeChanged()
    {
        OnPropertyChanged(nameof(IsGridView));
        OnPropertyChanged(nameof(IsListView));
        OnPropertyChanged(nameof(IsTreeView));
    }

    /// <summary>Rebuilds the page of every view whose page matches, e.g. all folders after Show hidden files.</summary>
    private void RefreshPanes(Func<PageViewModel, bool> needsRefresh)
    {
        foreach (var pane in Panes.ToList())
        {
            if (needsRefresh(pane.CurrentPage))
                pane.Refresh();
        }
    }

    // History stores locations, not pages: going back re-reads the folder, so it's never stale.
    // The pane is the page's navigator, so a folder opened in a view opens in that view.
    private PageViewModel CreatePage(PaneViewModel pane, string location)
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
                return new ComputerViewModel(SystemLocations.GetUserDirectories(), shown, pane);
            }
            case Locations.Recent:
                return new PlaceholderPageViewModel(location, "Recent", MaterialIconKind.ClockOutline);
            case Locations.Trash:
                return new PlaceholderPageViewModel(location, "Trash", MaterialIconKind.TrashCanOutline);
            case Locations.Network:
                return new PlaceholderPageViewModel(location, "Network", MaterialIconKind.LanConnect);
        }

        var directory = new DirectoryViewModel(location, pane.ViewMode, FileSort.Of(SortMode), CurrentFolderOptions(), _columns, pane);
        _ = directory.LoadAsync(); // never throws, reports errors through Error
        return directory;
    }
}
