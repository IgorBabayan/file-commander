using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using File.Commander.Application.Settings;

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

    // Changing these is not a settings change
    private static readonly HashSet<string> NotSettings =
        [nameof(SelectedNavItem), nameof(SaveError), nameof(HasSaveError)];

    private readonly ISettingsService _settings;
    private readonly bool _loaded;

    public SettingsViewModel(ISettingsService settings)
    {
        _settings = settings;
        var current = settings.Current;

        var basic = current.Basic;
        AlwaysOpenFolderInNewWindow = basic.AlwaysOpenFolderInNewWindow;
        OpenFile = Pick(OpenFileChoices, basic.OpenFile);
        StartLocation = Pick(StartLocationChoices, basic.StartLocation);
        NewTabLocation = Pick(NewTabChoices, basic.NewTabLocation);
        ShowHiddenFiles = basic.ShowHiddenFiles;
        ShowFileExtensions = basic.ShowFileExtensions;
        MixFilesAndFolders = basic.MixFilesAndFolders;

        var sidebar = current.Sidebar;
        ShowRecent = sidebar.ShowRecent;
        ShowHome = sidebar.ShowHome;
        ShowUserFolders = sidebar.ShowUserFolders;
        ShowTrash = sidebar.ShowTrash;
        ShowComputer = sidebar.ShowComputer;
        ShowPartitions = sidebar.ShowPartitions;
        ShowNetwork = sidebar.ShowNetwork;

        var workspace = current.Workspace;
        DefaultView = Pick(ViewChoices, workspace.DefaultView);
        PreviewImages = workspace.PreviewImages;
        PreviewVideos = workspace.PreviewVideos;
        PreviewText = workspace.PreviewText;
        PreviewDocuments = workspace.PreviewDocuments;
        HideSystemDisk = workspace.HideSystemDisk;
        ShowFileSystemOnDisks = workspace.ShowFileSystemOnDisks;

        var advanced = current.Advanced;
        FullTextSearch = advanced.FullTextSearch;
        IndexExternalDrives = advanced.IndexExternalDrives;
        AutoMount = advanced.AutoMount;
        OpenAfterAutoMount = advanced.OpenAfterAutoMount;
        ConfirmPermanentDelete = advanced.ConfirmPermanentDelete;
        ShowOperationProgress = advanced.ShowOperationProgress;

        SelectedNavItem = NavItems[0];
        _loaded = true;
    }

    public IReadOnlyList<SettingsNavItem> NavItems { get; } =
    [
        new SettingsNavGroup("basic", "Basic"),
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
    ];

    /// <summary>Picked in the navigation (scrolls the content) or updated while the content scrolls.</summary>
    [ObservableProperty]
    public partial SettingsNavItem? SelectedNavItem { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSaveError))]
    public partial string? SaveError { get; set; }

    public bool HasSaveError => SaveError is not null;

    public IReadOnlyList<Choice<OpenFileMode>> OpenFileOptions => OpenFileChoices;
    public IReadOnlyList<Choice<StartLocation>> StartLocationOptions => StartLocationChoices;
    public IReadOnlyList<Choice<NewTabLocation>> NewTabOptions => NewTabChoices;
    public IReadOnlyList<Choice<FolderViewMode>> ViewOptions => ViewChoices;

    // ===== Basic =====
    [ObservableProperty] public partial bool AlwaysOpenFolderInNewWindow { get; set; }
    [ObservableProperty] public partial Choice<OpenFileMode> OpenFile { get; set; } = OpenFileChoices[1];
    [ObservableProperty] public partial Choice<StartLocation> StartLocation { get; set; } = StartLocationChoices[0];
    [ObservableProperty] public partial Choice<NewTabLocation> NewTabLocation { get; set; } = NewTabChoices[0];
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
    [ObservableProperty] public partial Choice<FolderViewMode> DefaultView { get; set; } = ViewChoices[1];
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

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (_loaded && e.PropertyName is { } name && !NotSettings.Contains(name))
            _ = SaveAsync();
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

    // Starts from Current so fields this window doesn't edit (addons, log folder…) are kept
    private AppSettings Snapshot() => _settings.Current with
    {
        Basic = new BasicSettings
        {
            AlwaysOpenFolderInNewWindow = AlwaysOpenFolderInNewWindow,
            OpenFile = OpenFile.Value,
            StartLocation = StartLocation.Value,
            NewTabLocation = NewTabLocation.Value,
            ShowHiddenFiles = ShowHiddenFiles,
            ShowFileExtensions = ShowFileExtensions,
            MixFilesAndFolders = MixFilesAndFolders,
        },
        Sidebar = new SidebarSettings
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
        },
    };

    private static Choice<T> Pick<T>(Choice<T>[] choices, T value)
        => choices.FirstOrDefault(c => EqualityComparer<T>.Default.Equals(c.Value, value)) ?? choices[0];
}
