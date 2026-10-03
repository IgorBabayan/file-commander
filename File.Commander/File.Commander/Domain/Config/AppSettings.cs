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
    public Dictionary<string, bool> Addons { get; set; } = new();

    [JsonPropertyName(nameof(AutoUpdate))]
    public bool AutoUpdate { get; set; }

    [JsonPropertyName(nameof(Basic))]
    public BasicSettings Basic { get; set; } = new();

    [JsonPropertyName(nameof(Sidebar))]
    public SidebarSettings Sidebar { get; set; } = new();

    [JsonPropertyName(nameof(Workspace))]
    public WorkspaceSettings Workspace { get; set; } = new();

    [JsonPropertyName(nameof(Advanced))]
    public AdvancedSettings Advanced { get; set; } = new();

    public bool IsAddonEnabled(string key) => !Addons.TryGetValue(key, out var enabled) || enabled;
}

/// <summary>Settings → Basic.</summary>
public sealed record BasicSettings
{
    public bool AlwaysOpenFolderInNewWindow { get; init; }
    public OpenFileMode OpenFile { get; init; } = OpenFileMode.DoubleClick;

    /// <summary>What a new window shows.</summary>
    public StartLocation StartLocation { get; init; } = StartLocation.Computer;

    public NewTabLocation NewTabLocation { get; init; } = NewTabLocation.CurrentDirectory;
    public bool ShowHiddenFiles { get; init; }
    public bool ShowFileExtensions { get; init; } = true;

    /// <summary>False: folders first, whatever the order.</summary>
    public bool MixFilesAndFolders { get; init; }
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
