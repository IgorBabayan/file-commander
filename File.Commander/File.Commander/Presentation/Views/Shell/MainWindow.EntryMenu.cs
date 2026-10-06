using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Material.Icons;

namespace File.Commander.Presentation.Views.Shell;

/// <summary>
/// The context menu of files and folders in a view, on a right click or the menu key (Shift+F10).
/// DirectoryView works out what it is for (<see cref="EntryMenuRequestedEventArgs"/>); built per opening, as what
/// it offers depends on the entries (folders, files, one or several) and on the page (Trash).
/// </summary>
public partial class MainWindow
{
    private ContextMenu? _entryMenu;

    private void InitializeEntryMenu() => AddHandler(DirectoryView.EntryMenuRequestedEvent, OnEntryMenuRequested);

    private void OnEntryMenuRequested(object? sender, EntryMenuRequestedEventArgs e)
    {
        e.Handled = true;

        // Kept so a second request (e.g. the menu key right after a click) doesn't stack another menu
        if (ViewModel is not { } vm || _entryMenu is { IsOpen: true } || _folderMenu is { IsOpen: true })
            return;

        // Opened on the window, placed at the row: a menu opened on the row would be its child, so clicks on the
        // menu bubble into the file list (its rubber band took them for clicks on empty space and cleared the selection)
        var menu = new ContextMenu
        {
            Placement = e.AtPointer ? PlacementMode.Pointer : PlacementMode.BottomEdgeAlignedLeft,
            PlacementTarget = e.Target,
        };

        foreach (var entry in BuildEntryMenu(vm, e.Page, e.Entries))
            menu.Items.Add(entry);

        _entryMenu = menu;
        menu.Open(this);
    }

    /// <summary>
    /// Runs <paramref name="action"/> once the click that picked it is done and the menu is closed. Open navigates the
    /// view the menu was opened from: doing that while the click is still being routed through it doesn't work.
    /// </summary>
    private static Action AfterMenu(Action action) => () => Dispatcher.UIThread.Post(action);

    private static IEnumerable<Control> BuildEntryMenu(MainViewModel vm, DirectoryViewModel page,
        IReadOnlyList<FileEntryViewModel> entries)
    {
        var single = entries.Count == 1 ? entries[0] : null;
        var hasFolders = entries.Any(entry => entry.IsDirectory);
        var hasFiles = entries.Any(entry => !entry.IsDirectory);
        var canModify = MainViewModel.CanModify(page);

        // Several folders and no file: Open has nothing to do (there is only one view to go into)
        yield return EntryMenuItem("Open",
            single is { IsDirectory: false } ? MaterialIconKind.OpenInApp : MaterialIconKind.FolderOpenOutline,
            () => vm.OpenEntries(page, entries),
            isEnabled: single is not null || hasFiles);
        yield return EntryMenuItem("Open in a view", MaterialIconKind.ViewSplitVertical,
            () => vm.OpenEntryInSplitView(single!),
            isEnabled: single is { IsDirectory: true });
        yield return EntryMenuItem("Open in a new tab", MaterialIconKind.TabPlus,
            () => vm.OpenEntriesInNewTabs(entries),
            isEnabled: hasFolders);
        yield return new Separator();

        yield return ShortcutEntry(vm, "Cut", MaterialIconKind.ContentCut, KeymapActions.Cut,
            () => _ = vm.CutAsync(entries), isEnabled: canModify);
        yield return ShortcutEntry(vm, "Copy", MaterialIconKind.ContentCopy, KeymapActions.Copy,
            () => _ = vm.CopyAsync(entries));
        yield return EntryMenuItem("Move to…", MaterialIconKind.FileMoveOutline,
            () => _ = vm.MoveToAsync(entries), isEnabled: canModify);
        yield return EntryMenuItem("Copy to…", MaterialIconKind.ContentDuplicate,
            () => _ = vm.CopyToAsync(entries));
        yield return new Separator();

        yield return ShortcutEntry(vm, "Rename…", MaterialIconKind.PencilOutline, KeymapActions.Rename,
            () => _ = vm.RenameAsync(single!), isEnabled: canModify && single is not null);
        yield return EntryMenuItem("Compress…", MaterialIconKind.FolderZipOutline,
            () => _ = vm.CompressAsync(entries), isEnabled: canModify);
        yield return EntryMenuItem("Extract here", MaterialIconKind.PackageVariant,
            () => _ = vm.ExtractAsync(entries),
            isEnabled: canModify && MainViewModel.CanExtract(entries));
        yield return EntryMenuItem("Email…", MaterialIconKind.EmailOutline,
            () => _ = vm.EmailAsync(entries), isEnabled: !hasFolders);
        yield return ShortcutEntry(vm, "Move to Trash", MaterialIconKind.DeleteOutline, KeymapActions.MoveToTrash,
            () => _ = vm.MoveToTrashAsync(entries), isEnabled: canModify);
        yield return ShortcutEntry(vm, "Delete permanently", MaterialIconKind.DeleteForeverOutline,
            KeymapActions.DeletePermanently, () => _ = vm.DeletePermanentlyAsync(entries), isEnabled: canModify);
        yield return new Separator();

        yield return ShortcutEntry(vm, "Properties", MaterialIconKind.InformationOutline, KeymapActions.Properties,
            () => _ = vm.ShowEntryPropertiesAsync(entries));
    }

    /// <summary>A menu item whose action runs after the menu closed (<see cref="AfterMenu"/>).</summary>
    private static MenuItem EntryMenuItem(string header, MaterialIconKind icon, Action action, bool isEnabled = true)
        => MenuEntry(header, icon, AfterMenu(action), isEnabled);

    /// <summary>A menu item that shows its shortcut from Settings → Keymap, e.g. "Ctrl+X". Display only.</summary>
    private static MenuItem ShortcutEntry(MainViewModel vm, string header, MaterialIconKind icon, string actionId,
        Action action, bool isEnabled = true)
    {
        var entry = EntryMenuItem(header, icon, action, isEnabled);
        if (vm.ShortcutFor(actionId) is { } chord)
            entry.InputGesture = new KeyGesture(chord.Key, chord.Modifiers);

        return entry;
    }
}
