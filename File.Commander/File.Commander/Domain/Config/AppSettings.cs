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

    public bool IsAddonEnabled(string key) => !Addons!.TryGetValue(key, out var enabled) || enabled;
}

/// <summary>Settings → Basic.</summary>
public sealed record BasicSettings
{
    /// <summary>The color theme. Applied to every window as soon as it changes.</summary>
    public AppTheme Theme { get; init; } = AppTheme.Mocha;

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
    public bool FullTextSearch { get; init; }
    public bool IndexExternalDrives { get; init; }

    public bool AutoMount { get; init; } = true;
    public bool OpenAfterAutoMount { get; init; }

    public bool ConfirmPermanentDelete { get; init; } = true;
    public bool ShowOperationProgress { get; init; } = true;
}
