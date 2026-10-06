using CommunityToolkit.Mvvm.Input;

namespace File.Commander.Presentation.ViewModels.Shell;

/// <summary>
/// Search (Ctrl+F): the bar under the Search button of the title bar, or a dialog while the button isn't on the
/// toolbar. The results open in the active view as a page of their own (search://…, see <see cref="SearchQuery"/>),
/// with the same layouts, menus and file actions as a folder.
/// </summary>
public partial class MainViewModel
{
    /// <summary>
    /// Ctrl+F: the window should open the bar under the Search button, or, when the button isn't on the toolbar,
    /// call <see cref="ShowSearchDialogAsync"/>.
    /// </summary>
    public event EventHandler? SearchShowRequested;

    /// <summary>Ctrl+F by default. Not while the toolbar is being customized: its buttons are only something to drag then.</summary>
    [RelayCommand]
    private void OpenSearch()
    {
        if (!Toolbar.IsCustomizing)
            SearchShowRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The search bar as a dialog, looking in the active view's folder.</summary>
    public async Task ShowSearchDialogAsync()
    {
        Search.Prepare();
        await _dialogService.ShowDialogAsync<SearchViewModel, bool>(Search);
    }

    /// <summary>Search in the bar or the dialog: shows the results in the active view, as a new history entry.</summary>
    private void OnSearchRequested(object? sender, SearchQuery query)
    {
        var location = query.ToLocation();

        // The same search again (e.g. after the files changed): run it again in place
        if (Locations.AreEqual(location, ActivePane.Location))
            ActivePane.Refresh();
        else
            Navigate(location);
    }

    /// <summary>
    /// Settings → Search → Include external drives off: the mount points of removable and optical drives, so a search
    /// of / or Home doesn't wander into them. FileSearch still searches a drive when the search starts inside it.
    /// </summary>
    private IReadOnlyCollection<string> SearchSkippedFolders(SearchQuery query)
    {
        if (_appliedSettings.Advanced!.IndexExternalDrives)
            return [];

        return SystemLocations.GetVolumes()
            .Where(volume => volume.Kind is VolumeKind.Removable or VolumeKind.Optical)
            .Select(volume => volume.MountPoint)
            .Where(mountPoint => !FileOperations.IsSameOrInside(query.Folder, mountPoint))
            .ToList();
    }
}
