using CommunityToolkit.Mvvm.Input;
using File.Commander.Application.Keyboard;
using File.Commander.Presentation.Services;
using File.Commander.Presentation.ViewModels.ActionCenter;
using File.Commander.Presentation.ViewModels.Browser;
using File.Commander.Presentation.ViewModels.Dialogs;

namespace File.Commander.Presentation.ViewModels.Shell;

/// <summary>
/// What the context menu of files and folders does (the menu itself is built by MainWindow.EntryMenu.cs),
/// and the shortcuts of Settings → Keymap → Files, which do the same on the active view's selection.
/// Copies, moves, the trash and archives run in the Action center.
/// </summary>
public partial class MainViewModel
{
    /// <summary>
    /// Items of <paramref name="page"/> can be moved, renamed or trashed. Not in the Trash page: its items live
    /// under trash names, with info files that must stay in step.
    /// </summary>
    public static bool CanModify(DirectoryViewModel page) => page.Location != Locations.Trash;

    /// <summary>The first chord bound to <paramref name="actionId"/>, shown next to the menu item. Null: unbound.</summary>
    public KeyChord? ShortcutFor(string actionId) => _keymap.Current.For(actionId) is [var first, ..] ? first : null;

    /// <summary>
    /// Open in split view: splits the selected tab if it isn't split yet, opens <paramref name="location"/> in the
    /// other view and makes that view the active one. The view that was active keeps its folder.
    /// </summary>
    public void OpenInSplitView(string location)
    {
        var tab = ActiveTab;
        if (!tab.IsSplit)
            ToggleSplitView();

        if (tab.Panes.FirstOrDefault(pane => !ReferenceEquals(pane, tab.ActivePane)) is not { } other)
            return;

        other.Navigate(location);
        ActivatePane(other); // Syncs the shell through ActivePageChanged
    }

    /// <summary>Open in new tab: a tab next to the selected one, in the active view's layout, selected.</summary>
    public void OpenInNewTab(string location)
    {
        var tab = CreateTab(location, ActivePane.ViewMode);
        Tabs.Insert(Tabs.IndexOf(_activeTab) + 1, tab);
        UpdateTabStates();
        SelectTab(tab);
    }

    /// <summary>Open: what a double click does; several entries open their files.</summary>
    public void OpenEntries(DirectoryViewModel page, IReadOnlyList<FileEntryViewModel> entries)
        => page.OpenEntries(entries);

    /// <summary>Open in a view: the folder in the other view of a split tab.</summary>
    public void OpenEntryInSplitView(FileEntryViewModel entry)
    {
        if (entry.IsDirectory)
            OpenInSplitView(entry.FullPath);
    }

    /// <summary>Open in a new tab: one tab per folder, in their order, the last one selected. Files are skipped.</summary>
    public void OpenEntriesInNewTabs(IReadOnlyList<FileEntryViewModel> entries)
    {
        // Each new tab is selected, so the next one opens to its right
        foreach (var entry in entries.Where(entry => entry.IsDirectory).ToList())
            OpenInNewTab(entry.FullPath);
    }

    /// <summary>Cut: Paste (here or in another file manager) moves them.</summary>
    public Task CutAsync(IReadOnlyList<FileEntryViewModel> entries) => FileClipboard.SetAsync(PathsOf(entries), cut: true);

    /// <summary>Copy: Paste (here or in another file manager) copies them.</summary>
    public Task CopyAsync(IReadOnlyList<FileEntryViewModel> entries) => FileClipboard.SetAsync(PathsOf(entries), cut: false);

    /// <summary>
    /// Ctrl+V: the files on the clipboard into the active view's folder, copied or (after Cut) moved.
    /// Taken names get a number; nothing is overwritten.
    /// </summary>
    public async Task PasteAsync()
    {
        if (CurrentPage is not DirectoryViewModel page || Locations.IsVirtual(page.Location))
            return;

        var target = page.Location;
        if (await FileClipboard.GetAsync() is not { Paths.Count: > 0 } files)
            return;

        await TransferAsync(files.Paths, target, move: files.IsCut);

        // Moved: the clipboard would point at where they were
        if (files.IsCut)
            await FileClipboard.ClearAsync();
    }

    /// <summary>Move to…: into a folder picked in the system's chooser.</summary>
    public Task MoveToAsync(IReadOnlyList<FileEntryViewModel> entries) => TransferToPickedAsync(entries, move: true);

    /// <summary>Copy to…: into a folder picked in the system's chooser.</summary>
    public Task CopyToAsync(IReadOnlyList<FileEntryViewModel> entries) => TransferToPickedAsync(entries, move: false);

    /// <summary>Rename… (F2): asks for the new name. The views showing its folder are refreshed.</summary>
    public async Task RenameAsync(FileEntryViewModel entry)
    {
        using var prompt = PromptViewModel.ForInput(
            entry.IsDirectory ? "Rename folder" : "Rename file",
            $"The new name of “{entry.Name}”.",
            entry.Name,
            "Rename");

        if (!await _dialogService.ShowDialogAsync<PromptViewModel, bool>(prompt))
            return;

        var name = prompt.InputText.Trim();
        if (name == entry.Name)
            return;

        if (FileOperations.Rename(entry.FullPath, name) is { } problem)
        {
            await ShowNoticeAsync("Can't rename", problem);
            return;
        }

        RefreshFolders([FileOperations.ParentOf(entry.FullPath)], entriesMoved: true);
    }

    /// <summary>
    /// Compress…: asks for the archive's name, then packs the entries into a .zip next to the first one,
    /// in the Action center.
    /// </summary>
    public async Task CompressAsync(IReadOnlyList<FileEntryViewModel> entries)
    {
        if (entries.Count == 0)
            return;

        var folder = FileOperations.ParentOf(entries[0].FullPath);
        var suggested = entries.Count == 1
            ? (entries[0].IsDirectory ? entries[0].Name : IOPath.GetFileNameWithoutExtension(entries[0].Name)) + ".zip"
            : "Archive.zip";

        using var prompt = PromptViewModel.ForInput(
            "Compress",
            entries.Count == 1
                ? $"“{entries[0].Name}” is packed into a zip archive in the same folder."
                : $"The {entries.Count:N0} items are packed into one zip archive in the same folder.",
            suggested,
            "Create");

        if (!await _dialogService.ShowDialogAsync<PromptViewModel, bool>(prompt))
            return;

        var name = prompt.InputText.Trim();
        if (!name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            name += ".zip";

        if (FileOperations.ValidateName(name) is { } problem)
        {
            await ShowNoticeAsync("Can't compress", problem);
            return;
        }

        var archive = IOPath.Combine(folder, name);
        if (FileOperations.Exists(archive))
        {
            await ShowNoticeAsync("Can't compress", $"An item named “{name}” already exists in this folder.");
            return;
        }

        var paths = PathsOf(entries);
        await ActionCenter.RunAsync(OperationKind.Other, OperationTitles.Compress, archive,
            progress => FileOperations.Compress(paths, archive, progress));

        RefreshFolders([folder]);
    }

    /// <summary>Email…: the default email app with the files attached. Folders can't be attached.</summary>
    public async Task EmailAsync(IReadOnlyList<FileEntryViewModel> entries)
    {
        var files = entries.Where(entry => !entry.IsDirectory).Select(entry => entry.FullPath).ToList();
        if (files.Count == 0)
            return;

        if (FileOperations.Email(files) is { } problem)
            await ShowNoticeAsync("Can't send by email", problem);
    }

    /// <summary>Move to Trash (Delete): no question asked, as everything can be restored from the trash.</summary>
    public async Task MoveToTrashAsync(IReadOnlyList<FileEntryViewModel> entries)
    {
        if (entries.Count == 0)
            return;

        var paths = PathsOf(entries);
        await ActionCenter.RunAsync(OperationKind.Delete, OperationTitles.MoveToTrash, ItemsText(paths),
            progress => TrashBins.MoveToTrash(paths, progress));

        RefreshFolders(paths.Select(FileOperations.ParentOf), entriesMoved: true, trashChanged: true);
    }

    /// <summary>Properties (Alt+Enter): what the entries are, where they are and how much they hold.</summary>
    public async Task ShowEntryPropertiesAsync(IReadOnlyList<FileEntryViewModel> entries)
    {
        if (entries.Count == 0)
            return;

        using var properties = PropertiesViewModel.For(entries);
        await _dialogService.ShowDialogAsync<PropertiesViewModel, bool>(properties);
    }

    // ===== Shortcuts: the same, on the active view's selection =====

    private IReadOnlyList<FileEntryViewModel> SelectedEntries
        => CurrentPage is DirectoryViewModel page ? page.SelectedEntries : [];

    private bool HasSelectedEntries() => SelectedEntries.Count > 0;

    private bool CanModifySelection() => CurrentPage is DirectoryViewModel { HasSelection: true } page && CanModify(page);

    private bool CanRenameSelection() => CanModifySelection() && SelectedEntries.Count == 1;

    private bool CanPaste() => CurrentPage is DirectoryViewModel page && !Locations.IsVirtual(page.Location);

    [RelayCommand(CanExecute = nameof(CanModifySelection))]
    private Task CutSelection() => CutAsync(SelectedEntries);

    [RelayCommand(CanExecute = nameof(HasSelectedEntries))]
    private Task CopySelection() => CopyAsync(SelectedEntries);

    [RelayCommand(CanExecute = nameof(CanPaste))]
    private Task Paste() => PasteAsync();

    [RelayCommand(CanExecute = nameof(CanRenameSelection))]
    private Task RenameSelection() => RenameAsync(SelectedEntries[0]);

    [RelayCommand(CanExecute = nameof(CanModifySelection))]
    private Task TrashSelection() => MoveToTrashAsync(SelectedEntries);

    [RelayCommand(CanExecute = nameof(HasSelectedEntries))]
    private Task ShowSelectionProperties() => ShowEntryPropertiesAsync(SelectedEntries);

    // ===== Helpers =====

    /// <summary>A folder page's files that can't be opened (no app registered…) are reported in a notice.</summary>
    private DirectoryViewModel Watch(DirectoryViewModel page)
    {
        page.OpenFailed += OnOpenFailed;
        return page;
    }

    private void OnOpenFailed(object? sender, OpenFailedEventArgs e)
        => _ = ShowNoticeAsync($"Can't open “{e.Name}”", e.Problem);

    private async Task TransferToPickedAsync(IReadOnlyList<FileEntryViewModel> entries, bool move)
    {
        if (entries.Count == 0)
            return;

        var paths = PathsOf(entries);
        var title = move ? $"Move {ItemsText(paths)} to" : $"Copy {ItemsText(paths)} to";
        if (await FolderPicker.PickAsync(title, FileOperations.ParentOf(paths[0])) is not { } target)
            return;

        await TransferAsync(paths, target, move);
    }

    /// <summary>Copies or moves in the Action center, then refreshes the views of the folders that changed.</summary>
    private async Task TransferAsync(IReadOnlyList<string> sources, string target, bool move)
    {
        var details = $"{ItemsText(sources)} to {target}";
        await ActionCenter.RunAsync(move ? OperationKind.Move : OperationKind.Copy,
            move ? OperationTitles.Move : OperationTitles.Copy, details,
            progress => FileOperations.Transfer(sources, target, move, progress));

        IEnumerable<string> changed = move ? sources.Select(FileOperations.ParentOf).Append(target) : [target];
        RefreshFolders(changed, entriesMoved: move);
    }

    /// <summary>
    /// Rebuilds every view showing one of <paramref name="folders"/>; in the tree, every view whose tree reaches it.
    /// </summary>
    /// <param name="entriesMoved">Files left their place: Recent lists them by path.</param>
    /// <param name="trashChanged">Something went into the trash: the Trash page lists it.</param>
    private void RefreshFolders(IEnumerable<string> folders, bool entriesMoved = false, bool trashChanged = false)
    {
        var changed = folders.Select(Locations.Normalize).Distinct(StringComparer.Ordinal).ToList();
        RefreshPanes(page => page.Location switch
        {
            Locations.Trash => trashChanged,
            Locations.Recent => entriesMoved,
            _ => page is DirectoryViewModel directory && changed.Any(folder => directory.IsTreeView
                ? FileOperations.IsSameOrInside(folder, directory.Location)
                : Locations.AreEqual(folder, directory.Location)),
        });
    }

    private async Task ShowNoticeAsync(string title, string message)
    {
        using var notice = PromptViewModel.ForNotice(title, message);
        await _dialogService.ShowDialogAsync<PromptViewModel, bool>(notice);
    }

    private static List<string> PathsOf(IReadOnlyList<FileEntryViewModel> entries)
        => entries.Select(entry => entry.FullPath).ToList();

    /// <summary>"report.pdf" for one, "3 items" for several: how the Action center names what it works on.</summary>
    private static string ItemsText(IReadOnlyList<string> paths)
        => paths.Count == 1 ? $"“{IOPath.GetFileName(paths[0])}”" : $"{paths.Count:N0} items";
}
