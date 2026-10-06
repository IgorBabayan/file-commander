using Avalonia.Controls;
using File.Commander.Application.Keyboard;
using File.Commander.Presentation.ViewModels.Browser;
using File.Commander.Presentation.Views.Browser;
using Material.Icons;
using Material.Icons.Avalonia;

namespace File.Commander.Presentation.Views.Shell;

/// <summary>
/// The context menu of a folder's empty space, on a right click there or the menu key (Shift+F10) with nothing
/// selected. DirectoryView asks for it (<see cref="FolderMenuRequestedEventArgs"/>); built per opening, as what it
/// offers depends on the page (Recent and Trash take no new items) and on the clipboard (Paste).
/// </summary>
public partial class MainWindow
{
    private ContextMenu? _folderMenu;

    // The clipboard is being read for a menu about to open: a second request would open another one
    private bool _openingFolderMenu;

    private void InitializeFolderMenu() => AddHandler(DirectoryView.FolderMenuRequestedEvent, OnFolderMenuRequested);

    private async void OnFolderMenuRequested(object? sender, FolderMenuRequestedEventArgs e)
    {
        e.Handled = true;

        if (ViewModel is not { } vm || _openingFolderMenu || _folderMenu is { IsOpen: true } || _entryMenu is { IsOpen: true })
            return;

        _openingFolderMenu = true;
        try
        {
            // Paste is enabled only when there are files to paste
            var canPaste = await MainViewModel.CanPasteIntoAsync(e.Page);

            // Opened on the window, placed at the view, for the same reason as the entries' menu
            // (MainWindow.EntryMenu.cs): clicks on a menu that is the view's child would reach its rubber band
            var menu = new ContextMenu
            {
                Placement = e.AtPointer ? PlacementMode.Pointer : PlacementMode.Center,
                PlacementTarget = e.Target,
            };

            foreach (var entry in BuildFolderMenu(vm, e.Page, canPaste))
                menu.Items.Add(entry);

            _folderMenu = menu;
            menu.Open(this);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Can't open the folder menu: {ex}");
        }
        finally
        {
            _openingFolderMenu = false;
        }
    }

    private static IEnumerable<Control> BuildFolderMenu(MainViewModel vm, DirectoryViewModel page, bool canPaste)
    {
        var canCreate = MainViewModel.CanCreateIn(page);

        yield return ShortcutEntry(vm, "New folder", MaterialIconKind.FolderPlusOutline, KeymapActions.NewFolder,
            () => _ = vm.CreateFolderAsync(page), isEnabled: canCreate);
        yield return ShortcutEntry(vm, "New text document", MaterialIconKind.FilePlusOutline,
            KeymapActions.NewTextDocument, () => _ = vm.CreateTextDocumentAsync(page), isEnabled: canCreate);
        yield return new Separator();

        yield return PasteSubmenu(vm, page, canPaste);
        yield return ShortcutEntry(vm, "Select all", MaterialIconKind.SelectAll, KeymapActions.SelectAll,
            page.SelectAll, isEnabled: page.Entries.Count > 0);
        yield return new Separator();

        yield return EntryMenuItem("Properties", MaterialIconKind.InformationOutline,
            () => _ = vm.ShowFolderPropertiesAsync(page));
    }

    /// <summary>Paste ▸ Paste, Paste with replace, Paste without replace: what happens to a name that is taken.</summary>
    private static MenuItem PasteSubmenu(MainViewModel vm, DirectoryViewModel page, bool canPaste)
    {
        var paste = new MenuItem
        {
            Header = "Paste",
            Icon = new MaterialIcon { Kind = MaterialIconKind.ContentPaste, Width = 16, Height = 16 },
            IsEnabled = canPaste,
        };

        paste.Items.Add(ShortcutEntry(vm, "Paste", MaterialIconKind.ContentPaste, KeymapActions.Paste,
            () => _ = vm.PasteIntoAsync(page, PasteMode.Ask)));
        paste.Items.Add(ShortcutEntry(vm, "Paste with replace", MaterialIconKind.FileReplaceOutline,
            KeymapActions.PasteWithReplace, () => _ = vm.PasteIntoAsync(page, PasteMode.Replace)));
        paste.Items.Add(ShortcutEntry(vm, "Paste without replace", MaterialIconKind.ContentDuplicate,
            KeymapActions.PasteWithoutReplace, () => _ = vm.PasteIntoAsync(page, PasteMode.KeepBoth)));

        return paste;
    }
}
