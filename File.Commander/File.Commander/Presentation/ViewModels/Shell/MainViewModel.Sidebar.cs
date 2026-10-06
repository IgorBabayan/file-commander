namespace File.Commander.Presentation.ViewModels.Shell;

/// <summary>What the sidebar's context menu does. The menu itself is built by MainWindow.SidebarMenu.cs.</summary>
public partial class MainViewModel
{
    /// <summary>Open: what a click on the item does.</summary>
    public void OpenSidebarItem(SidebarItem item) => Navigate(item.Location);

    /// <summary>Open in split view: see <see cref="OpenInSplitView"/>.</summary>
    public void OpenSidebarItemInSplitView(SidebarItem item) => OpenInSplitView(item.Location);

    /// <summary>Open in new tab: see <see cref="OpenInNewTab"/>.</summary>
    public void OpenSidebarItemInNewTab(SidebarItem item) => OpenInNewTab(item.Location);

    /// <summary>Recent → File history settings.</summary>
    public Task OpenFileHistorySettingsAsync() => OpenSettingsAtAsync(SettingsViewModel.FileHistorySection);

    /// <summary>Trash → Trash settings.</summary>
    public Task OpenTrashSettingsAsync() => OpenSettingsAtAsync(SettingsViewModel.TrashSection);

    /// <summary>
    /// Whether Empty trash is enabled: something is in one of the trash folders it would empty,
    /// and the trash isn't already being emptied in the Action center.
    /// </summary>
    public bool CanEmptyTrash() => !IsEmptyingTrash
                                   && TrashBins.HasItems(TrashBins.Find(_appliedSettings.Advanced!.EmptyTrashOnAllDrives));

    private bool IsEmptyingTrash => ActionCenter.Operations.Any(operation =>
        operation is { IsRunning: true, Kind: OperationKind.EmptyTrash });

    /// <summary>
    /// Trash → Empty trash. Deletes everything in the trash for good, after asking (Settings → Trash),
    /// as an action in the Action center.
    /// </summary>
    public async Task EmptyTrashAsync()
    {
        var advanced = _appliedSettings.Advanced!;
        var bins = TrashBins.Find(advanced.EmptyTrashOnAllDrives);
        if (IsEmptyingTrash || !TrashBins.HasItems(bins))
            return;

        if (advanced.ConfirmEmptyTrash)
        {
            using var confirm = PromptViewModel.ForConfirmation(
                "Empty the trash?",
                "All items in the trash will be deleted permanently. This can't be undone.",
                "Empty trash",
                destructive: true);

            if (!await _dialogService.ShowDialogAsync<PromptViewModel, bool>(confirm))
                return;
        }

        // Runs in the Action center: progress there, Cancel stops before the next item, and items that
        // couldn't be deleted are listed there (the panel opens by itself when that happens)
        var details = bins.Count == 1 ? bins[0] : $"{bins[0]} and {bins.Count - 1} more";
        await ActionCenter.RunAsync(OperationKind.EmptyTrash, OperationTitles.EmptyTrash, details,
            progress => TrashBins.Empty(bins, progress));

        // A view showing the trash (or a folder inside a trash folder) would list what's gone
        RefreshPanes(page => page.Location == Locations.Trash
                             || bins.Any(bin => page.Location.StartsWith(bin, StringComparison.Ordinal)));
    }

    /// <summary>Properties: what the item is, where it is and how much it holds.</summary>
    public async Task ShowSidebarItemPropertiesAsync(SidebarItem item)
    {
        using var properties = PropertiesViewModel.For(item, _appliedSettings.Advanced!.EmptyTrashOnAllDrives);
        await _dialogService.ShowDialogAsync<PropertiesViewModel, bool>(properties);
    }

    /// <summary>Favorites → Rename. Only the name on the sidebar changes; an empty name goes back to the folder's.</summary>
    public async Task RenameFavoriteAsync(SidebarItem item)
    {
        using var prompt = PromptViewModel.ForInput(
            "Rename favorite",
            "The name shown on the sidebar. The folder itself keeps its name; leave it empty to use the folder's name.",
            item.Title,
            "Rename");

        // Saved through FavoritesChanged, like a drop or a removal
        if (await _dialogService.ShowDialogAsync<PromptViewModel, bool>(prompt))
            Sidebar.RenameFavorite(item, prompt.InputText);
    }

    /// <summary>Favorites → Remove from favorites. The folder itself is left alone.</summary>
    public void RemoveFavorite(SidebarItem item) => Sidebar.RemoveFavorite(item);

    private async Task OpenSettingsAtAsync(string section)
    {
        // A fresh one per opening, like the Settings command: it reads the stored settings when created
        using var settings = _settingsFactory.Create(section);
        await _dialogService.ShowDialogAsync<SettingsViewModel, bool>(settings);
    }
}
