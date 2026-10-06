using System.Text.Json.Serialization;

namespace File.Commander.Domain.Config;

/// <summary>
/// Stored in settings.json. A record: change it with <c>with { }</c>, never mutate the instance
/// returned by <c>ISettingsService.Current</c>.
/// </summary>
public record AppSettings
{
    [JsonPropertyName(nameof(LogFolder))]
    public string? LogFolder { get; set; }

    [JsonPropertyName(nameof(Addons))]
    public Dictionary<string, bool>? Addons { get; set; } = new();

    [JsonPropertyName(nameof(AutoUpdate))]
    public bool AutoUpdate { get; set; }

    [JsonPropertyName(nameof(Basic))]
    public BasicSettings? Basic { get; set; } = new();

    [JsonPropertyName(nameof(Sidebar))]
    public SidebarSettings? Sidebar { get; set; } = new();

    [JsonPropertyName(nameof(Workspace))]
    public WorkspaceSettings? Workspace { get; set; } = new();

    [JsonPropertyName(nameof(Advanced))]
    public AdvancedSettings? Advanced { get; set; } = new();

    /// <summary>
    /// Settings → Basic → Keymap: action id → its shortcuts (e.g. "Ctrl+Shift+V"), replacing the defaults.
    /// Only changed actions are stored, so new defaults reach everyone else; an empty list unbinds the action.
    /// </summary>
    [JsonPropertyName(nameof(Keymap))]
    public Dictionary<string, List<string>>? Keymap { get; set; } = new();

    /// <summary>The detail columns of the list and tree, as set from their header (not from the Settings window).</summary>
    [JsonPropertyName(nameof(Columns))]
    public ColumnSettings? Columns { get; set; } = new();

    /// <summary>The title bar's items, as set with Customize Toolbar… (a right click on the title bar).</summary>
    [JsonPropertyName(nameof(Toolbar))]
    public ToolbarSettings? Toolbar { get; set; } = new();

    public bool IsAddonEnabled(string key) => !Addons!.TryGetValue(key, out var enabled) || enabled;
}

/// <summary>Settings → Basic.</summary>
public sealed record BasicSettings
{
    /// <summary>
    /// The built-in color theme. Applied to every window as soon as it changes. Also the fallback while
    /// <see cref="PluginThemeKey"/> is set but its plugin is off or gone.
    /// </summary>
    public AppTheme Theme { get; init; } = AppTheme.Mocha;

    /// <summary>
    /// A theme added by a plugin, as "&lt;plugin id&gt;/&lt;theme id&gt;". Wins over <see cref="Theme"/> while that plugin
    /// is loaded and still offers it. Null: the built-in <see cref="Theme"/>.
    /// </summary>
    public string? PluginThemeKey { get; init; }

    public bool AlwaysOpenFolderInNewWindow { get; init; }
    public OpenFileMode OpenFile { get; init; } = OpenFileMode.DoubleClick;

    /// <summary>What a new window shows.</summary>
    public StartLocation StartLocation { get; init; } = StartLocation.Computer;

    public NewTabLocation NewTabLocation { get; init; } = NewTabLocation.CurrentDirectory;
    public bool ShowHiddenFiles { get; init; }
    public bool ShowFileExtensions { get; init; } = true;

    /// <summary>False: folders first, whatever the order.</summary>
    public bool MixFilesAndFolders { get; init; }

    /// <summary>The sort menu's order, used by every folder opened. Header clicks aren't stored.</summary>
    public FolderSortOrder SortOrder { get; init; } = FolderSortOrder.Type;
}

/// <summary>
/// The detail columns of the list and tree: shown or not (the header's context menu), their order (dragged
/// headers) and their widths (dragged separators). Columns are named as in the header: "Size", "Type",
/// "Modified", "Created", "Accessed", "Permissions"; unknown names are ignored.
/// Replace the lists, never mutate them: the shell compares settings by reference.
/// </summary>
public sealed record ColumnSettings
{
    /// <summary>Columns turned off. Null or empty: every column is shown.</summary>
    public IReadOnlyList<string>? Hidden { get; init; }

    /// <summary>Columns left to right. Null: the built-in order. Columns that aren't listed go to the end.</summary>
    public IReadOnlyList<string>? Order { get; init; }

    /// <summary>Widths the user dragged columns to, by name. A column that isn't listed has its default width.</summary>
    public IReadOnlyDictionary<string, double>? Widths { get; init; }
}

/// <summary>Settings → Sidebar: which sidebar entries are shown.</summary>
public sealed record SidebarSettings
{
    public bool ShowRecent { get; init; } = true;
    public bool ShowHome { get; init; } = true;
    public bool ShowUserFolders { get; init; } = true;
    public bool ShowTrash { get; init; } = true;
    public bool ShowComputer { get; init; } = true;
    public bool ShowPartitions { get; init; } = true;
    public bool ShowNetwork { get; init; } = true;

    /// <summary>
    /// Locations of sidebar items in the order the user dragged them to. Null: the built-in order.
    /// Items that aren't listed (a new drive, a folder that came back) go to the end of their section.
    /// Replace the list, never mutate it: the shell compares settings by reference.
    /// </summary>
    public IReadOnlyList<string>? ItemOrder { get; init; }

    /// <summary>
    /// Folders the user dragged from a view onto the sidebar, shown under "Favorites". Null or empty: no section.
    /// Replace the list, never mutate it: the shell compares settings by reference.
    /// </summary>
    public IReadOnlyList<string>? Favorites { get; init; }

    /// <summary>
    /// Names given to favorites with Rename, by location. A favorite that isn't listed shows its folder's name.
    /// Replace the dictionary, never mutate it: the shell compares settings by reference.
    /// </summary>
    public IReadOnlyDictionary<string, string>? FavoriteNames { get; init; }
}

/// <summary>Settings → Workspace.</summary>
public sealed record WorkspaceSettings
{
    public FolderViewMode DefaultView { get; init; } = FolderViewMode.List;

    public bool PreviewImages { get; init; } = true;
    public bool PreviewVideos { get; init; } = true;
    public bool PreviewText { get; init; } = true;
    public bool PreviewDocuments { get; init; } = true;

    public bool HideSystemDisk { get; init; }
    public bool ShowFileSystemOnDisks { get; init; } = true;
}

/// <summary>Settings → Advanced.</summary>
public sealed record AdvancedSettings
{
    /// <summary>Settings → Search: the search bar's "File contents" option starts checked.</summary>
    public bool FullTextSearch { get; init; }

    /// <summary>Settings → Search: a search goes into removable and optical drives mounted below the folder searched.</summary>
    public bool IndexExternalDrives { get; init; }

    public bool AutoMount { get; init; } = true;
    public bool OpenAfterAutoMount { get; init; }

    /// <summary>Settings → Advanced → File history: Recent lists only what was used in this many days. 0: any time.</summary>
    public int FileHistoryDays { get; init; }

    /// <summary>Recent lists recently used folders too, not only files.</summary>
    public bool ShowRecentFolders { get; init; } = true;

    /// <summary>Settings → Advanced → Trash: Empty trash asks first.</summary>
    public bool ConfirmEmptyTrash { get; init; } = true;

    /// <summary>Empty trash also empties the trash folders of other drives ($topdir/.Trash-$uid).</summary>
    public bool EmptyTrashOnAllDrives { get; init; } = true;

    public bool ConfirmPermanentDelete { get; init; } = true;
    public bool ShowOperationProgress { get; init; } = true;
}
