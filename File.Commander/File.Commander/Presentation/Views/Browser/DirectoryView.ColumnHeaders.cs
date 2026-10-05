using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using File.Commander.Presentation.ViewModels.Browser;

namespace File.Commander.Presentation.Views.Browser;

/// <summary>
/// The column headers of the list and tree. A click sorts (DirectoryViewModel.SortBy), a right-click
/// picks the columns (DirectoryView.axaml), and here:
/// drag a header sideways to move its column; drag the gap on a column's left edge to resize it,
/// double-click that gap to give the column its default width back.
/// </summary>
/// <remarks>
/// The detail columns are anchored to the right edge and Name takes what is left (DetailRowPanel), so the
/// left edge of a column is the one that follows the pointer: dragging it left widens the column and
/// narrows Name. Order and widths live in the shared FileColumnsViewModel, so every view follows.
/// </remarks>
public partial class DirectoryView
{
    // Half the width of the grip around a column's separator (drawn in the middle of the 12 px gap before it)
    private const double ResizeGripHalfWidth = 5;

    private static readonly Cursor ResizeCursor = new(StandardCursorType.SizeWestEast);

    private enum HeaderGesture
    {
        None,

        // Left button down on a header: a click unless it moves past DragThreshold
        Pressed,
        Moving,
        Resizing,
    }

    private HeaderGesture _headerGesture;
    private DetailColumn _headerColumn;
    private Button? _headerButton;

    // Pointer x at the press, header coordinates
    private double _headerPressX;

    // Pointer x minus the header's left edge at the press: keeps the header under the pointer the same way
    private double _headerGrab;

    // The column's width when a resize began
    private double _headerStartWidth;

    // Captured by the header row while moving or resizing
    private IPointer? _headerPointer;

    private readonly TranslateTransform _headerShift = new();

    private void InitializeColumnHeaders()
    {
        // Tunnel: a press on a grip must not reach the header button, which would sort on release
        ColumnHeaders.AddHandler(PointerPressedEvent, OnHeaderPointerPressed, RoutingStrategies.Tunnel);
        // handledEventsToo: the header buttons mark their presses and releases as handled
        ColumnHeaders.AddHandler(PointerMovedEvent, OnHeaderPointerMoved, RoutingStrategies.Bubble, handledEventsToo: true);
        ColumnHeaders.AddHandler(PointerReleasedEvent, OnHeaderPointerReleased, RoutingStrategies.Bubble, handledEventsToo: true);
        ColumnHeaders.PointerCaptureLost += (_, _) => EndHeaderGesture();
        ColumnHeaders.PointerExited += (_, _) =>
        {
            if (_headerGesture == HeaderGesture.None)
                SetHotSeparator(null);
        };
    }

    private void OnHeaderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        EndHeaderGesture();

        if (_viewModel is not { } vm || !e.GetCurrentPoint(ColumnHeaders).Properties.IsLeftButtonPressed)
            return;

        var x = e.GetPosition(ColumnHeaders).X;

        if (GripAt(x) is { } grip)
        {
            e.Handled = true;

            if (e.ClickCount == 2)
            {
                vm.Columns.SetWidth(grip.Column, null);
                return;
            }

            _headerGesture = HeaderGesture.Resizing;
            _headerColumn = grip.Column;
            _headerButton = grip.Header;
            _headerPressX = x;
            _headerStartWidth = grip.Header.Bounds.Width;
            _headerPointer = e.Pointer;
            SetHotSeparator(grip.Column);
            e.Pointer.Capture(ColumnHeaders);
            return;
        }

        // Not handled: unless it turns into a move, the button gets its click and sorts
        if (HeaderAt(e.Source as Visual) is { } header && DetailRowPanel.ColumnOf(header) is { } column)
        {
            _headerGesture = HeaderGesture.Pressed;
            _headerColumn = column;
            _headerButton = header;
            _headerPressX = x;
            _headerGrab = x - header.Bounds.X;
        }
    }

    private void OnHeaderPointerMoved(object? sender, PointerEventArgs e)
    {
        var x = e.GetPosition(ColumnHeaders).X;

        if (_headerGesture == HeaderGesture.None)
        {
            SetHotSeparator(GripAt(x)?.Column);
            return;
        }

        if (!e.GetCurrentPoint(ColumnHeaders).Properties.IsLeftButtonPressed)
        {
            EndHeaderGesture();
            return;
        }

        switch (_headerGesture)
        {
            case HeaderGesture.Resizing:
                Resize(x);
                break;

            case HeaderGesture.Pressed when Math.Abs(x - _headerPressX) < DragThreshold:
                return;

            case HeaderGesture.Pressed:
                BeginHeaderMove(e.Pointer);
                MoveHeader(x);
                break;

            case HeaderGesture.Moving:
                MoveHeader(x);
                break;
        }

        e.Handled = true;
    }

    private void OnHeaderPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        // A move or resize isn't a click
        if (_headerGesture is HeaderGesture.Moving or HeaderGesture.Resizing)
            e.Handled = true;

        EndHeaderGesture();
    }

    private void Resize(double x)
    {
        if (_viewModel is not { } vm)
            return;

        var width = DetailColumnsLayout.Clamp(_headerColumn, _headerStartWidth + (_headerPressX - x));
        vm.Columns.SetWidth(_headerColumn, Math.Round(width));
    }

    private void BeginHeaderMove(IPointer pointer)
    {
        if (_headerButton is not { } header)
            return;

        _headerGesture = HeaderGesture.Moving;
        _headerPointer = pointer;

        // Taken from the button: it drops its pressed state and won't sort on release
        pointer.Capture(ColumnHeaders);

        header.Classes.Add("dragging");
        header.ZIndex = 1;
        header.RenderTransform = _headerShift;
    }

    /// <summary>
    /// Moves the dragged column to where the pointer is: past the middle of a neighbour, they swap.
    /// The rows follow at once; the header itself also follows the pointer between the swaps.
    /// </summary>
    private void MoveHeader(double x)
    {
        if (_viewModel is not { } vm || _headerButton is not { } dragged)
            return;

        var left = x - _headerGrab;
        var center = left + dragged.Bounds.Width / 2;

        var others = VisibleHeaders().Where(h => !ReferenceEquals(h.Header, dragged)).ToList();
        if (others.Count > 0)
        {
            // Hidden columns keep their place in the order, so the target is found among all of them
            var order = vm.Columns.Order.Where(c => c != _headerColumn).ToList();
            var before = others.FirstOrDefault(h => center < h.Header.Bounds.X + h.Header.Bounds.Width / 2);
            var index = before.Header is not null
                ? order.IndexOf(before.Column)
                : order.IndexOf(others[^1].Column) + 1;

            var current = vm.Columns.Order;
            vm.Columns.Move(_headerColumn, index);

            // Moved: lay out now, so the header's new place is known before it is shifted
            if (!ReferenceEquals(current, vm.Columns.Order))
                ColumnHeaders.UpdateLayout();
        }

        _headerShift.X = left - dragged.Bounds.X;
    }

    private void EndHeaderGesture()
    {
        if (_headerGesture == HeaderGesture.None)
            return;

        if (_headerButton is { } header)
        {
            header.Classes.Remove("dragging");
            header.ZIndex = 0;
            header.RenderTransform = null;
        }

        _headerShift.X = 0;
        _headerGesture = HeaderGesture.None;
        _headerButton = null;
        SetHotSeparator(null);

        // Last, as releasing the capture raises PointerCaptureLost, which comes back here (and finds nothing to do)
        var pointer = _headerPointer;
        _headerPointer = null;
        if (pointer is not null && ReferenceEquals(pointer.Captured, ColumnHeaders))
            pointer.Capture(null);
    }

    /// <summary>Highlights the separator before <paramref name="column"/> and shows the resize cursor; none when null.</summary>
    private void SetHotSeparator(DetailColumn? column)
    {
        ColumnHeaders.HotColumn = column;
        ColumnHeaders.Cursor = column is null ? null : ResizeCursor;
    }

    /// <summary>The detail column headers on screen, left to right.</summary>
    private List<(Button Header, DetailColumn Column)> VisibleHeaders()
    {
        var headers = new List<(Button Header, DetailColumn Column)>();
        foreach (var child in ColumnHeaders.Children)
        {
            if (child is Button { IsVisible: true } button && DetailRowPanel.ColumnOf(button) is { } column)
                headers.Add((button, column));
        }

        headers.Sort((a, b) => a.Header.Bounds.X.CompareTo(b.Header.Bounds.X));
        return headers;
    }

    /// <summary>The header whose resize grip (its separator, on its left) is at <paramref name="x"/>.</summary>
    private (Button Header, DetailColumn Column)? GripAt(double x)
    {
        foreach (var header in VisibleHeaders())
        {
            if (Math.Abs(x - DetailRowPanel.SeparatorX(header.Header)) <= ResizeGripHalfWidth)
                return header;
        }

        return null;
    }

    /// <summary>The header button <paramref name="source"/> is in, if any.</summary>
    private Button? HeaderAt(Visual? source)
    {
        for (var visual = source; visual is not null && !ReferenceEquals(visual, ColumnHeaders); visual = visual.GetVisualParent())
        {
            if (visual is Button button && ReferenceEquals(button.GetVisualParent(), ColumnHeaders))
                return button;
        }

        return null;
    }
}
