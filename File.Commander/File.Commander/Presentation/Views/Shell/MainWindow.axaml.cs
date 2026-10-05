using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using File.Commander.Application.Path;
using File.Commander.Presentation.Views.Browser;

namespace File.Commander.Presentation.Views.Shell;

public partial class MainWindow : Window
{
    /// <summary>How far (in px) the pointer moves with the button down before a click becomes a drag.</summary>
    private const double DragThreshold = 4;

    private const string DraggingClass = "dragging";

    private const string DropTargetClass = "drop-target";

    private MainViewModel? _subscribed;

    // Sidebar drag: the pressed item, where it was pressed, and whether the pointer went past the threshold
    private SidebarItem? _dragItem;
    private Avalonia.Point _dragStart;
    private bool _dragging;

    // Drag from a folder view over the sidebar: the drag being followed, and whether it brings a new favorite.
    // DragOver fires on every move, so the folders are checked once per drag.
    private IDataTransfer? _dropData;
    private bool _dropAddsFavorite;

    public MainWindow()
    {
        InitializeComponent();

        // Tunnel: works wherever the pointer or focus is, before a child control can swallow the event
        AddHandler(PointerPressedEvent, OnNavigationPointerPressed, RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, OnNavigationKeyDown, RoutingStrategies.Tunnel);

        // Tab into a view: it becomes the active one, like a click
        AddHandler(GotFocusEvent, (_, e) => ActivatePaneOf(e.Source));

        // Tunnel: the press must not reach the ListBoxItem, or it would select (navigate) on mouse down
        SidebarList.AddHandler(PointerPressedEvent, OnSidebarPointerPressed, RoutingStrategies.Tunnel);
        SidebarList.AddHandler(PointerMovedEvent, OnSidebarPointerMoved, RoutingStrategies.Tunnel);
        SidebarList.AddHandler(PointerReleasedEvent, OnSidebarPointerReleased, RoutingStrategies.Tunnel);
        SidebarList.AddHandler(PointerCaptureLostEvent, OnSidebarPointerCaptureLost);

        // Folders dragged from a view are dropped on the sidebar as favorites
        DragDrop.SetAllowDrop(SidebarList, true);
        DragDrop.AddDragEnterHandler(SidebarList, OnSidebarDragOver);
        DragDrop.AddDragOverHandler(SidebarList, OnSidebarDragOver);
        DragDrop.AddDragLeaveHandler(SidebarList, OnSidebarDragLeave);
        DragDrop.AddDropHandler(SidebarList, OnSidebarDrop);

        // Right click and the menu key on an item (MainWindow.SidebarMenu.cs)
        InitializeSidebarMenu();
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_subscribed is not null)
            _subscribed.PropertyChanged -= OnViewModelPropertyChanged;

        _subscribed = ViewModel;

        if (_subscribed is not null)
            _subscribed.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // After the old tab is hidden and the new one shown
        if (e.PropertyName is nameof(MainViewModel.ActiveTab))
            Dispatcher.UIThread.Post(FocusActivePane, DispatcherPriority.Loaded);
    }

    /// <summary>
    /// Another tab was selected: keyboard focus would stay in the now hidden one, so the arrow keys
    /// would move its selection. Moves focus to the file list of the selected tab's active view.
    /// </summary>
    private void FocusActivePane()
    {
        if (ViewModel is not { } vm)
            return;

        var paneView = this.GetVisualDescendants()
            .OfType<PaneView>()
            .FirstOrDefault(view => ReferenceEquals(view.DataContext, vm.ActivePane) && view.IsEffectivelyVisible);

        var candidates = paneView?.GetVisualDescendants()
            .OfType<InputElement>()
            .Where(element => element.Focusable && element.IsEffectivelyVisible && element.IsEffectivelyEnabled)
            .ToList() ?? [];

        // The file list; on a page without one (Computer…) its first control, e.g. a card
        var target = candidates.FirstOrDefault(element => element is ListBox or TreeView)
                     ?? candidates.FirstOrDefault();

        // Nothing to focus in the view: at least take focus out of the hidden tab
        if (target is null || !target.Focus())
            Focus();
    }

    /// <summary>A click on a tab in the tab bar selects it, a middle click closes it.</summary>
    private void OnTabPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is not Control { DataContext: TabViewModel tab })
            return;

        var command = e.InitialPressMouseButton switch
        {
            MouseButton.Left => tab.SelectCommand,
            MouseButton.Middle => tab.CloseCommand,
            _ => null,
        };

        if (TryExecute(command))
            e.Handled = true;
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs args)
    {
        if (!args.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        if (args.ClickCount == 2 && !DesktopEnvironmentHelper.IsHyprland())
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;

            return;
        }

        BeginMoveDrag(args);
    }

    /// <summary>Any click into a view makes it the active one. Mouse side buttons: back / forward.</summary>
    private void OnNavigationPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (ViewModel is not { } vm)
            return;

        // First, so the view itself and the side buttons below act on the view that was clicked
        ActivatePaneOf(e.Source);

        var properties = e.GetCurrentPoint(this).Properties;
        var command = properties.IsXButton1Pressed ? vm.GoBackCommand
            : properties.IsXButton2Pressed ? vm.GoForwardCommand
            : null;

        if (TryExecute(command))
            e.Handled = true;
    }

    /// <summary>
    /// Sidebar items open on release, so a press can still turn into a drag. A drag reorders the
    /// items inside their section as the pointer moves; the order is saved on release.
    /// </summary>
    private void OnSidebarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // Touch keeps the ListBox's own behavior: a drag there is a scroll
        if (ViewModel is null || e.Pointer.Type == PointerType.Touch || e.KeyModifiers != KeyModifiers.None)
            return;

        var point = e.GetCurrentPoint(SidebarList);
        if (!point.Properties.IsLeftButtonPressed || e.Source is not Avalonia.Visual source)
            return;

        // The eject button handles its own clicks
        if (source.FindAncestorOfType<Button>(includeSelf: true) is not null || SidebarItemAt(source) is not { } item)
            return;

        _dragItem = item;
        _dragStart = point.Position;
        _dragging = false;

        e.Pointer.Capture(SidebarList);
        e.Handled = true;
    }

    private void OnSidebarPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragItem is not { } item || ViewModel is not { } vm)
            return;

        var position = e.GetPosition(SidebarList);
        if (!_dragging)
        {
            var delta = position - _dragStart;
            if (Math.Abs(delta.X) < DragThreshold && Math.Abs(delta.Y) < DragThreshold)
                return;

            _dragging = true;
            SidebarList.Cursor = new Cursor(StandardCursorType.DragMove);
        }

        // All items have the same height: after a move the pointer is over the dragged item, so it doesn't jitter
        if (SidebarList.InputHitTest(position) is Avalonia.Visual hit && SidebarItemAt(hit) is { } target)
            vm.Sidebar.MoveTo(item, target);

        // Every time: a move can give the dragged item another container
        MarkDragged(item);
        e.Handled = true;
    }

    private void OnSidebarPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragItem is not { } item || ViewModel is not { } vm)
            return;

        var dragged = _dragging;
        EndSidebarDrag();
        e.Pointer.Capture(null);
        e.Handled = true;

        if (dragged)
        {
            vm.Sidebar.CommitOrder();
            return;
        }

        // A plain click: what the ListBox would have done on press
        SidebarList.ContainerFromItem(item)?.Focus(NavigationMethod.Pointer);
        vm.Sidebar.SelectedEntry = item;
    }

    /// <summary>The window lost the pointer mid-drag (e.g. it was deactivated): keep where the item got to.</summary>
    private void OnSidebarPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (_dragItem is null)
            return;

        var dragged = _dragging;
        EndSidebarDrag();

        if (dragged)
            ViewModel?.Sidebar.CommitOrder();
    }

    private void EndSidebarDrag()
    {
        _dragItem = null;
        _dragging = false;
        SidebarList.Cursor = null;
        MarkDragged(null);
    }

    private void MarkDragged(SidebarItem? item)
    {
        foreach (var container in SidebarList.GetRealizedContainers())
            container.Classes.Set(DraggingClass, item is not null && ReferenceEquals(container.DataContext, item));
    }

    /// <summary>Entering and moving over the sidebar: accepts the drag when it brings a folder that isn't a favorite yet.</summary>
    private void OnSidebarDragOver(object? sender, DragEventArgs e)
    {
        if (!ReferenceEquals(e.DataTransfer, _dropData))
        {
            _dropData = e.DataTransfer;
            _dropAddsFavorite = ViewModel is { } vm
                                && FileDragData.TryGetPaths(e.DataTransfer) is { } paths
                                && vm.Sidebar.CanAddFavorites(paths);
        }

        e.DragEffects = _dropAddsFavorite ? DragDropEffects.Link : DragDropEffects.None;
        SidebarList.Classes.Set(DropTargetClass, _dropAddsFavorite);
        e.Handled = true;
    }

    private void OnSidebarDragLeave(object? sender, DragEventArgs e) => EndSidebarDrop();

    /// <summary>Adds the dropped folders to "Favorites"; the section appears with the first one.</summary>
    private void OnSidebarDrop(object? sender, DragEventArgs e)
    {
        var added = ViewModel is { } vm
                    && FileDragData.TryGetPaths(e.DataTransfer) is { } paths
                    && vm.Sidebar.AddFavorites(paths);

        e.DragEffects = added ? DragDropEffects.Link : DragDropEffects.None;
        e.Handled = true;
        EndSidebarDrop();
    }

    private void EndSidebarDrop()
    {
        _dropData = null;
        _dropAddsFavorite = false;
        SidebarList.Classes.Set(DropTargetClass, false);
    }

    private static SidebarItem? SidebarItemAt(Avalonia.Visual visual)
        => visual.FindAncestorOfType<ListBoxItem>(includeSelf: true)?.DataContext as SidebarItem;

    /// <summary>Split view: makes the view that contains <paramref name="source"/> the active one (selected tab only).</summary>
    private void ActivatePaneOf(object? source)
    {
        if (ViewModel is { } vm
            && source is Avalonia.Visual visual
            && visual.FindAncestorOfType<PaneView>(includeSelf: true)?.DataContext is PaneViewModel pane)
            vm.ActivatePane(pane);
    }

    /// <summary>
    /// Shortcuts of Settings → Basic → Keymap (Ctrl+L, F5, Ctrl+1/2/3, Alt+Left… by default).
    /// While the user types (address bar, a future search box) Backspace, arrows and plain keys stay with the text box.
    /// </summary>
    private void OnNavigationKeyDown(object? sender, KeyEventArgs e)
    {
        if (ViewModel is { } vm && vm.HandleKey(e.Key, e.KeyModifiers, isTyping: e.Source is TextBox))
            e.Handled = true;
    }

    private static bool TryExecute(IRelayCommand? command)
    {
        if (command is null || !command.CanExecute(null))
            return false;

        command.Execute(null);
        return true;
    }

    private void OnMinimizeClick(object? sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void OnMaximizeClick(object? sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.TryShutdown();
        }
    }
}
