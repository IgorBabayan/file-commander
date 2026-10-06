using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using Avalonia.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;

namespace File.Commander.Presentation.ViewModels.Shell;

public partial class MainViewModel : ViewModelBase, INavigator
{
    private TabViewModel _activeTab;
    private bool _showHiddenFiles;
    
    private readonly FileColumnsViewModel _columns = new();
    private readonly IDialogService _dialogService;
    private readonly ISettingsService _settings;
    private readonly IDesktopService _desktopService;
    private readonly IKeymapService _keymap;

    // What the shell currently runs with; compared on every save to apply only what changed
    private AppSettings _appliedSettings;

    // A column change waiting to be stored: resizing changes the width on every pointer move,
    // so changes are stored once they stop for a moment
    private IDisposable? _pendingColumnsSave;
    private static readonly TimeSpan ColumnsSaveDelay = TimeSpan.FromMilliseconds(400);

    // Applying stored columns: that isn't a change to store
    private bool _applyingColumns;

    public SidebarViewModel Sidebar { get; }

    public AddressBarViewModel AddressBar { get; }

    /// <summary>The title bar's items and Customize Toolbar….</summary>
    public ToolbarViewModel Toolbar { get; }

    /// <summary>The tabs of the window (Ctrl+T). Each has its own views, split view and info panel.</summary>
    public ObservableCollection<TabViewModel> Tabs { get; } = [];

    /// <summary>The tab the window shows and the shell acts on.</summary>
    public TabViewModel ActiveTab => _activeTab;

    /// <summary>The tab bar only shows while there is more than one tab.</summary>
    public bool HasMultipleTabs => Tabs.Count > 1;

    /// <summary>The details panel of the selected tab, toggled with Space.</summary>
    public InfoPanelViewModel InfoPanel => ActiveTab.InfoPanel;

    /// <summary>The view the title bar, address bar and sidebar act on. Picked by clicking into a view.</summary>
    public PaneViewModel ActivePane => ActiveTab.ActivePane;

    /// <summary>The selected tab shows two views side by side.</summary>
    public bool IsSplit => ActiveTab.IsSplit;

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
            foreach (var pane in AllPanes)
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
        IKeymapService keymap, ActionCenterViewModel actionCenter)
    {
        _dialogService = dialogService;
        ActionCenter = actionCenter;
        _settings = settings;
        _desktopService = desktopService;
        _keymap = keymap;
        _appliedSettings = settings.Current;

        var basic = _appliedSettings.Basic!;
        _showHiddenFiles = basic.ShowHiddenFiles;

        Sidebar = new SidebarViewModel(SystemLocations.GetUserDirectories(), SystemLocations.GetVolumes(),
            _appliedSettings.Sidebar!);
        Sidebar.NavigationRequested += (_, location) => Navigate(location);
        Sidebar.OrderChanged += OnSidebarOrderChanged;
        Sidebar.FavoritesChanged += OnSidebarFavoritesChanged;
        AddressBar = new AddressBarViewModel(this);
        AddressBar.ConnectRequested += (_, address) => ConnectToServer(address);
        Toolbar = new ToolbarViewModel(settings);

        // Before the first page: it is created with this order
        SortMode = ToSortMode(basic.SortOrder);

        // Columns as the user left them; changed from the column header, stored as they change
        ApplyColumns(_appliedSettings.Columns!);
        _columns.PropertyChanged += OnColumnsChanged;

        var start = StartLocationOf(basic.StartLocation);
        _activeTab = CreateTab(start, ToViewMode(_appliedSettings.Workspace!.DefaultView));
        _activeTab.IsSelected = true;
        Tabs.Add(_activeTab);
        UpdateTabStates();
        SyncWithActivePane();

        _settings.Changed += OnSettingsChanged;
    }

    /// <summary>Opens <paramref name="location"/> in the active view as a new history entry. Clears Forward.</summary>
    public void Navigate(string location) => ActivePane.Navigate(location);

    /// <summary>
    /// A server address typed into the address bar (smb://nas/media, sftp://me@server…): the active view opens the
    /// Network page, which connects to it and opens the share.
    /// </summary>
    private void ConnectToServer(string address)
    {
        Navigate(Locations.Network);
        if (CurrentPage is NetworkViewModel network)
            _ = network.ConnectAsync(address); // never throws, reports problems in a notice
    }

    /// <summary>
    /// Makes <paramref name="pane"/> the view the shell acts on. Called by a click or focus inside it.
    /// Only views of the selected tab: the others are hidden.
    /// </summary>
    public void ActivatePane(PaneViewModel pane) => ActiveTab.ActivatePane(pane); // Syncs through ActivePageChanged

    /// <summary>Shows <paramref name="tab"/> and points the shell at its active view.</summary>
    public void SelectTab(TabViewModel tab)
    {
        if (ReferenceEquals(tab, _activeTab) || !Tabs.Contains(tab))
            return;

        _activeTab.IsSelected = false;
        _activeTab = tab;
        _activeTab.IsSelected = true;

        OnPropertyChanged(nameof(ActiveTab));
        SyncWithActivePane();
    }

    /// <summary>
    /// Ctrl+T by default, and the + button of the tab bar. Opens a tab next to the selected one,
    /// starting at the active view's location in the same layout, with a history of its own.
    /// </summary>
    [RelayCommand]
    private void NewTab()
    {
        var source = ActivePane;
        var tab = CreateTab(source.Location, source.ViewMode);
        Tabs.Insert(Tabs.IndexOf(_activeTab) + 1, tab);
        UpdateTabStates();
        SelectTab(tab);
    }

    /// <summary>Ctrl+W by default: closes the selected tab. The last tab stays.</summary>
    [RelayCommand(CanExecute = nameof(HasMultipleTabs))]
    private void CloseTab() => RemoveTab(_activeTab);

    /// <summary>Ctrl+Tab / Ctrl+PageDown by default. Wraps around.</summary>
    [RelayCommand(CanExecute = nameof(HasMultipleTabs))]
    private void NextTab() => SelectTab(Tabs[(Tabs.IndexOf(_activeTab) + 1) % Tabs.Count]);

    /// <summary>Ctrl+Shift+Tab / Ctrl+PageUp by default. Wraps around.</summary>
    [RelayCommand(CanExecute = nameof(HasMultipleTabs))]
    private void PreviousTab() => SelectTab(Tabs[(Tabs.IndexOf(_activeTab) - 1 + Tabs.Count) % Tabs.Count]);

    /// <summary>Closes <paramref name="tab"/>. Closing the selected one selects its right neighbour, else its left one.</summary>
    private void RemoveTab(TabViewModel tab)
    {
        var index = Tabs.IndexOf(tab);
        if (index < 0 || Tabs.Count < 2)
            return;

        if (ReferenceEquals(tab, _activeTab))
            SelectTab(Tabs[index + 1 < Tabs.Count ? index + 1 : index - 1]);

        tab.ActivePageChanged -= OnTabActivePageChanged;
        tab.SelectRequested -= OnTabSelectRequested;
        tab.CloseRequested -= OnTabCloseRequested;
        Tabs.Remove(tab);
        tab.Dispose();
        UpdateTabStates();
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
    /// The button between the view switcher and the Action center (F3 by default). Splits the selected tab into two views,
    /// the new one opening the active view's folder in the same layout with a history of its own.
    /// While split, closes the other view and keeps the active one.
    /// </summary>
    [RelayCommand]
    private void ToggleSplitView()
    {
        ActiveTab.ToggleSplitView();
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

    /// <summary>Ctrl+A by default. Selects every entry of the active view (in the tree: every visible row).</summary>
    [RelayCommand(CanExecute = nameof(IsFolderPage))]
    private void SelectAll() => (CurrentPage as DirectoryViewModel)?.SelectAll();

    /// <summary>Ctrl+Shift+A by default; Esc too while the info panel is closed.</summary>
    [RelayCommand(CanExecute = nameof(IsFolderPage))]
    private void SelectNone() => (CurrentPage as DirectoryViewModel)?.SelectNone();

    /// <summary>Ctrl+Shift+I by default.</summary>
    [RelayCommand(CanExecute = nameof(IsFolderPage))]
    private void InvertSelection() => (CurrentPage as DirectoryViewModel)?.InvertSelection();

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
    /// <returns>True when an action ran, so the key press is consumed.</returns>
    public bool HandleKey(Key key, KeyModifiers modifiers, bool isTyping)
    {
        if (KeyChord.FromKeyPress(key, modifiers) is not { } chord)
            return false;

        if (_keymap.Current.Find(chord) is not { } action)
            return TryHandleEscape(chord, isTyping);

        if (isTyping && (!action.WorksWhileTyping || !chord.IsCommandChord))
            return false;

        var (command, parameter) = CommandFor(action.Id);
        if (command is null || !command.CanExecute(parameter))
            return false;

        command.Execute(parameter);
        return true;
    }

    /// <summary>
    /// Esc, unless it is bound to something else or a text box has it: closes the info panel; else cancels a Cut
    /// (the cut items are drawn as before, the clipboard is emptied, the selection stays); else clears the active
    /// view's selection.
    /// </summary>
    private bool TryHandleEscape(KeyChord chord, bool isTyping)
    {
        if (isTyping || chord != new KeyChord(Key.Escape))
            return false;

        if (InfoPanel.IsShown)
        {
            InfoPanel.IsOpen = false;
            return true;
        }

        if (FileClipboard.CancelCut())
            return true;

        if (CurrentPage is DirectoryViewModel { HasSelection: true } page)
        {
            page.SelectNone();
            return true;
        }

        return false;
    }

    private (ICommand? Command, object? Parameter) CommandFor(string actionId) => actionId switch
    {
        KeymapActions.ToggleHiddenFiles => (ToggleHiddenFilesCommand, null),
        KeymapActions.Refresh => (RefreshCommand, null),
        KeymapActions.OpenSettings => (SettingsCommand, null),
        KeymapActions.ToggleInfoPanel => (ToggleInfoPanelCommand, null),
        KeymapActions.Cut => (CutSelectionCommand, null),
        KeymapActions.Copy => (CopySelectionCommand, null),
        KeymapActions.Paste => (PasteCommand, null),
        KeymapActions.PasteWithoutReplace => (PasteWithoutReplaceCommand, null),
        KeymapActions.PasteWithReplace => (PasteWithReplaceCommand, null),
        KeymapActions.Rename => (RenameSelectionCommand, null),
        KeymapActions.MoveToTrash => (TrashSelectionCommand, null),
        KeymapActions.Properties => (ShowSelectionPropertiesCommand, null),
        KeymapActions.NewFolder => (NewFolderCommand, null),
        KeymapActions.NewTextDocument => (NewTextDocumentCommand, null),
        KeymapActions.SelectAll => (SelectAllCommand, null),
        KeymapActions.SelectNone => (SelectNoneCommand, null),
        KeymapActions.InvertSelection => (InvertSelectionCommand, null),
        KeymapActions.EditPath => (AddressBar.BeginEditCommand, null),
        KeymapActions.GoBack => (GoBackCommand, null),
        KeymapActions.GoForward => (GoForwardCommand, null),
        KeymapActions.GoUp => (GoUpCommand, null),
        KeymapActions.GoComputer => (GoComputerCommand, null),
        KeymapActions.GridView => (ShowGridViewCommand, null),
        KeymapActions.ListView => (ShowListViewCommand, null),
        KeymapActions.TreeView => (ShowTreeViewCommand, null),
        KeymapActions.ToggleSplitView => (ToggleSplitViewCommand, null),
        KeymapActions.NewTab => (NewTabCommand, null),
        KeymapActions.CloseTab => (CloseTabCommand, null),
        KeymapActions.NextTab => (NextTabCommand, null),
        KeymapActions.PreviousTab => (PreviousTabCommand, null),
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
        var reloadRecent = false;
        var reloadTrash = false;

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

        // Settings → File history filters what the Recent page lists
        if (settings.Advanced!.FileHistoryDays != previous.Advanced!.FileHistoryDays
            || settings.Advanced.ShowRecentFolders != previous.Advanced.ShowRecentFolders)
            reloadRecent = true;

        // Settings → Trash: whether the Trash page lists the trash folders of other drives too
        if (settings.Advanced.EmptyTrashOnAllDrives != previous.Advanced.EmptyTrashOnAllDrives)
            reloadTrash = true;

        // Only a change of the default itself: Ctrl+1/2/3 choices survive unrelated saves.
        // A new default applies to every view of every tab.
        if (settings.Workspace.DefaultView != previous.Workspace.DefaultView)
        {
            var mode = ToViewMode(settings.Workspace.DefaultView);
            foreach (var pane in AllPanes)
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

        // Saved by another window: this one follows. Its own saves come back as the same instance (SaveColumns)
        if (!ReferenceEquals(settings.Columns, previous.Columns))
            ApplyColumns(settings.Columns!);

        if (reloadFolders || reloadComputer || reloadRecent || reloadTrash)
            RefreshPanes(page => (reloadFolders && page is DirectoryViewModel)
                                 || (reloadComputer && page is ComputerViewModel)
                                 || (reloadRecent && page.Location == Locations.Recent)
                                 || (reloadTrash && page.Location == Locations.Trash));
    }

    /// <summary>An item was dragged to a new place on the sidebar: store the order.</summary>
    private void OnSidebarOrderChanged(object? sender, IReadOnlyList<string> order)
    {
        var current = _settings.Current;
        var sidebar = current.Sidebar! with { ItemOrder = order };

        // The sidebar already shows this order: applying the same instance first keeps
        // OnSettingsChanged from rebuilding the sidebar (and losing the highlight) after the save
        _appliedSettings = _appliedSettings with { Sidebar = sidebar };
        _ = SaveSettingsAsync(current with { Sidebar = sidebar });
    }

    /// <summary>A folder was dropped on the sidebar, or removed from "Favorites": store the list.</summary>
    private void OnSidebarFavoritesChanged(object? sender, IReadOnlyList<string> favorites)
    {
        var current = _settings.Current;

        // Stored together with the list, so a rename or a removal is one save
        var names = Sidebar.FavoriteNames;
        var sidebar = current.Sidebar! with { Favorites = favorites, FavoriteNames = names.Count == 0 ? null : names };

        // As for the order: the sidebar already shows these favorites, don't rebuild it after the save
        _appliedSettings = _appliedSettings with { Sidebar = sidebar };
        _ = SaveSettingsAsync(current with { Sidebar = sidebar });
    }

    private void ApplyColumns(ColumnSettings columns)
    {
        _applyingColumns = true;
        try
        {
            _columns.Apply(columns);
        }
        finally
        {
            _applyingColumns = false;
        }
    }

    /// <summary>A column was shown, hidden, moved or resized: store it once the changes stop.</summary>
    private void OnColumnsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_applyingColumns)
            return;

        _pendingColumnsSave?.Dispose();
        _pendingColumnsSave = DispatcherTimer.RunOnce(SaveColumns, ColumnsSaveDelay);
    }

    private void SaveColumns()
    {
        _pendingColumnsSave?.Dispose();
        _pendingColumnsSave = null;

        var columns = _columns.ToSettings();

        // The columns already show this: applying the same instance first keeps OnSettingsChanged
        // from applying it again after the save
        _appliedSettings = _appliedSettings with { Columns = columns };
        _ = SaveSettingsAsync(_settings.Current with { Columns = columns });
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
        _columns.PropertyChanged -= OnColumnsChanged;
        Toolbar.Dispose();

        // The window closes right after a change: store it now
        if (_pendingColumnsSave is not null)
            SaveColumns();

        foreach (var tab in Tabs)
        {
            tab.ActivePageChanged -= OnTabActivePageChanged;
            tab.SelectRequested -= OnTabSelectRequested;
            tab.CloseRequested -= OnTabCloseRequested;
            tab.Dispose();
        }

        base.OnDispose();
    }

    /// <summary>The views of every tab, hidden ones included: settings apply to all of them.</summary>
    private IEnumerable<PaneViewModel> AllPanes => Tabs.SelectMany(tab => tab.Panes);

    private TabViewModel CreateTab(string location, DirectoryViewMode viewMode)
    {
        var tab = new TabViewModel(location, viewMode, CreatePane);
        tab.ActivePageChanged += OnTabActivePageChanged;
        tab.SelectRequested += OnTabSelectRequested;
        tab.CloseRequested += OnTabCloseRequested;
        return tab;
    }

    private PaneViewModel CreatePane(string location, DirectoryViewMode viewMode)
        => new(location, viewMode, CreatePage);

    /// <summary>Only the selected tab drives the shell; the others keep their own paths.</summary>
    private void OnTabActivePageChanged(object? sender, EventArgs e)
    {
        if (ReferenceEquals(sender, _activeTab))
            SyncWithActivePane();
    }

    private void OnTabSelectRequested(object? sender, EventArgs e)
    {
        if (sender is TabViewModel tab)
            SelectTab(tab);
    }

    private void OnTabCloseRequested(object? sender, EventArgs e)
    {
        if (sender is TabViewModel tab)
            RemoveTab(tab);
    }

    /// <summary>After tabs were added or removed: the tab bar, the close buttons and the tab commands follow the count.</summary>
    private void UpdateTabStates()
    {
        var canClose = HasMultipleTabs;
        foreach (var tab in Tabs)
            tab.CanClose = canClose;

        OnPropertyChanged(nameof(HasMultipleTabs));
        CloseTabCommand.NotifyCanExecuteChanged();
        NextTabCommand.NotifyCanExecuteChanged();
        PreviousTabCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Points the address bar, sidebar and title bar buttons at the active view of the selected tab.
    /// The tab attaches its own info panel.
    /// </summary>
    private void SyncWithActivePane()
    {
        var page = CurrentPage;
        Sidebar.Select(page.Location);
        AddressBar.Update(page.Location);

        OnPropertyChanged(nameof(ActivePane));
        OnPropertyChanged(nameof(InfoPanel));
        OnPropertyChanged(nameof(IsSplit));
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
        SelectAllCommand.NotifyCanExecuteChanged();
        SelectNoneCommand.NotifyCanExecuteChanged();
        InvertSelectionCommand.NotifyCanExecuteChanged();

        // The file buttons of the title bar follow the active view and its selection (MainViewModel.FileActions.cs)
        UpdateFileActions();
    }

    private void NotifyViewModeChanged()
    {
        OnPropertyChanged(nameof(IsGridView));
        OnPropertyChanged(nameof(IsListView));
        OnPropertyChanged(nameof(IsTreeView));
    }

    /// <summary>Rebuilds the page of every view of every tab whose page matches, e.g. all folders after Show hidden files.</summary>
    private void RefreshPanes(Func<PageViewModel, bool> needsRefresh)
    {
        foreach (var pane in AllPanes.ToList())
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
            {
                // Read on every visit, so the list is never stale
                var history = _appliedSettings.Advanced!;
                var recent = Watch(DirectoryViewModel.ForRecent(pane.ViewMode, CurrentFolderOptions(), _columns, pane,
                    history.FileHistoryDays, history.ShowRecentFolders));
                _ = recent.LoadAsync(); // never throws, reports errors through Error
                return recent;
            }
            case Locations.Trash:
            {
                // Read on every visit, like Recent: what's in the trash folders Empty trash would empty
                var trash = Watch(DirectoryViewModel.ForTrash(pane.ViewMode, FileSort.Of(SortMode), CurrentFolderOptions(),
                    _columns, pane, _appliedSettings.Advanced!.EmptyTrashOnAllDrives));
                _ = trash.LoadAsync(); // never throws, reports errors through Error
                return trash;
            }
            case Locations.Network:
                // Read on every visit: what is mounted now, and a new search of the network
                return new NetworkViewModel(pane, _dialogService);
        }

        var directory = Watch(new DirectoryViewModel(location, pane.ViewMode, FileSort.Of(SortMode), CurrentFolderOptions(), _columns, pane));
        _ = directory.LoadAsync(); // never throws, reports errors through Error
        return directory;
    }
}
