using File.Commander.Presentation.Services;
using File.Commander.Presentation.ViewModels.Dialogs;
using File.Commander.Presentation.ViewModels.Settings;

namespace File.Commander.Presentation.ViewModels.Shell;

/// <summary>What the sidebar's context menu does. The menu itself is built by MainWindow.SidebarMenu.cs.</summary>
public partial class MainViewModel
{
    /// <summary>Open: what a click on the item does.</summary>
    public void OpenSidebarItem(SidebarItem item) => Navigate(item.Location);

    /// <summary>
    /// Open in split view: splits the selected tab if it isn't split yet, opens the item in the other
    /// view and makes that view the active one. The view that was active keeps its folder.
    /// </summary>
    public void OpenSidebarItemInSplitView(SidebarItem item)
    {
        var tab = ActiveTab;
        if (!tab.IsSplit)
            ToggleSplitView();

        if (tab.Panes.FirstOrDefault(pane => !ReferenceEquals(pane, tab.ActivePane)) is not { } other)
            return;

        other.Navigate(item.Location);
        ActivatePane(other); // Syncs the shell through ActivePageChanged
    }

    /// <summary>Open in new tab: a tab next to the selected one, in the active view's layout, selected.</summary>
    public void OpenSidebarItemInNewTab(SidebarItem item)
    {
        var tab = CreateTab(item.Location, ActivePane.ViewMode);
        Tabs.Insert(Tabs.IndexOf(_activeTab) + 1, tab);
        UpdateTabStates();
        SelectTab(tab);
    }

    /// <summary>Recent → File history settings.</summary>
    public Task OpenFileHistorySettingsAsync() => OpenSettingsAtAsync(SettingsViewModel.FileHistorySection);

    /// <summary>Trash → Trash settings.</summary>
    public Task OpenTrashSettingsAsync() => OpenSettingsAtAsync(SettingsViewModel.TrashSection);

    /// <summary>Whether Empty trash is enabled: something is in one of the trash folders it would empty.</summary>
    public bool CanEmptyTrash() => TrashBins.HasItems(TrashBins.Find(_appliedSettings.Advanced!.EmptyTrashOnAllDrives));

    /// <summary>Trash → Empty trash. Deletes everything in the trash for good, after asking (Settings → Trash).</summary>
    public async Task EmptyTrashAsync()
    {
        var advanced = _appliedSettings.Advanced!;
        var bins = TrashBins.Find(advanced.EmptyTrashOnAllDrives);
        if (!TrashBins.HasItems(bins))
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

        var failed = await Task.Run(() => TrashBins.Empty(bins));

        // A view showing the trash (or a folder inside a trash folder) would list what's gone
        RefreshPanes(page => page.Location == Locations.Trash
                             || bins.Any(bin => page.Location.StartsWith(bin, StringComparison.Ordinal)));

        if (failed > 0)
        {
            using var notice = PromptViewModel.ForNotice(
                "The trash wasn't emptied completely",
                failed == 1
                    ? "1 item couldn't be deleted. Check that you're allowed to delete it."
                    : $"{failed:N0} items couldn't be deleted. Check that you're allowed to delete them.");

            await _dialogService.ShowDialogAsync<PromptViewModel, bool>(notice);
        }
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
        using var settings = new SettingsViewModel(_settings, section);
        await _dialogService.ShowDialogAsync<SettingsViewModel, bool>(settings);
    }
}
