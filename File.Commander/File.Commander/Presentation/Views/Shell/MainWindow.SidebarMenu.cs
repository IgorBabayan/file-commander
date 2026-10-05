using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Material.Icons;
using Material.Icons.Avalonia;

namespace File.Commander.Presentation.Views.Shell;

/// <summary>
/// The sidebar's context menu, on a right click or the menu key (Shift+F10). Built per opening:
/// what it offers depends on the item (<see cref="SidebarItemKind"/>) and, for Empty trash, on the trash.
/// </summary>
public partial class MainWindow
{
    // The item a right press went down on: its menu opens on release, like every context menu
    private SidebarItem? _menuItem;
    private ContextMenu? _sidebarMenu;

    private void InitializeSidebarMenu()
    {
        // Tunnel: the press must not reach the ListBoxItem, or the right click would select (open) the item
        SidebarList.AddHandler(PointerPressedEvent, OnSidebarMenuPointerPressed, RoutingStrategies.Tunnel);
        SidebarList.AddHandler(PointerReleasedEvent, OnSidebarMenuPointerReleased, RoutingStrategies.Tunnel);
        SidebarList.AddHandler(ContextRequestedEvent, OnSidebarContextRequested);
    }

    private void OnSidebarMenuPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _menuItem = null;

        if (ViewModel is null || e.Source is not Visual source
            || !e.GetCurrentPoint(SidebarList).Properties.IsRightButtonPressed)
            return;

        // The eject button handles its own clicks
        if (source.FindAncestorOfType<Button>(includeSelf: true) is not null || SidebarItemAt(source) is not { } item)
            return;

        _menuItem = item;
        e.Handled = true;
    }

    private void OnSidebarMenuPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Right || _menuItem is not { } item)
            return;

        _menuItem = null;

        // Handled: the item would raise ContextRequested for the same click
        e.Handled = true;
        OpenSidebarMenu(item, PlacementMode.Pointer);
    }

    /// <summary>The menu key or Shift+F10 on the focused item. There is no pointer position: the menu opens below the item.</summary>
    private void OnSidebarContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (e.Source is not Visual source || SidebarItemAt(source) is not { } item)
            return;

        e.Handled = true;
        OpenSidebarMenu(item, e.TryGetPosition(SidebarList, out _) ? PlacementMode.Pointer : PlacementMode.BottomEdgeAlignedLeft);
    }

    private void OpenSidebarMenu(SidebarItem item, PlacementMode placement)
    {
        if (ViewModel is not { } vm || _sidebarMenu is { IsOpen: true }
            || SidebarList.ContainerFromItem(item) is not Control container)
            return;

        var menu = new ContextMenu { Placement = placement };
        foreach (var entry in BuildSidebarMenu(vm, item))
            menu.Items.Add(entry);

        // Kept so a second request (e.g. the menu key right after a click) doesn't stack another menu
        _sidebarMenu = menu;
        menu.Open(container);
    }

    private static IEnumerable<Control> BuildSidebarMenu(MainViewModel vm, SidebarItem item)
    {
        yield return MenuEntry("Open", MaterialIconKind.FolderOpenOutline, () => vm.OpenSidebarItem(item));
        yield return MenuEntry("Open in split view", MaterialIconKind.ViewSplitVertical,
            () => vm.OpenSidebarItemInSplitView(item));
        yield return MenuEntry("Open in new tab", MaterialIconKind.TabPlus, () => vm.OpenSidebarItemInNewTab(item));
        yield return new Separator();

        switch (SidebarViewModel.KindOf(item))
        {
            case SidebarItemKind.Recent:
                yield return MenuEntry("File history settings", MaterialIconKind.History,
                    () => _ = vm.OpenFileHistorySettingsAsync());
                break;

            case SidebarItemKind.Trash:
                yield return MenuEntry("Trash settings", MaterialIconKind.CogOutline,
                    () => _ = vm.OpenTrashSettingsAsync());
                yield return MenuEntry("Empty trash", MaterialIconKind.DeleteForeverOutline,
                    () => _ = vm.EmptyTrashAsync(), isEnabled: vm.CanEmptyTrash());
                yield return new Separator();
                yield return PropertiesEntry(vm, item);
                break;

            case SidebarItemKind.Favorite:
                yield return PropertiesEntry(vm, item);
                yield return new Separator();
                yield return MenuEntry("Rename", MaterialIconKind.PencilOutline, () => _ = vm.RenameFavoriteAsync(item));
                yield return new Separator();
                yield return MenuEntry("Remove from favorites", MaterialIconKind.StarOffOutline,
                    () => vm.RemoveFavorite(item));
                break;

            default:
                yield return PropertiesEntry(vm, item);
                break;
        }
    }

    private static MenuItem PropertiesEntry(MainViewModel vm, SidebarItem item)
        => MenuEntry("Properties", MaterialIconKind.InformationOutline, () => _ = vm.ShowSidebarItemPropertiesAsync(item));

    private static MenuItem MenuEntry(string header, MaterialIconKind icon, Action action, bool isEnabled = true)
    {
        var entry = new MenuItem
        {
            Header = header,
            Icon = new MaterialIcon { Kind = icon, Width = 16, Height = 16 },
            IsEnabled = isEnabled,
        };

        entry.Click += (_, _) => action();
        return entry;
    }
}
