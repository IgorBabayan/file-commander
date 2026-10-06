using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace File.Commander.Presentation.ViewModels.Settings;

/// <summary>A drop-down option. The ComboBox shows <see cref="ToString"/>.</summary>
public sealed record Choice<T>(T Value, string Title)
{
    public override string ToString() => Title;
}

/// <summary>An entry of the left navigation. <see cref="Key"/> is the Tag of its heading in the content.</summary>
public abstract record SettingsNavItem(string Key, string Title);

public sealed record SettingsNavGroup(string Key, string Title) : SettingsNavItem(Key, Title);

public sealed record SettingsNavSection(string Key, string Title) : SettingsNavItem(Key, Title);

/// <summary>
/// The Settings window. There is no Save button: every change is written at once.
/// One instance per opening.
/// </summary>
public sealed partial class SettingsViewModel : ViewModelBase
{
    private static readonly Choice<OpenFileMode>[] OpenFileChoices =
    [
        new(OpenFileMode.Click, "Click"),
        new(OpenFileMode.DoubleClick, "Double click"),
    ];

    private static readonly Choice<StartLocation>[] StartLocationChoices =
    [
        new(Domain.Config.StartLocation.Computer, "Computer"),
        new(Domain.Config.StartLocation.Home, "Home"),
        new(Domain.Config.StartLocation.Recent, "Recent"),
    ];

    private static readonly Choice<NewTabLocation>[] NewTabChoices =
    [
        new(Domain.Config.NewTabLocation.CurrentDirectory, "Current Directory"),
        new(Domain.Config.NewTabLocation.Computer, "Computer"),
        new(Domain.Config.NewTabLocation.Home, "Home"),
    ];

    private static readonly Choice<FolderViewMode>[] ViewChoices =
    [
        new(FolderViewMode.Grid, "Icons"),
        new(FolderViewMode.List, "List"),
        new(FolderViewMode.Tree, "Tree"),
    ];

    private static readonly Choice<int>[] FileHistoryChoices =
    [
        new(0, "Any time"),
        new(1, "Today"),
        new(7, "1 week"),
        new(30, "1 month"),
        new(90, "3 months"),
        new(365, "1 year"),
    ];

    /// <summary>Navigation keys the window can be opened at, e.g. from the sidebar's context menu.</summary>
    public const string FileHistorySection = "file-history";
    public const string TrashSection = "trash";
    public const string PluginsGroup = "plugins";
    public const string PluginsSection = "plugins-list";

    private static readonly IReadOnlyList<SettingsNavItem> BuiltInNavItems =
    [
        new SettingsNavGroup("basic", "Basic"),
        new SettingsNavSection("appearance", "Appearance"),
        new SettingsNavSection("keymap", "Keymap"),
        new SettingsNavSection("open-behavior", "Open behavior"),
        new SettingsNavSection("new-window-tab", "New window and tab"),
        new SettingsNavSection("files-folders", "Files and folders"),
        new SettingsNavGroup("sidebar", "Sidebar"),
        new SettingsNavSection("sidebar-items", "Items on sidebar"),
        new SettingsNavGroup("workspace", "Workspace"),
        new SettingsNavSection("view", "View"),
        new SettingsNavSection("thumbnails", "Thumbnail preview"),
        new SettingsNavSection("computer-display", "Computer display"),
        new SettingsNavGroup("advanced", "Advanced"),
        new SettingsNavSection("search", "Search"),
        new SettingsNavSection("mount", "Mount"),
        new SettingsNavSection("dialog", "Dialog"),
        new SettingsNavSection(FileHistorySection, "File history"),
        new SettingsNavSection(TrashSection, "Trash"),
        new SettingsNavGroup(PluginsGroup, "Plugins"),
        new SettingsNavSection(PluginsSection, "Plugins list"),
    ];

    // Changing these is not a settings change
    private static readonly HashSet<string> NotSettings =
        [nameof(SelectedNavItem), nameof(SaveError), nameof(HasSaveError), nameof(ThemeOptions)];

    private readonly ISettingsService _settings;
    private readonly IThemeCatalog _themes;
    private readonly bool _loaded;

    // Selected in the Theme drop-down. Never null: a drop-down whose items are replaced writes null, which is ignored
    private Choice<ThemeOption> _theme;
    private IReadOnlyList<Choice<ThemeOption>> _themeOptions;

    public SettingsViewModel(ISettingsService settings, IThemeCatalog themes, IPluginCatalog plugins,
        IPluginRegistry pluginRegistry, IPluginUninstaller pluginUninstaller, IDialogService dialogs,
        string? section = null)
    {
        _settings = settings;
        _themes = themes;
        var current = settings.Current;

        var basic = current.Basic!;
        _themeOptions = BuildThemeOptions();
        _theme = PickTheme(_themeOptions, themes.KeyOf(basic));
        Keymap = new KeymapSettingsViewModel(current.Keymap, () => _ = SaveAsync());
        Plugins = new PluginsSettingsViewModel(plugins, pluginRegistry, pluginUninstaller, dialogs, current,
            () => _ = SaveAsync(), RefreshThemeOptions);
        AlwaysOpenFolderInNewWindow = basic.AlwaysOpenFolderInNewWindow;
        OpenFile = Pick(OpenFileChoices, basic.OpenFile);
        StartLocation = Pick(StartLocationChoices, basic.StartLocation);
        NewTabLocation = Pick(NewTabChoices, basic.NewTabLocation);
        ShowHiddenFiles = basic.ShowHiddenFiles;
        ShowFileExtensions = basic.ShowFileExtensions;
        MixFilesAndFolders = basic.MixFilesAndFolders;

        var sidebar = current.Sidebar!;
        ShowRecent = sidebar.ShowRecent;
        ShowHome = sidebar.ShowHome;
        ShowUserFolders = sidebar.ShowUserFolders;
        ShowTrash = sidebar.ShowTrash;
        ShowComputer = sidebar.ShowComputer;
        ShowPartitions = sidebar.ShowPartitions;
        ShowNetwork = sidebar.ShowNetwork;

        var workspace = current.Workspace!;
        DefaultView = Pick(ViewChoices, workspace.DefaultView);
        PreviewImages = workspace.PreviewImages;
        PreviewVideos = workspace.PreviewVideos;
        PreviewText = workspace.PreviewText;
        PreviewDocuments = workspace.PreviewDocuments;
        HideSystemDisk = workspace.HideSystemDisk;
        ShowFileSystemOnDisks = workspace.ShowFileSystemOnDisks;

        var advanced = current.Advanced!;
        FullTextSearch = advanced.FullTextSearch;
        IndexExternalDrives = advanced.IndexExternalDrives;
        AutoMount = advanced.AutoMount;
        OpenAfterAutoMount = advanced.OpenAfterAutoMount;
        ConfirmPermanentDelete = advanced.ConfirmPermanentDelete;
        ShowOperationProgress = advanced.ShowOperationProgress;
        ShowRecentFolders = advanced.ShowRecentFolders;
        FileHistoryDays = Pick(FileHistoryChoices, advanced.FileHistoryDays);
        ConfirmEmptyTrash = advanced.ConfirmEmptyTrash;
        EmptyTrashOnAllDrives = advanced.EmptyTrashOnAllDrives;

        // The plugins' own sections come last, under Plugins list
        NavItems = [.. BuiltInNavItems, .. Plugins.Sections.Select(s => new SettingsNavSection(s.NavKey, s.Title))];

        InitialNavItem = section is null ? null : NavItems.FirstOrDefault(item => item.Key == section);
        SelectedNavItem = InitialNavItem ?? NavItems[0];
        _loaded = true;
    }

    /// <summary>The entry the window was asked to open at. The window scrolls to it once laid out.</summary>
    public SettingsNavItem? InitialNavItem { get; }

    public IReadOnlyList<SettingsNavItem> NavItems { get; }

    /// <summary>Picked in the navigation (scrolls the content) or updated while the content scrolls.</summary>
    [ObservableProperty]
    public partial SettingsNavItem? SelectedNavItem { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSaveError))]
    public partial string? SaveError { get; set; }

    public bool HasSaveError => SaveError is not null;

    /// <summary>The built-in themes, then those the loaded plugins add.</summary>
    public IReadOnlyList<Choice<ThemeOption>> ThemeOptions
    {
        get => _themeOptions;
        private set => SetProperty(ref _themeOptions, value);
    }

    public IReadOnlyList<Choice<OpenFileMode>> OpenFileOptions => OpenFileChoices;
    public IReadOnlyList<Choice<StartLocation>> StartLocationOptions => StartLocationChoices;
    public IReadOnlyList<Choice<NewTabLocation>> NewTabOptions => NewTabChoices;
    public IReadOnlyList<Choice<FolderViewMode>> ViewOptions => ViewChoices;
    public IReadOnlyList<Choice<int>> FileHistoryOptions => FileHistoryChoices;

    // ===== Basic =====
    public Choice<ThemeOption>? Theme
    {
        get => _theme;
        set
        {
            if (value is not null)
                SetProperty(ref _theme, value);
        }
    }

    /// <summary>Saves through <see cref="SaveAsync"/> itself: its changes aren't properties of this view model.</summary>
    public KeymapSettingsViewModel Keymap { get; }

    [ObservableProperty] public partial bool AlwaysOpenFolderInNewWindow { get; set; }
    [ObservableProperty] public partial Choice<OpenFileMode> OpenFile { get; set; }
    [ObservableProperty] public partial Choice<StartLocation> StartLocation { get; set; }
    [ObservableProperty] public partial Choice<NewTabLocation> NewTabLocation { get; set; }
    [ObservableProperty] public partial bool ShowHiddenFiles { get; set; }
    [ObservableProperty] public partial bool ShowFileExtensions { get; set; }
    [ObservableProperty] public partial bool MixFilesAndFolders { get; set; }

    // ===== Sidebar =====
    [ObservableProperty] public partial bool ShowRecent { get; set; }
    [ObservableProperty] public partial bool ShowHome { get; set; }
    [ObservableProperty] public partial bool ShowUserFolders { get; set; }
    [ObservableProperty] public partial bool ShowTrash { get; set; }
    [ObservableProperty] public partial bool ShowComputer { get; set; }
    [ObservableProperty] public partial bool ShowPartitions { get; set; }
    [ObservableProperty] public partial bool ShowNetwork { get; set; }

    // ===== Workspace =====
    [ObservableProperty] public partial Choice<FolderViewMode> DefaultView { get; set; }
    [ObservableProperty] public partial bool PreviewImages { get; set; }
    [ObservableProperty] public partial bool PreviewVideos { get; set; }
    [ObservableProperty] public partial bool PreviewText { get; set; }
    [ObservableProperty] public partial bool PreviewDocuments { get; set; }
    [ObservableProperty] public partial bool HideSystemDisk { get; set; }
    [ObservableProperty] public partial bool ShowFileSystemOnDisks { get; set; }

    // ===== Advanced =====
    [ObservableProperty] public partial bool FullTextSearch { get; set; }
    [ObservableProperty] public partial bool IndexExternalDrives { get; set; }
    [ObservableProperty] public partial bool AutoMount { get; set; }
    [ObservableProperty] public partial bool OpenAfterAutoMount { get; set; }
    [ObservableProperty] public partial bool ConfirmPermanentDelete { get; set; }
    [ObservableProperty] public partial bool ShowOperationProgress { get; set; }
    [ObservableProperty] public partial bool ShowRecentFolders { get; set; }
    [ObservableProperty] public partial Choice<int> FileHistoryDays { get; set; }
    [ObservableProperty] public partial bool ConfirmEmptyTrash { get; set; }
    [ObservableProperty] public partial bool EmptyTrashOnAllDrives { get; set; }

    // ===== Plugins =====
    /// <summary>Saves through <see cref="SaveAsync"/> itself, like <see cref="Keymap"/>.</summary>
    public PluginsSettingsViewModel Plugins { get; }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (_loaded && e.PropertyName is { } name && !NotSettings.Contains(name))
            _ = SaveAsync();
    }

    protected override void OnDispose()
    {
        Plugins.Dispose();
        base.OnDispose();
    }

    /// <summary>
    /// A plugin's settings were saved: the themes it offers may have changed. The selection stays when its theme is
    /// still there; otherwise the drop-down falls back to the built-in theme, which is saved like a pick.
    /// </summary>
    private void RefreshThemeOptions()
    {
        var options = BuildThemeOptions();
        var key = _theme.Value.Key;
        ThemeOptions = options;

        if (options.FirstOrDefault(option => option.Value.Key == key) is { } same)
        {
            // Not a change: only puts the drop-down's selection back after its items were replaced
            _theme = same;
            base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(Theme)));
            return;
        }

        Theme = PickTheme(options, _settings.Current.Basic!.Theme.ToString());
    }

    private IReadOnlyList<Choice<ThemeOption>> BuildThemeOptions()
        => _themes.Options.Select(option => new Choice<ThemeOption>(option, option.Title)).ToList();

    private static Choice<ThemeOption> PickTheme(IReadOnlyList<Choice<ThemeOption>> choices, string key)
        => choices.FirstOrDefault(choice => choice.Value.Key == key)
           ?? choices.FirstOrDefault(choice => choice.Value.BuiltIn == AppTheme.Mocha)
           ?? choices[0];

    /// <summary>Puts the sidebar items back in the built-in order. Visibility is kept.</summary>
    [RelayCommand]
    private async Task ResetSidebarOrder()
    {
        var current = _settings.Current;
        if (current.Sidebar!.ItemOrder is null)
            return;

        try
        {
            await _settings.SaveAsync(current with { Sidebar = current.Sidebar! with { ItemOrder = null } });
            SaveError = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.WriteLine($"Can't save settings: {ex}");
            SaveError = $"Settings weren't saved: {ex.Message}";
        }
    }

    private async Task SaveAsync()
    {
        try
        {
            await _settings.SaveAsync(Snapshot());
            SaveError = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.WriteLine($"Can't save settings: {ex}");
            SaveError = $"Settings weren't saved: {ex.Message}";
        }
    }

    // Starts from Current so fields this window doesn't edit (log folder, columns…) are kept
    private AppSettings Snapshot() => _settings.Current with
    {
        Addons = Plugins.ToAddons(_settings.Current.Addons),
        // Copied, not rebuilt: Basic also holds the sort menu's order, which this window doesn't edit
        Basic = _settings.Current.Basic! with
        {
            // A plugin theme keeps the last built-in one as the fallback for when its plugin is gone
            Theme = _theme.Value.BuiltIn ?? _settings.Current.Basic!.Theme,
            PluginThemeKey = _theme.Value.BuiltIn is null ? _theme.Value.Key : null,
            AlwaysOpenFolderInNewWindow = AlwaysOpenFolderInNewWindow,
            OpenFile = OpenFile.Value,
            StartLocation = StartLocation.Value,
            NewTabLocation = NewTabLocation.Value,
            ShowHiddenFiles = ShowHiddenFiles,
            ShowFileExtensions = ShowFileExtensions,
            MixFilesAndFolders = MixFilesAndFolders,
        },
        // Copied, not rebuilt: Sidebar also holds the order dragged on the sidebar
        Sidebar = _settings.Current.Sidebar! with
        {
            ShowRecent = ShowRecent,
            ShowHome = ShowHome,
            ShowUserFolders = ShowUserFolders,
            ShowTrash = ShowTrash,
            ShowComputer = ShowComputer,
            ShowPartitions = ShowPartitions,
            ShowNetwork = ShowNetwork,
        },
        Workspace = new WorkspaceSettings
        {
            DefaultView = DefaultView.Value,
            PreviewImages = PreviewImages,
            PreviewVideos = PreviewVideos,
            PreviewText = PreviewText,
            PreviewDocuments = PreviewDocuments,
            HideSystemDisk = HideSystemDisk,
            ShowFileSystemOnDisks = ShowFileSystemOnDisks,
        },
        Advanced = new AdvancedSettings
        {
            FullTextSearch = FullTextSearch,
            IndexExternalDrives = IndexExternalDrives,
            AutoMount = AutoMount,
            OpenAfterAutoMount = OpenAfterAutoMount,
            ConfirmPermanentDelete = ConfirmPermanentDelete,
            ShowOperationProgress = ShowOperationProgress,
            ShowRecentFolders = ShowRecentFolders,
            FileHistoryDays = FileHistoryDays.Value,
            ConfirmEmptyTrash = ConfirmEmptyTrash,
            EmptyTrashOnAllDrives = EmptyTrashOnAllDrives,
        },
        Keymap = this.Keymap.ToOverrides(),
    };

    private static Choice<T> Pick<T>(Choice<T>[] choices, T value)
        => choices.FirstOrDefault(c => EqualityComparer<T>.Default.Equals(c.Value, value)) ?? choices[0];
}
