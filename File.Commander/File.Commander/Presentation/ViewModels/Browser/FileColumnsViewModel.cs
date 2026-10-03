using CommunityToolkit.Mvvm.ComponentModel;

namespace File.Commander.Presentation.ViewModels.Browser;

/// <summary>
/// Which detail columns the list and tree show (Name is always shown). One instance is shared
/// by every folder page, so the choice survives navigation. Toggled from the header's context menu.
/// </summary>
public sealed partial class FileColumnsViewModel : ObservableObject
{
    [ObservableProperty]
    public partial bool ShowSize { get; set; } = true;

    [ObservableProperty]
    public partial bool ShowType { get; set; } = true;

    [ObservableProperty]
    public partial bool ShowModified { get; set; } = true;

    [ObservableProperty]
    public partial bool ShowCreated { get; set; } = true;

    [ObservableProperty]
    public partial bool ShowAccessed { get; set; } = true;

    [ObservableProperty]
    public partial bool ShowPermissions { get; set; } = true;
}
