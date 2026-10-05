using Avalonia.Controls;
using Avalonia.Input;
using File.Commander.Application.Keyboard;
using File.Commander.Presentation.ViewModels.Browser;
using File.Commander.Presentation.Views.Browser;
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
        if (ViewModel is not { } vm || _entryMenu is { IsOpen: true })
            return;

        var menu = new ContextMenu
        {
            Placement = e.AtPointer ? PlacementMode.Pointer : PlacementMode.BottomEdgeAlignedLeft,
        };

        foreach (var entry in BuildEntryMenu(vm, e.Page, e.Entries))
            menu.Items.Add(entry);

        _entryMenu = menu;
        menu.Open(e.Target);
    }

    private static IEnumerable<Control> BuildEntryMenu(MainViewModel vm, DirectoryViewModel page,
        IReadOnlyList<FileEntryViewModel> entries)
    {
        var single = entries.Count == 1 ? entries[0] : null;
        var hasFolders = entries.Any(entry => entry.IsDirectory);
        var hasFiles = entries.Any(entry => !entry.IsDirectory);
        var canModify = MainViewModel.CanModify(page);

        // Several folders and no file: Open has nothing to do (there is only one view to go into)
        yield return MenuEntry("Open",
            single is { IsDirectory: false } ? MaterialIconKind.OpenInApp : MaterialIconKind.FolderOpenOutline,
            () => vm.OpenEntries(page, entries),
            isEnabled: single is not null || hasFiles);
        yield return MenuEntry("Open in a view", MaterialIconKind.ViewSplitVertical,
            () => vm.OpenEntryInSplitView(single!),
            isEnabled: single is { IsDirectory: true });
        yield return MenuEntry("Open in a new tab", MaterialIconKind.TabPlus,
            () => vm.OpenEntriesInNewTabs(entries),
            isEnabled: hasFolders);
        yield return new Separator();

        yield return ShortcutEntry(vm, "Cut", MaterialIconKind.ContentCut, KeymapActions.Cut,
            () => _ = vm.CutAsync(entries), isEnabled: canModify);
        yield return ShortcutEntry(vm, "Copy", MaterialIconKind.ContentCopy, KeymapActions.Copy,
            () => _ = vm.CopyAsync(entries));
        yield return MenuEntry("Move to…", MaterialIconKind.FileMoveOutline,
            () => _ = vm.MoveToAsync(entries), isEnabled: canModify);
        yield return MenuEntry("Copy to…", MaterialIconKind.ContentDuplicate,
            () => _ = vm.CopyToAsync(entries));
        yield return new Separator();

        yield return ShortcutEntry(vm, "Rename…", MaterialIconKind.PencilOutline, KeymapActions.Rename,
            () => _ = vm.RenameAsync(single!), isEnabled: canModify && single is not null);
        yield return MenuEntry("Compress…", MaterialIconKind.FolderZipOutline,
            () => _ = vm.CompressAsync(entries), isEnabled: canModify);
        yield return MenuEntry("Email…", MaterialIconKind.EmailOutline,
            () => _ = vm.EmailAsync(entries), isEnabled: !hasFolders);
        yield return ShortcutEntry(vm, "Move to Trash", MaterialIconKind.DeleteOutline, KeymapActions.MoveToTrash,
            () => _ = vm.MoveToTrashAsync(entries), isEnabled: canModify);
        yield return new Separator();

        yield return ShortcutEntry(vm, "Properties", MaterialIconKind.InformationOutline, KeymapActions.Properties,
            () => _ = vm.ShowEntryPropertiesAsync(entries));
    }

    /// <summary>A menu item that shows its shortcut from Settings → Keymap, e.g. "Ctrl+X". Display only.</summary>
    private static MenuItem ShortcutEntry(MainViewModel vm, string header, MaterialIconKind icon, string actionId,
        Action action, bool isEnabled = true)
    {
        var entry = MenuEntry(header, icon, action, isEnabled);
        if (vm.ShortcutFor(actionId) is { } chord)
            entry.InputGesture = new KeyGesture(chord.Key, chord.Modifiers);

        return entry;
    }
}
