using CommunityToolkit.Mvvm.Input;

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

    /// <summary>Extract here has something to do: one of <paramref name="entries"/> is an archive it can extract.</summary>
    public static bool CanExtract(IReadOnlyList<FileEntryViewModel> entries)
        => entries.Any(entry => !entry.IsDirectory && ArchiveExtractor.CanExtract(entry.FullPath));

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

    /// <summary>
    /// Open with…: pick the app that opens <paramref name="entry"/>, this time or ("Always use for this file type")
    /// from now on. The choice is saved in the user's mimeapps.list, so other apps follow it too.
    /// </summary>
    public async Task OpenWithAsync(FileEntryViewModel entry)
    {
        if (entry.IsDirectory)
            return;

        var path = entry.FullPath;
        var choices = await Task.Run(() => OpenWithApps.For(path));

        using var dialog = new OpenWithViewModel(entry.Name, choices);
        if (!await _dialogService.ShowDialogAsync<OpenWithViewModel, bool>(dialog) || dialog.SelectedApp is not { } app)
            return;

        // Saved first: a file the app can't open still leaves the choice made
        var makeDefault = dialog.AlwaysUse;
        if (await Task.Run(() => OpenWithApps.Remember(choices.Type, app, makeDefault)) is { } saveProblem && makeDefault)
            await ShowNoticeAsync($"Can't make “{app.Name}” the default app", saveProblem);

        if (await Task.Run(() => FileLauncher.Launch(app, path)) is { } problem)
            await ShowNoticeAsync($"Can't open “{entry.Name}” in {app.Name}", problem);
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
    /// The files on the clipboard into the active view's folder, copied or (after Cut) moved. What happens to a name
    /// that is taken depends on <paramref name="mode"/>: Paste (Ctrl+V) asks, Paste without replace (Ctrl+Alt+V)
    /// numbers it, Paste with replace (Ctrl+Shift+V) replaces the item that has it.
    /// </summary>
    public Task PasteAsync(PasteMode mode = PasteMode.Ask)
        => CurrentPage is DirectoryViewModel page ? PasteIntoAsync(page, mode) : Task.CompletedTask;

    /// <summary>
    /// <see cref="PasteAsync"/> into the folder of <paramref name="page"/>: the context menu of a folder's empty space
    /// pastes into the view it was opened on.
    /// </summary>
    public async Task PasteIntoAsync(DirectoryViewModel page, PasteMode mode = PasteMode.Ask)
    {
        if (!CanCreateIn(page))
            return;

        var target = Locations.Normalize(page.Location);
        if (await FileClipboard.GetAsync() is not { Paths.Count: > 0 } files)
            return;

        var items = mode switch
        {
            PasteMode.KeepBoth => files.Paths.Select(path => new TransferItem(path)).ToList(),
            PasteMode.Replace => files.Paths.Select(path => new TransferItem(path, OnConflict: NameConflict.Replace)).ToList(),
            _ => await AskAboutConflictsAsync(files.Paths, target, files.IsCut),
        };

        // Canceled in the dialog, or every item skipped
        if (items is not { Count: > 0 })
            return;

        await TransferAsync(items, target, move: files.IsCut);

        // Moved: the clipboard would point at where they were
        if (files.IsCut)
            await FileClipboard.ClearAsync();
    }

    /// <summary>
    /// Paste: asks, one item after the other, what happens to each pasted item whose name is taken in
    /// <paramref name="target"/> (by an item there, or by another pasted item): replace, rename or skip.
    /// "Do the same for the other items" answers for the rest of the items whose name is taken.
    /// </summary>
    /// <returns>What to transfer, skipped items left out. Null: the paste was canceled.</returns>
    private async Task<List<TransferItem>?> AskAboutConflictsAsync(IReadOnlyList<string> paths, string target, bool move)
    {
        var sources = paths.Select(Locations.Normalize).ToList();

        // Names this paste ends up using, so a new name can't take one of them
        var taken = new HashSet<string>(StringComparer.Ordinal);
        var conflicts = new bool[sources.Count];
        for (var i = 0; i < sources.Count; i++)
        {
            var name = IOPath.GetFileName(sources[i]);

            // Moved where it already is: nothing happens to it
            if (move && FileOperations.ParentOf(sources[i]) == target)
                continue;

            conflicts[i] = FileOperations.Exists(IOPath.Combine(target, name)) || !taken.Add(name);
        }

        var remaining = conflicts.Count(conflict => conflict);
        PasteConflictChoice? forAll = null;
        var items = new List<TransferItem>(sources.Count);

        for (var i = 0; i < sources.Count; i++)
        {
            var source = sources[i];
            if (!conflicts[i])
            {
                items.Add(new TransferItem(source));
                continue;
            }

            remaining--;
            var name = IOPath.GetFileName(source);
            bool IsFree(string candidate)
                => !taken.Contains(candidate) && !FileOperations.Exists(IOPath.Combine(target, candidate));

            var choice = forAll;
            string? newName = null;
            if (choice is null)
            {
                using var dialog = new PasteConflictViewModel(name, FileOperations.IsDirectory(source), target,
                    remaining, SuggestName(name, IsFree), IsFree);

                if (!await _dialogService.ShowDialogAsync<PasteConflictViewModel, bool>(dialog))
                    return null;

                choice = dialog.Choice;
                newName = dialog.NewName.Trim();
                if (dialog.ApplyToAll && dialog.CanApplyToAll)
                    forAll = choice;
            }

            switch (choice)
            {
                case PasteConflictChoice.Replace:
                    items.Add(new TransferItem(source, name, NameConflict.Replace));
                    break;

                case PasteConflictChoice.Rename when !string.IsNullOrEmpty(newName):
                    taken.Add(newName);
                    // Taken in the meantime: it gets a number rather than replacing anything
                    items.Add(new TransferItem(source, newName));
                    break;

                case PasteConflictChoice.Skip:
                    break;
            }
        }

        return items;
    }

    /// <summary>"report (2).pdf", "report (3).pdf"…: the first one <paramref name="isFree"/> accepts.</summary>
    private static string SuggestName(string name, Func<string, bool> isFree)
    {
        for (var number = 2; ; number++)
        {
            var candidate = FileOperations.NumberedName(name, number);
            if (isFree(candidate))
                return candidate;
        }
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

        // Asks for administrator rights in the system's dialog when the user isn't allowed to rename it
        if (await FileOperations.RenameAsync(entry.FullPath, name) is { } problem)
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

    /// <summary>
    /// Extract here, and a double click on an archive: each archive is extracted next to itself in the Action center,
    /// into a folder named after it (or as is, when all it holds is one folder). The views showing it are refreshed
    /// once it is done, so the new folder shows up without leaving the folder.
    /// </summary>
    public async Task ExtractAsync(IReadOnlyList<FileEntryViewModel> entries)
    {
        var archives = entries
            .Where(entry => !entry.IsDirectory && ArchiveExtractor.CanExtract(entry.FullPath))
            .Select(entry => entry.FullPath)
            .ToList();

        // One after the other, each its own action: two archives named alike would race for the same folder name.
        // Each folder shows as soon as its archive is done.
        foreach (var archive in archives)
        {
            await ActionCenter.RunAsync(OperationKind.Other, OperationTitles.Extract, ItemsText([archive]),
                progress => ArchiveExtractor.Extract(archive, progress));

            RefreshFolders([FileOperations.ParentOf(archive)]);
        }
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

    /// <summary>
    /// Delete permanently (Shift+Delete): skips the trash, so it can't be undone. Asks first while
    /// Settings → Advanced → Dialog → "Ask before deleting files permanently" is on.
    /// </summary>
    public async Task DeletePermanentlyAsync(IReadOnlyList<FileEntryViewModel> entries)
    {
        if (entries.Count == 0)
            return;

        var paths = PathsOf(entries);
        if (_appliedSettings.Advanced!.ConfirmPermanentDelete)
        {
            var message = paths.Count == 1
                ? "It won't go to the trash. This can't be undone."
                : "They won't go to the trash. This can't be undone.";

            using var confirm = PromptViewModel.ForConfirmation(
                $"Delete {ItemsText(paths)} permanently?", message, "Delete", destructive: true);

            if (!await _dialogService.ShowDialogAsync<PromptViewModel, bool>(confirm))
                return;
        }

        await ActionCenter.RunAsync(OperationKind.DeletePermanently, OperationTitles.DeletePermanently,
            ItemsText(paths), progress => FileOperations.DeletePermanently(paths, progress));

        RefreshFolders(paths.Select(FileOperations.ParentOf), entriesMoved: true);
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

    private bool CanPaste() => CurrentPage is DirectoryViewModel page && CanCreateIn(page);

    [RelayCommand(CanExecute = nameof(CanModifySelection))]
    private Task CutSelection() => CutAsync(SelectedEntries);

    [RelayCommand(CanExecute = nameof(HasSelectedEntries))]
    private Task CopySelection() => CopyAsync(SelectedEntries);

    [RelayCommand(CanExecute = nameof(CanPaste))]
    private Task Paste() => PasteAsync();

    [RelayCommand(CanExecute = nameof(CanPaste))]
    private Task PasteWithoutReplace() => PasteAsync(PasteMode.KeepBoth);

    [RelayCommand(CanExecute = nameof(CanPaste))]
    private Task PasteWithReplace() => PasteAsync(PasteMode.Replace);

    [RelayCommand(CanExecute = nameof(CanRenameSelection))]
    private Task RenameSelection() => RenameAsync(SelectedEntries[0]);

    [RelayCommand(CanExecute = nameof(CanModifySelection))]
    private Task TrashSelection() => MoveToTrashAsync(SelectedEntries);

    [RelayCommand(CanExecute = nameof(CanModifySelection))]
    private Task DeleteSelectionPermanently() => DeletePermanentlyAsync(SelectedEntries);

    [RelayCommand(CanExecute = nameof(HasSelectedEntries))]
    private Task ShowSelectionProperties() => ShowEntryPropertiesAsync(SelectedEntries);

    // ===== Helpers =====

    /// <summary>
    /// A folder page's files that can't be opened (no app registered…) are reported in a notice; the archives it opens
    /// are extracted.
    /// </summary>
    private DirectoryViewModel Watch(DirectoryViewModel page)
    {
        page.OpenFailed += OnOpenFailed;
        page.ExtractRequested += OnExtractRequested;
        return page;
    }

    private void OnOpenFailed(object? sender, OpenFailedEventArgs e)
        => _ = ShowNoticeAsync($"Can't open “{e.Name}”", e.Problem);

    private void OnExtractRequested(object? sender, ExtractRequestedEventArgs e) => _ = ExtractAsync([e.Entry]);

    private async Task TransferToPickedAsync(IReadOnlyList<FileEntryViewModel> entries, bool move)
    {
        if (entries.Count == 0)
            return;

        var paths = PathsOf(entries);
        var title = move ? $"Move {ItemsText(paths)} to" : $"Copy {ItemsText(paths)} to";
        if (await FolderPicker.PickAsync(title, FileOperations.ParentOf(paths[0])) is not { } target)
            return;

        await TransferAsync(paths.Select(path => new TransferItem(path)).ToList(), target, move);
    }

    /// <summary>Copies or moves in the Action center, then refreshes the views of the folders that changed.</summary>
    private async Task TransferAsync(IReadOnlyList<TransferItem> items, string target, bool move)
    {
        var sources = items.Select(item => item.Source).ToList();
        var details = $"{ItemsText(sources)} to {target}";
        await ActionCenter.RunAsync(move ? OperationKind.Move : OperationKind.Copy,
            move ? OperationTitles.Move : OperationTitles.Copy, details,
            progress => FileOperations.Transfer(items, target, move, progress));

        IEnumerable<string> changed = move ? sources.Select(FileOperations.ParentOf).Append(target) : [target];
        RefreshFolders(changed, entriesMoved: move);
    }

    /// <summary>
    /// Rebuilds every view showing one of <paramref name="folders"/>; in the tree, every view whose tree reaches it.
    /// </summary>
    private void RefreshFolders(IEnumerable<string> folders, bool entriesMoved = false, bool trashChanged = false)
    {
        var changed = folders.Select(Locations.Normalize).Distinct(StringComparer.Ordinal).ToList();
        RefreshPanes(page => page.Location switch
        {
            Locations.Trash => trashChanged,
            Locations.Recent => entriesMoved,
            // Search results: something changed where it searched, so what matches may have too
            _ when page is DirectoryViewModel { Search: { } search } => changed.Any(folder => search.IncludeSubfolders
                ? FileOperations.IsSameOrInside(folder, search.Folder)
                : Locations.AreEqual(folder, search.Folder)),
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
