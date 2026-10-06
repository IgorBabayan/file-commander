using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Material.Icons;
using Material.Icons.Avalonia;

namespace File.Commander.Presentation.Views.Shell;

/// <summary>
/// The title bar's toolbar and Customize Toolbar…, as in Firefox. A right click anywhere on the title bar opens a
/// menu with "Customize Toolbar…". While customizing, the window's content gives way to the palette
/// (<see cref="ToolbarCustomizer"/>), the toolbar's items stop acting and are dragged instead: along the toolbar
/// to move them, onto the palette to take them off, and from the palette onto the toolbar to add them. The dragged
/// item follows the pointer, and shows faded where it would land. Done or Esc closes it.
/// </summary>
public partial class MainWindow
{
    private const string CustomizingClass = "customizing";
    private const string ToolbarItemClass = "toolbar-item";
    private const string SpringClass = "spring";
    private const string NavGroupClass = "nav-group";
    private const string PaletteItemClass = "palette-item";
    private const double GroupCornerRadius = 8;

    // The control of every built-in item, by id. The ones that aren't on the toolbar wait in ToolbarPool,
    // hidden, rather than detached: the Action center checks its visibility before opening its panel
    private readonly Dictionary<string, Control> _toolbarControls = new(StringComparer.Ordinal);

    // The Border.toolbar-item around each item on the toolbar
    private readonly Dictionary<ToolbarEntry, Border> _toolbarHosts = new();

    private ToolbarViewModel? _toolbar;

    // A right press went down on the title bar: its menu opens on release, like every context menu
    private bool _toolbarMenuPending;
    private ContextMenu? _toolbarMenu;

    private ToolbarDrag? _toolbarDrag;

    private enum ToolbarDropTarget
    {
        None,
        Toolbar,
        Palette,
    }

    /// <summary>An item pressed while customizing: a drag once the pointer goes past the threshold.</summary>
    private sealed class ToolbarDrag(
        ToolbarEntry entry,
        bool fromPalette,
        Control source,
        IPointer pointer,
        Point start,
        Point grip,
        IReadOnlyList<ToolbarEntry> original)
    {
        /// <summary>The item; from the palette, a new entry that joins the toolbar when it is dragged there.</summary>
        public ToolbarEntry Entry { get; } = entry;

        public bool FromPalette { get; } = fromPalette;

        /// <summary>What was pressed: the picture that follows the pointer is taken from it.</summary>
        public Control Source { get; } = source;

        public IPointer Pointer { get; } = pointer;

        /// <summary>Where it was pressed, in window coordinates.</summary>
        public Point Start { get; } = start;

        /// <summary>Where the item was grabbed, from its top left corner.</summary>
        public Point Grip { get; } = grip;

        /// <summary>The toolbar before the drag: dropped elsewhere, the item goes back.</summary>
        public IReadOnlyList<ToolbarEntry> Original { get; } = original;

        public bool Started { get; set; }

        public Image? Ghost { get; set; }

        public RenderTargetBitmap? Bitmap { get; set; }
    }

    private void InitializeToolbar()
    {
        foreach (var control in ToolbarPool.Children.ToList())
        {
            if (control.Tag is string id)
                _toolbarControls[id] = control;
        }

        // Tunnel: before the items see the press (and, while customizing, act on it)
        TitleBar.AddHandler(PointerPressedEvent, OnTitleBarMenuPointerPressed, RoutingStrategies.Tunnel);
        TitleBar.AddHandler(PointerReleasedEvent, OnTitleBarMenuPointerReleased, RoutingStrategies.Tunnel);
        ToolbarArea.AddHandler(PointerPressedEvent, OnToolbarItemPointerPressed, RoutingStrategies.Tunnel);
        Customizer.ItemsHost.AddHandler(PointerPressedEvent, OnPaletteItemPointerPressed, RoutingStrategies.Tunnel);

        // A drag captures the pointer on the window: it goes from the title bar to the palette and back
        AddHandler(PointerMovedEvent, OnToolbarDragPointerMoved, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, OnToolbarDragPointerReleased, RoutingStrategies.Tunnel);
        AddHandler(PointerCaptureLostEvent, OnToolbarDragCaptureLost);
        AddHandler(KeyDownEvent, OnToolbarKeyDown, RoutingStrategies.Tunnel);

        DataContextChanged += (_, _) => AttachToolbar((DataContext as MainViewModel)?.Toolbar);
    }

    private void AttachToolbar(ToolbarViewModel? toolbar)
    {
        if (ReferenceEquals(toolbar, _toolbar))
            return;

        CancelToolbarDrag();

        if (_toolbar is not null)
        {
            _toolbar.LayoutChanged -= OnToolbarLayoutChanged;
            _toolbar.PropertyChanged -= OnToolbarPropertyChanged;
        }

        _toolbar = toolbar;

        if (_toolbar is not null)
        {
            _toolbar.LayoutChanged += OnToolbarLayoutChanged;
            _toolbar.PropertyChanged += OnToolbarPropertyChanged;
        }

        RebuildToolbar();
        ApplyCustomizing();
    }

    private void OnToolbarLayoutChanged(object? sender, EventArgs e) => RebuildToolbar();

    private void OnToolbarPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ToolbarViewModel.IsCustomizing))
            ApplyCustomizing();
    }

    // ===================== Context menu =====================

    private void OnTitleBarMenuPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _toolbarMenuPending = false;

        if (_toolbar is not { } toolbar || !e.GetCurrentPoint(TitleBar).Properties.IsRightButtonPressed)
            return;

        // The address bar while typing keeps the text box's own menu: cut, copy, paste
        if (!toolbar.IsCustomizing && e.Source is Visual source
                                   && source.FindAncestorOfType<TextBox>(includeSelf: true) is not null)
            return;

        // While customizing the menu isn't offered, and items take no clicks
        _toolbarMenuPending = !toolbar.IsCustomizing;
        e.Handled = true;
    }

    private void OnTitleBarMenuPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Right || !_toolbarMenuPending)
            return;

        _toolbarMenuPending = false;
        e.Handled = true;
        OpenToolbarMenu();
    }

    private void OpenToolbarMenu()
    {
        if (_toolbar is not { IsCustomizing: false } toolbar || _toolbarMenu is { IsOpen: true })
            return;

        // As in Firefox: the one entry, with C as its access key
        var customize = new MenuItem { Header = "_Customize Toolbar…" };
        customize.Click += (_, _) => toolbar.CustomizeCommand.Execute(null);

        var menu = new ContextMenu { Placement = PlacementMode.Pointer };
        menu.Items.Add(customize);

        // Kept so a second request doesn't stack another menu, and so customizing can close it
        _toolbarMenu = menu;
        menu.Open(TitleBar);
    }

    /// <summary>
    /// A row of the New or Paste menu was picked: the menu closes. Posted, so the row's command runs first
    /// while the menu still holds its DataContext.
    /// </summary>
    private void OnFileMenuRowClick(object? sender, RoutedEventArgs e)
        => Dispatcher.UIThread.Post(() =>
        {
            NewButton.Flyout?.Hide();
            PasteButton.Flyout?.Hide();
        });

    // ===================== Toolbar items =====================

    /// <summary>Puts a host on the toolbar for every entry, in order; hosts of entries that left give their control back to the pool.</summary>
    private void RebuildToolbar()
    {
        var entries = _toolbar?.Entries ?? [];

        foreach (var (entry, host) in _toolbarHosts.ToList())
        {
            if (entries.Contains(entry))
                continue;

            ReleaseHost(host);
            _toolbarHosts.Remove(entry);
        }

        var hosts = new List<Border>();
        foreach (var entry in entries)
        {
            if (!_toolbarHosts.TryGetValue(entry, out var host))
            {
                if (CreateHost(entry) is not { } created)
                    continue;

                _toolbarHosts[entry] = host = created;
            }

            hosts.Add(host);
        }

        if (!ToolbarArea.Children.SequenceEqual(hosts))
        {
            ToolbarArea.Children.Clear();
            ToolbarArea.Children.AddRange(hosts);
        }

        UpdateToolbarGroups(hosts);
        UpdateToolbarHosts();
    }

    private Border? CreateHost(ToolbarEntry entry)
    {
        var info = entry.Info;
        var isSpring = info.Id == ToolbarViewModel.FlexibleSpaceId;
        Control content;

        if (isSpring)
        {
            // Only shown while customizing: something to see and to grab
            content = new MaterialIcon
            {
                Kind = MaterialIconKind.ArrowExpandHorizontal,
                Width = 16,
                Height = 16,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            };
        }
        else if (_toolbarControls.TryGetValue(info.Id, out var control))
        {
            ToolbarPool.Children.Remove(control);
            content = control;
        }
        else
        {
            return null;
        }

        var host = new Border { Tag = entry, Child = content };
        host.Classes.Add(ToolbarItemClass);
        if (isSpring)
            host.Classes.Add(SpringClass);

        // Back / forward / up and the file buttons: side by side they share the pill of Border.nav-group
        // (MainWindow.axaml), each host drawing its part of it
        if (info.Group is not null && info.HasGroupBackground)
            host.Classes.Add(NavGroupClass);

        // Springs take theirs from the styles: wider while customizing
        if (info.MinWidth > 0)
            host.MinWidth = info.MinWidth;

        ToolbarPanel.SetFlex(host, info.Flex);
        ToolbarPanel.SetGroup(host, info.Group);
        ToolbarPanel.SetGroupSpacing(host, info.GroupSpacing);
        return host;
    }

    private void ReleaseHost(Border host)
    {
        var content = host.Child;
        host.Child = null;
        ToolbarArea.Children.Remove(host);

        if (content?.Tag is string id && _toolbarControls.ContainsKey(id))
        {
            content.IsHitTestVisible = true;
            ToolbarPool.Children.Add(content);
        }
    }

    /// <summary>Neighbors of a group with a background form one pill: only its outer corners are rounded.</summary>
    private static void UpdateToolbarGroups(List<Border> hosts)
    {
        for (var i = 0; i < hosts.Count; i++)
        {
            if (!hosts[i].Classes.Contains(NavGroupClass) || ToolbarPanel.GetGroup(hosts[i]) is not { } group)
                continue;

            var joinsPrevious = i > 0 && ToolbarPanel.GetGroup(hosts[i - 1]) == group;
            var joinsNext = i + 1 < hosts.Count && ToolbarPanel.GetGroup(hosts[i + 1]) == group;
            var left = joinsPrevious ? 0 : GroupCornerRadius;
            var right = joinsNext ? 0 : GroupCornerRadius;
            hosts[i].CornerRadius = new CornerRadius(left, right, right, left);
        }
    }

    private void UpdateToolbarHosts()
    {
        var customizing = _toolbar?.IsCustomizing == true;
        var dragged = _toolbarDrag is { Started: true } drag ? drag.Entry : null;

        foreach (var (entry, host) in _toolbarHosts)
        {
            // While customizing the host takes the clicks: the item is something to drag, not to use
            if (host.Child is { } content && !host.Classes.Contains(SpringClass))
                content.IsHitTestVisible = !customizing;

            ToolTip.SetTip(host, customizing ? entry.Info.Label : null);
            host.Classes.Set(DraggingClass, ReferenceEquals(entry, dragged));
        }
    }

    private void ApplyCustomizing()
    {
        var customizing = _toolbar?.IsCustomizing == true;
        if (!customizing)
            CancelToolbarDrag();

        ToolbarArea.Classes.Set(CustomizingClass, customizing);
        Customizer.IsVisible = customizing;
        UpdateToolbarHosts();

        if (customizing)
        {
            _toolbarMenu?.Close();

            // Out of the address bar: typing there would end up in a control that is now being dragged
            Focus();
        }
    }

    // ===================== Dragging =====================

    private void OnToolbarItemPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_toolbar is not { IsCustomizing: true } toolbar || e.Source is not Visual source)
            return;

        var host = source.GetSelfAndVisualAncestors()
            .OfType<Border>()
            .FirstOrDefault(border => border.Tag is ToolbarEntry && ReferenceEquals(border.GetVisualParent(), ToolbarArea));

        if (host?.Tag is not ToolbarEntry entry)
            return;

        // The item doesn't act, and the press doesn't move the window
        e.Handled = true;

        if (_toolbarDrag is null && e.GetCurrentPoint(host).Properties.IsLeftButtonPressed)
            BeginToolbarPress(toolbar, entry, fromPalette: false, host, e);
    }

    private void OnPaletteItemPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_toolbar is not { IsCustomizing: true } toolbar || e.Source is not Visual source)
            return;

        var tile = source.GetSelfAndVisualAncestors()
            .OfType<Control>()
            .FirstOrDefault(control => control.Classes.Contains(PaletteItemClass));

        if (tile?.DataContext is not ToolbarItemInfo info)
            return;

        e.Handled = true;

        if (_toolbarDrag is null && e.GetCurrentPoint(tile).Properties.IsLeftButtonPressed)
            BeginToolbarPress(toolbar, new ToolbarEntry(info), fromPalette: true, tile, e);
    }

    private void BeginToolbarPress(ToolbarViewModel toolbar, ToolbarEntry entry, bool fromPalette, Control source,
        PointerPressedEventArgs e)
    {
        _toolbarDrag = new ToolbarDrag(entry, fromPalette, source, e.Pointer, e.GetPosition(this),
            e.GetPosition(source), toolbar.Entries);

        e.Pointer.Capture(this);
    }

    private void OnToolbarDragPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_toolbarDrag is not { } drag || _toolbar is not { } toolbar)
            return;

        e.Handled = true;

        if (!drag.Started)
        {
            var delta = e.GetPosition(this) - drag.Start;
            if (Math.Abs(delta.X) < DragThreshold && Math.Abs(delta.Y) < DragThreshold)
                return;

            drag.Started = true;
            ShowDragGhost(drag);
            Cursor = new Cursor(StandardCursorType.DragMove);
            UpdateToolbarHosts();
        }

        MoveDragGhost(drag, e);
        PreviewToolbarDrop(drag, toolbar, e);
    }

    /// <summary>
    /// Shows the drop before it happens. Over the toolbar the item takes the place the pointer points at; over the
    /// palette it leaves the toolbar (unless it can't be removed); elsewhere nothing changes.
    /// </summary>
    private void PreviewToolbarDrop(ToolbarDrag drag, ToolbarViewModel toolbar, PointerEventArgs e)
    {
        var target = DropTargetAt(e);
        var removable = drag.Entry.Info.IsRemovable;

        Customizer.DropArea.Classes.Set(DropTargetClass,
            target == ToolbarDropTarget.Palette && removable && !drag.FromPalette);

        switch (target)
        {
            case ToolbarDropTarget.Toolbar:
                // Before every item whose middle is right of the pointer. The others move away from the pointer
                // when the item passes them, so it doesn't jump back and forth
                var x = e.GetPosition(ToolbarArea).X;
                var others = toolbar.Entries.Where(entry => !ReferenceEquals(entry, drag.Entry)).ToList();
                var index = others.Count(other => _toolbarHosts.TryGetValue(other, out var host)
                                                  && host.Bounds.Center.X < x);
                others.Insert(index, drag.Entry);
                toolbar.Preview(others);
                break;

            case ToolbarDropTarget.Palette when removable:
                toolbar.Preview(toolbar.Entries.Where(entry => !ReferenceEquals(entry, drag.Entry)).ToList());
                break;
        }
    }

    private void OnToolbarDragPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_toolbarDrag is not { } drag || _toolbar is not { } toolbar)
            return;

        e.Handled = true;
        var target = drag.Started ? DropTargetAt(e) : ToolbarDropTarget.None;

        // First: losing the capture below must not undo the drop
        EndToolbarDrag();
        drag.Pointer.Capture(null);

        // A click: items do nothing while customizing
        if (!drag.Started)
            return;

        if (target == ToolbarDropTarget.None)
        {
            // Dropped somewhere else: the item goes back where it was
            toolbar.Preview(drag.Original);
            return;
        }

        toolbar.Commit();
    }

    /// <summary>The window lost the pointer mid-drag (e.g. it was deactivated): the item goes back.</summary>
    private void OnToolbarDragCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        // Only the window's own capture: the item that had the press loses it when the drag takes it over
        if (ReferenceEquals(e.Source, this))
            CancelToolbarDrag();
    }

    private void OnToolbarKeyDown(object? sender, KeyEventArgs e)
    {
        if (_toolbar is not { IsCustomizing: true } toolbar || e.Key != Key.Escape)
            return;

        e.Handled = true;

        if (_toolbarDrag is not null)
            CancelToolbarDrag();
        else
            toolbar.DoneCommand.Execute(null);
    }

    private void CancelToolbarDrag()
    {
        if (_toolbarDrag is not { } drag)
            return;

        EndToolbarDrag();
        drag.Pointer.Capture(null);

        if (drag.Started)
            _toolbar?.Preview(drag.Original);
    }

    private void EndToolbarDrag()
    {
        if (_toolbarDrag is not { } drag)
            return;

        _toolbarDrag = null;

        if (drag.Ghost is not null)
            DragLayer.Children.Remove(drag.Ghost);

        drag.Bitmap?.Dispose();
        Cursor = null;
        Customizer.DropArea.Classes.Set(DropTargetClass, false);
        UpdateToolbarHosts();
    }

    private ToolbarDropTarget DropTargetAt(PointerEventArgs e)
    {
        if (IsPointerOver(ToolbarArea, e))
            return ToolbarDropTarget.Toolbar;

        if (Customizer.IsVisible && IsPointerOver(Customizer.DropArea, e))
            return ToolbarDropTarget.Palette;

        return ToolbarDropTarget.None;
    }

    private new static bool IsPointerOver(Visual visual, PointerEventArgs e)
        => new Rect(visual.Bounds.Size).Contains(e.GetPosition(visual));

    /// <summary>A picture of the pressed item that follows the pointer, as the system's drag image does in Firefox.</summary>
    private void ShowDragGhost(ToolbarDrag drag)
    {
        var size = drag.Source.Bounds.Size;
        if (size.Width < 1 || size.Height < 1)
            return;

        var scaling = RenderScaling;
        var bitmap = new RenderTargetBitmap(
            new PixelSize(Math.Max(1, (int)Math.Ceiling(size.Width * scaling)),
                Math.Max(1, (int)Math.Ceiling(size.Height * scaling))),
            new Vector(96 * scaling, 96 * scaling));
        bitmap.Render(drag.Source);

        drag.Bitmap = bitmap;
        drag.Ghost = new Image
        {
            Source = bitmap,
            Width = size.Width,
            Height = size.Height,
            Opacity = 0.85,
            IsHitTestVisible = false,
        };

        DragLayer.Children.Add(drag.Ghost);
    }

    private void MoveDragGhost(ToolbarDrag drag, PointerEventArgs e)
    {
        if (drag.Ghost is not { } ghost)
            return;

        var position = e.GetPosition(DragLayer);
        Canvas.SetLeft(ghost, position.X - drag.Grip.X);
        Canvas.SetTop(ghost, position.Y - drag.Grip.Y);
    }
}
