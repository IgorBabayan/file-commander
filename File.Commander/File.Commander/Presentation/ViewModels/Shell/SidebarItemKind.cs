namespace File.Commander.Presentation.ViewModels.Shell;

/// <summary>Decides which context menu a sidebar item gets.</summary>
public enum SidebarItemKind
{
    /// <summary>Home, user folders, partitions, Computer, Network: Open… and Properties.</summary>
    Regular,

    /// <summary>Open… and File history settings.</summary>
    Recent,

    /// <summary>Open…, Trash settings, Empty trash and Properties.</summary>
    Trash,

    /// <summary>A pinned folder: like <see cref="Regular"/>, plus Rename and Remove from favorites.</summary>
    Favorite,
}
