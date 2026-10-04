using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Selection;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using File.Commander.Presentation.ViewModels.Browser;

namespace File.Commander.Presentation.Views.Browser;

/// <summary>
/// Rubber-band selection: press the left button and drag to select every entry the rectangle touches.
/// Ctrl+drag adds to the selection. Dragging past the top or bottom edge scrolls.
/// A click on empty space (no drag, no Ctrl) clears the selection.
/// </summary>
/// <remarks>
/// Works in content coordinates (viewport position + scroll offset), so the band keeps its start while
/// the view scrolls. The list and the tree are single columns: the band selects the rows between its
/// top and bottom edges, by index, so rows scrolled out (and no longer realized) stay selected.
/// The grid doesn't virtualize: every tile has a container to hit-test.
/// </remarks>
public partial class DirectoryView
{
    // How far the pointer must move before a press becomes a drag, so a click stays a click
    private const double DragThreshold = 4;

    // Within this distance of the top or bottom edge, or past it, the view scrolls
    private const double AutoScrollMargin = 28;

    private const double AutoScrollMaxStep = 30;

    // The control the left button went down on; null when no press is being followed
    private Control? _bandOwner;
    private ScrollViewer? _bandScroll;

    // Where the press was, in content coordinates
    private Point _bandStart;

    // Last pointer position, relative to the scroll viewer (may be outside it while dragging)
    private Point _bandPointer;

    // Past the drag threshold: the band is drawn and selects
    private bool _bandActive;

    // Ctrl was held: the band adds to what was selected before
    private bool _bandAdds;

    // The press was on no entry: releasing it without a drag clears the selection
    private bool _bandOnEmpty;

    // List and tree: the rows around the press point, the band's fixed end
    private int _bandRowAbove;
    private int _bandRowBelow;

    // Tree: its visible rows when the drag began, and their positions
    private List<FileTreeNodeViewModel> _bandTreeRows = [];
    private Dictionary<FileTreeNodeViewModel, int> _bandTreeIndex = [];

    // What was selected before a Ctrl+drag: list/grid indexes, tree nodes
    private List<int> _bandBaseIndexes = [];
    private List<FileTreeNodeViewModel> _bandBaseNodes = [];

    // The band's last result, so an unchanged one isn't applied again on every pointer move.
    // Null until the first update, which always applies: a drag without Ctrl replaces the selection.
    private List<int>? _bandLast;

    // Captured by the band while it is active; released when the band ends without a button release
    private IPointer? _bandPointerDevice;

    private DispatcherTimer? _bandAutoScroll;
    private bool _bandUpdatePosted;

    // A drag ended over an entry: the Tapped that follows must not open it (Settings → Open file: Click)
    private bool _suppressTap;

    private void InitializeRubberBand()
    {
        foreach (var control in new Control[] { FileList, GridList, FileTree })
        {
            // handledEventsToo: items mark presses as handled when they select
            control.AddHandler(PointerPressedEvent, OnBandPointerPressed, handledEventsToo: true);
            control.AddHandler(PointerMovedEvent, OnBandPointerMoved, handledEventsToo: true);
            control.AddHandler(PointerReleasedEvent, OnBandPointerReleased, handledEventsToo: true);
            control.PointerCaptureLost += (_, _) => EndBand();
        }
    }

    private Control ActiveControl(DirectoryViewModel vm) => vm.IsTreeView ? FileTree : ActiveList(vm);

    /// <summary>True once per drag that ended: the Tapped after it is not a click.</summary>
    private bool ConsumeSuppressedTap()
    {
        var suppressed = _suppressTap;
        _suppressTap = false;
        return suppressed;
    }

    private void OnBandPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // A Tapped that never came (released elsewhere) mustn't eat the next click
        _suppressTap = false;
        EndBand();

        if (sender is not Control owner || _viewModel is not { } vm || !ReferenceEquals(owner, ActiveControl(vm)))
            return;

        if (!e.GetCurrentPoint(owner).Properties.IsLeftButtonPressed || e.ClickCount != 1
            || IsOnChrome(e.Source as Visual, owner)
            || owner.FindDescendantOfType<ScrollViewer>() is not { } scroll)
            return;

        _bandOwner = owner;
        _bandScroll = scroll;
        _bandPointer = e.GetPosition(scroll);
        _bandStart = _bandPointer + scroll.Offset;
        _bandAdds = (e.KeyModifiers & KeyModifiers.Control) != 0;
        _bandOnEmpty = !IsOnItem(e.Source as Visual, owner);
    }

    private void OnBandPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_bandOwner is null || !ReferenceEquals(sender, _bandOwner) || _bandScroll is not { } scroll)
            return;

        if (!e.GetCurrentPoint(_bandOwner).Properties.IsLeftButtonPressed)
        {
            EndBand();
            return;
        }

        _bandPointer = e.GetPosition(scroll);

        if (!_bandActive)
        {
            var moved = _bandPointer - (_bandStart - scroll.Offset);
            if (Math.Abs(moved.X) < DragThreshold && Math.Abs(moved.Y) < DragThreshold)
                return;

            BeginBand(e.Pointer);
            if (!_bandActive)
                return;
        }

        UpdateBand();
        e.Handled = true;
    }

    private void OnBandPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_bandOwner is null || !ReferenceEquals(sender, _bandOwner))
            return;

        if (_bandActive)
        {
            _suppressTap = true;
            e.Handled = true;
        }
        else if (_bandOnEmpty && !_bandAdds && _viewModel is { HasSelection: true } vm)
        {
            vm.SelectNone();
        }

        EndBand();
    }

    /// <summary>Scroll bars and the tree's expander arrows keep their own drag and click.</summary>
    private static bool IsOnChrome(Visual? source, Visual owner)
    {
        for (var visual = source; visual is not null && !ReferenceEquals(visual, owner); visual = visual.GetVisualParent())
        {
            if (visual is ScrollBar or ToggleButton)
                return true;
        }

        return false;
    }

    private static bool IsOnItem(Visual? source, Visual owner)
    {
        for (var visual = source; visual is not null && !ReferenceEquals(visual, owner); visual = visual.GetVisualParent())
        {
            if (visual is ListBoxItem or TreeViewItem)
                return true;
        }

        return false;
    }

    private void BeginBand(IPointer pointer)
    {
        if (_viewModel is not { } vm || _bandOwner is null || _bandScroll is null)
            return;

        _bandActive = true;
        _bandLast = null;

        // Captured by the control: moves outside it still arrive, and the release doesn't reach
        // the item under the pointer (an item reselects itself on release)
        pointer.Capture(_bandOwner);
        _bandPointerDevice = pointer;

        if (vm.IsTreeView)
        {
            _bandTreeRows = vm.VisibleTreeNodes().ToList();
            _bandTreeIndex = new Dictionary<FileTreeNodeViewModel, int>(_bandTreeRows.Count);
            for (var i = 0; i < _bandTreeRows.Count; i++)
                _bandTreeIndex[_bandTreeRows[i]] = i;

            _bandBaseNodes = _bandAdds
                ? FileTree.SelectedItems.OfType<FileTreeNodeViewModel>().Where(n => !n.IsPlaceholder).ToList()
                : [];
        }
        else
        {
            _bandBaseIndexes = _bandAdds ? ActiveList(vm).Selection.SelectedIndexes.ToList() : [];
        }

        // The start point is still on screen, so the rows around it are realized now
        var rows = RealizedRows(vm, _bandScroll);
        var y = _bandStart.Y;
        _bandRowAbove = rows.Where(r => r.Bounds.Top <= y).Select(r => r.Index).DefaultIfEmpty(-1).Max();
        _bandRowBelow = rows.Where(r => r.Bounds.Bottom >= y).Select(r => r.Index).DefaultIfEmpty(int.MaxValue).Min();

        _bandScroll.ScrollChanged += OnBandScrollChanged;
        _bandAutoScroll ??= new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Input, OnBandAutoScrollTick);
        _bandAutoScroll.Start();

        RubberBand.IsVisible = true;
    }

    private void EndBand()
    {
        if (_bandOwner is null && !_bandActive)
            return;

        if (_bandScroll is not null)
            _bandScroll.ScrollChanged -= OnBandScrollChanged;

        _bandAutoScroll?.Stop();
        RubberBand.IsVisible = false;

        var pointer = _bandPointerDevice;
        var owner = _bandOwner;
        _bandPointerDevice = null;
        _bandOwner = null;
        _bandScroll = null;
        _bandActive = false;
        _bandTreeRows = [];
        _bandTreeIndex = [];
        _bandBaseIndexes = [];
        _bandBaseNodes = [];
        _bandLast = null;

        // Last, as releasing the capture raises PointerCaptureLost, which comes back here (and finds nothing to do)
        if (pointer is not null && owner is not null && ReferenceEquals(pointer.Captured, owner))
            pointer.Capture(null);
    }

    private void OnBandScrollChanged(object? sender, ScrollChangedEventArgs e) => PostBandUpdate();

    /// <summary>After layout: rows scrolled into view must be realized before they are hit-tested.</summary>
    private void PostBandUpdate()
    {
        if (_bandUpdatePosted)
            return;

        _bandUpdatePosted = true;
        Dispatcher.UIThread.Post(() =>
        {
            _bandUpdatePosted = false;
            UpdateBand();
        }, DispatcherPriority.Background);
    }

    /// <summary>Scrolls while the pointer is near or past the top or bottom edge, faster the further out it is.</summary>
    private void OnBandAutoScrollTick(object? sender, EventArgs e)
    {
        if (!_bandActive || _bandScroll is not { } scroll)
            return;

        var height = scroll.Viewport.Height;
        var y = _bandPointer.Y;
        var distance = y < AutoScrollMargin ? y - AutoScrollMargin
            : y > height - AutoScrollMargin ? y - (height - AutoScrollMargin)
            : 0;

        if (distance == 0)
            return;

        var step = Math.Clamp(distance / 2, -AutoScrollMaxStep, AutoScrollMaxStep);
        var max = Math.Max(0, scroll.Extent.Height - height);
        var offset = Math.Clamp(scroll.Offset.Y + step, 0, max);

        // ScrollChanged updates the band
        if (offset != scroll.Offset.Y)
            scroll.Offset = new Vector(scroll.Offset.X, offset);
    }

    private void UpdateBand()
    {
        if (!_bandActive || _bandScroll is not { } scroll || _viewModel is not { } vm)
            return;

        // The moving corner never goes past the viewport: nothing is hit out there
        var viewport = scroll.Viewport;
        var pointer = new Point(Math.Clamp(_bandPointer.X, 0, viewport.Width), Math.Clamp(_bandPointer.Y, 0, viewport.Height));
        var end = pointer + scroll.Offset;

        var band = new Rect(
            new Point(Math.Min(_bandStart.X, end.X), Math.Min(_bandStart.Y, end.Y)),
            new Point(Math.Max(_bandStart.X, end.X), Math.Max(_bandStart.Y, end.Y)));

        DrawBand(band, scroll);
        SelectInBand(vm, band, downwards: end.Y >= _bandStart.Y, scroll);
    }

    /// <summary>Shows the part of <paramref name="band"/> (content coordinates) that is on screen.</summary>
    private void DrawBand(Rect band, ScrollViewer scroll)
    {
        var offset = scroll.Offset;
        var viewport = scroll.Viewport;
        var left = Math.Clamp(band.Left - offset.X, 0, viewport.Width);
        var top = Math.Clamp(band.Top - offset.Y, 0, viewport.Height);
        var right = Math.Clamp(band.Right - offset.X, 0, viewport.Width);
        var bottom = Math.Clamp(band.Bottom - offset.Y, 0, viewport.Height);

        if (scroll.TranslatePoint(new Point(left, top), RubberBandLayer) is not { } origin)
            return;

        Canvas.SetLeft(RubberBand, origin.X);
        Canvas.SetTop(RubberBand, origin.Y);
        RubberBand.Width = right - left;
        RubberBand.Height = bottom - top;
    }

    private void SelectInBand(DirectoryViewModel vm, Rect band, bool downwards, ScrollViewer scroll)
    {
        var rows = RealizedRows(vm, scroll);
        List<int> hits;

        if (vm.IsGridView)
        {
            hits = rows.Where(r => r.Bounds.Intersects(band)).Select(r => r.Index).Order().ToList();
        }
        else
        {
            // One column: the fixed end is the row next to the press point, the moving end the last row
            // the band reaches. Everything in between is selected, realized or not.
            int first, last;
            if (downwards)
            {
                first = _bandRowBelow;
                last = rows.Where(r => r.Bounds.Top < band.Bottom).Select(r => r.Index).DefaultIfEmpty(-1).Max();
            }
            else
            {
                first = rows.Where(r => r.Bounds.Bottom > band.Top).Select(r => r.Index).DefaultIfEmpty(int.MaxValue).Min();
                last = _bandRowAbove;
            }

            var count = vm.IsTreeView ? _bandTreeRows.Count : vm.Entries.Count;
            first = Math.Max(first, 0);
            last = Math.Min(last, count - 1);
            hits = first <= last ? Enumerable.Range(first, last - first + 1).ToList() : [];
        }

        if (_bandLast is not null && hits.SequenceEqual(_bandLast))
            return;

        _bandLast = hits;
        ApplyBand(vm, hits);
    }

    private void ApplyBand(DirectoryViewModel vm, List<int> hits)
    {
        _applyingSelection = true;
        try
        {
            if (vm.IsTreeView)
            {
                var selected = FileTree.SelectedItems;
                selected.Clear();

                var nodes = new HashSet<FileTreeNodeViewModel>();
                foreach (var node in _bandBaseNodes.Concat(hits.Select(i => _bandTreeRows[i])))
                {
                    if (nodes.Add(node))
                        selected.Add(node);
                }
            }
            else
            {
                var selection = ActiveList(vm).Selection;
                using (selection.BatchUpdate())
                {
                    selection.Clear();
                    foreach (var index in _bandBaseIndexes)
                        selection.Select(index);

                    // Hits are sorted; a run of them (always, in the list) is one range
                    if (hits.Count > 0 && hits[^1] - hits[0] + 1 == hits.Count)
                        selection.SelectRange(hits[0], hits[^1]);
                    else
                        foreach (var index in hits)
                            selection.Select(index);
                }
            }
        }
        finally
        {
            _applyingSelection = false;
        }

        // Live count in the status bar, and the info panel follows
        ReadSelection(vm);
    }

    /// <summary>The rows that have a container now, with their bounds in content coordinates.</summary>
    private List<(int Index, Rect Bounds)> RealizedRows(DirectoryViewModel vm, ScrollViewer scroll)
    {
        var rows = new List<(int, Rect)>();
        var offset = scroll.Offset;

        if (vm.IsTreeView)
        {
            foreach (var container in FileTree.GetRealizedTreeContainers())
            {
                // Children of a collapsed folder may keep their containers
                if (!container.IsEffectivelyVisible
                    || FileTree.TreeItemFromContainer(container) is not FileTreeNodeViewModel node
                    || !_bandTreeIndex.TryGetValue(node, out var index))
                    continue;

                // The item's own row: its container also spans its expanded children
                if (BoundsIn(HeaderOf(container), scroll, offset) is { } bounds)
                    rows.Add((index, bounds));
            }

            return rows;
        }

        var list = ActiveList(vm);
        foreach (var container in list.GetRealizedContainers())
        {
            // Recycled containers are hidden, not removed
            if (!container.IsVisible)
                continue;

            var index = list.IndexFromContainer(container);
            if (index < 0)
                continue;

            if (BoundsIn(container, scroll, offset) is { } bounds)
                rows.Add((index, bounds));
        }

        return rows;
    }

    private static Rect? BoundsIn(Visual visual, ScrollViewer scroll, Vector offset)
        => visual.TranslatePoint(default, scroll) is { } origin
            ? new Rect(origin + offset, visual.Bounds.Size)
            : null;

    /// <summary>The Fluent template's row border; the whole container if the template has none.</summary>
    private static Visual HeaderOf(Control container)
        => container.GetVisualDescendants().OfType<Border>().FirstOrDefault(b => b.Name == "PART_LayoutRoot")
           ?? (Visual)container;
}
