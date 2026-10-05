using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Selection;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using File.Commander.Presentation.ViewModels.Browser;

namespace File.Commander.Presentation.Views.Browser;

public partial class DirectoryView : UserControl
{
    // Horizontal space that isn't name or detail columns: side margins (list padding + item padding,
    // same as DockPanel.column-headers), the icon slot, and room for the vertical scroll bar
    private const double ListChrome = 22 + 22 + 28 + 8;

    // The tree's headers start further right, past the expander column
    private const double TreeChrome = 48 + 22 + 28 + 8;

    private DirectoryViewModel? _viewModel;

    // The page asked for a selection that isn't applied yet: the controls' own changes until then
    // (a new ItemsSource clearing them) must not overwrite it
    private bool _selectionPending;

    // Applying the page's selection: the controls report every step of it
    private bool _applyingSelection;

    // The layout changed: show the restored selection
    private bool _scrollToSelection;

    public DirectoryView()
    {
        InitializeComponent();

        SizeChanged += (_, _) => FitColumns();

        // List and grid show the same entries, so they share the handlers
        foreach (var list in new[] { FileList, GridList })
        {
            list.SelectionChanged += OnListSelectionChanged;
            list.Tapped += OnEntryTapped;
            list.DoubleTapped += OnEntryDoubleTapped;
            // handledEventsToo: ListBox may consume Enter itself
            list.AddHandler(KeyDownEvent, OnListKeyDown, RoutingStrategies.Bubble, handledEventsToo: true);
        }

        FileTree.SelectionChanged += OnTreeSelectionChanged;
        FileTree.Tapped += OnTreeTapped;
        FileTree.DoubleTapped += OnTreeDoubleTapped;
        // handledEventsToo: TreeView may consume Enter itself
        FileTree.AddHandler(KeyDownEvent, OnTreeKeyDown, RoutingStrategies.Bubble, handledEventsToo: true);

        InitializeRubberBand();

        // Right click and the menu key on entries (DirectoryView.EntryMenu.cs)
        InitializeEntryMenu();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        EndBand();
        Subscribe(DataContext as DirectoryViewModel);
        FitColumns();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Subscribe(DataContext as DirectoryViewModel);
        FitColumns();
    }

    // Columns is shared by every folder page and lives as long as the window: don't leave handlers on it
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Subscribe(null);
        base.OnDetachedFromVisualTree(e);
    }

    private void Subscribe(DirectoryViewModel? viewModel)
    {
        if (ReferenceEquals(viewModel, _viewModel))
            return;

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel.Columns.PropertyChanged -= OnColumnsPropertyChanged;
            _viewModel.SelectionRequested -= OnSelectionRequested;
        }

        _viewModel = viewModel;

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            _viewModel.Columns.PropertyChanged += OnColumnsPropertyChanged;
            _viewModel.SelectionRequested += OnSelectionRequested;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not nameof(DirectoryViewModel.ViewMode))
            return;

        FitColumns();
        _scrollToSelection = true;
    }

    /// <summary>
    /// The page set its selection. Applied once the bindings are done: a new order or another view
    /// gives the control a new ItemsSource first.
    /// </summary>
    private void OnSelectionRequested(object? sender, EventArgs e)
    {
        if (_selectionPending)
            return;

        _selectionPending = true;
        Dispatcher.UIThread.Post(ApplySelection, DispatcherPriority.Background);
    }

    private void OnListSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        // The hidden list and grid follow nothing: each one is set when it is shown
        if (!ReferenceEquals(e.Source, sender) || _selectionPending || _applyingSelection
            || _viewModel is not { IsTreeView: false } vm || !ReferenceEquals(sender, ActiveList(vm)))
            return;

        ReadSelection(vm);
    }

    private void OnTreeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, sender) || _selectionPending || _applyingSelection
            || _viewModel is not { IsTreeView: true } vm)
            return;

        ReadSelection(vm);
    }

    private ListBox ActiveList(DirectoryViewModel vm) => vm.IsGridView ? GridList : FileList;

    /// <summary>Tells the page what the shown control has selected.</summary>
    private void ReadSelection(DirectoryViewModel vm)
    {
        if (vm.IsTreeView)
        {
            vm.SelectedTreeNode = FileTree.SelectedItem as FileTreeNodeViewModel;
            vm.UpdateSelection(FileTree.SelectedItems
                .OfType<FileTreeNodeViewModel>()
                .Where(n => n.Entry is not null)
                .Select(n => n.Entry!)
                .ToList());
            return;
        }

        var list = ActiveList(vm);
        vm.SelectedEntry = list.SelectedItem as FileEntryViewModel;
        vm.UpdateSelection(list.Selection.SelectedItems.OfType<FileEntryViewModel>().ToList());
    }

    /// <summary>Shows the page's <see cref="DirectoryViewModel.SelectedEntries"/> in the shown control.</summary>
    private void ApplySelection()
    {
        _selectionPending = false;
        var scroll = _scrollToSelection;
        _scrollToSelection = false;

        if (_viewModel is not { } vm)
            return;

        var wanted = vm.SelectedEntries.ToHashSet();

        _applyingSelection = true;
        try
        {
            if (vm.IsTreeView)
                ApplyToTree(vm, wanted, scroll);
            else
                ApplyToList(ActiveList(vm), wanted, scroll);
        }
        finally
        {
            _applyingSelection = false;
        }

        // What the control could take: e.g. a row that is gone
        ReadSelection(vm);
    }

    private static void ApplyToList(ListBox list, HashSet<FileEntryViewModel> wanted, bool scroll)
    {
        var selection = list.Selection;
        var first = -1;

        using (selection.BatchUpdate())
        {
            selection.Clear();

            if (wanted.Count > 0 && list.ItemsSource is IReadOnlyList<FileEntryViewModel> items)
            {
                for (var i = 0; i < items.Count; i++)
                {
                    if (!wanted.Contains(items[i]))
                        continue;

                    selection.Select(i);
                    if (first < 0)
                        first = i;
                }
            }
        }

        if (scroll && first >= 0)
            list.ScrollIntoView(first);
    }

    private void ApplyToTree(DirectoryViewModel vm, HashSet<FileEntryViewModel> wanted, bool scroll)
    {
        var selected = FileTree.SelectedItems;
        selected.Clear();

        FileTreeNodeViewModel? first = null;
        if (wanted.Count > 0)
        {
            foreach (var node in vm.VisibleTreeNodes())
            {
                if (!wanted.Contains(node.Entry!))
                    continue;

                selected.Add(node);
                first ??= node;
            }
        }

        // Only a root has an index of the tree's own: rows in subfolders belong to their TreeViewItem
        if (scroll && first is not null && IndexOf(vm.TreeRoots, first) is var index and >= 0)
            FileTree.ScrollIntoView(index);
    }

    private static int IndexOf(IReadOnlyList<FileTreeNodeViewModel> nodes, FileTreeNodeViewModel node)
    {
        for (var i = 0; i < nodes.Count; i++)
        {
            if (ReferenceEquals(nodes[i], node))
                return i;
        }

        return -1;
    }

    private void OnColumnsPropertyChanged(object? sender, PropertyChangedEventArgs e) => FitColumns();

    /// <summary>
    /// Shrinks the detail columns to the view's width, so a narrow view (split view) trims their text
    /// instead of drawing them over each other and the name. Header and rows share the widths, so they line up.
    /// </summary>
    private void FitColumns()
    {
        // Before the first layout the width is 0: that would drop every column for a frame
        if (_viewModel is not { IsGridView: false } vm || Bounds.Width <= 0)
            return;

        var available = Bounds.Width - (vm.IsTreeView ? TreeChrome : ListChrome);
        var widths = DetailColumnsLayout.Fit(available, ShownColumns(vm.Columns));

        foreach (var column in DetailColumnsLayout.All)
        {
            var width = widths[(int)column];
            var name = column.ToString();

            Root.Classes.Set("drop-" + name.ToLowerInvariant(), width is null);

            // Only real changes: every row re-measures when a width resource changes
            var key = "Col" + name + "Width";
            var value = width ?? DetailColumnsLayout.PreferredWidth(column);
            if (!Resources.TryGetValue(key, out var current) || current is not double d || d != value)
                Resources[key] = value;
        }
    }

    private static List<DetailColumn> ShownColumns(FileColumnsViewModel columns)
    {
        var shown = new List<DetailColumn>();
        if (columns.ShowSize) shown.Add(DetailColumn.Size);
        if (columns.ShowType) shown.Add(DetailColumn.Type);
        if (columns.ShowModified) shown.Add(DetailColumn.Modified);
        if (columns.ShowCreated) shown.Add(DetailColumn.Created);
        if (columns.ShowAccessed) shown.Add(DetailColumn.Accessed);
        if (columns.ShowPermissions) shown.Add(DetailColumn.Permissions);
        return shown;
    }

    /// <summary>Settings → Open file: Click. Ctrl/Shift+click still only selects (several entries).</summary>
    private void OnEntryTapped(object? sender, TappedEventArgs e)
    {
        // The end of a rubber-band drag, not a click
        if (ConsumeSuppressedTap())
            return;

        if (DataContext is DirectoryViewModel { OpenOnSingleClick: true } vm
            && e.KeyModifiers == KeyModifiers.None
            && (e.Source as StyledElement)?.DataContext is FileEntryViewModel entry)
            vm.OpenCommand.Execute(entry);
    }

    private void OnEntryDoubleTapped(object? sender, TappedEventArgs e)
    {
        // A double-click on empty space has the page as DataContext, not an entry: ignore it.
        // In single-click mode the first click already opened it.
        if (DataContext is DirectoryViewModel { OpenOnSingleClick: false } vm
            && (e.Source as StyledElement)?.DataContext is FileEntryViewModel entry)
            vm.OpenCommand.Execute(entry);
    }

    private void OnListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || e.KeyModifiers != KeyModifiers.None)
            return;

        if (DataContext is DirectoryViewModel { HasSelection: true } vm)
        {
            vm.OpenSelectionCommand.Execute(null);
            e.Handled = true;
        }
    }

    /// <summary>Single-click mode: a click opens a file. Folders still expand with the arrow or a double click.</summary>
    private void OnTreeTapped(object? sender, TappedEventArgs e)
    {
        if (ConsumeSuppressedTap())
            return;

        if (DataContext is DirectoryViewModel { OpenOnSingleClick: true } vm
            && e.KeyModifiers == KeyModifiers.None
            && (e.Source as StyledElement)?.DataContext is FileTreeNodeViewModel { Entry: { IsDirectory: false } entry })
            vm.OpenCommand.Execute(entry);
    }

    /// <summary>Opens files only: a double-click on a folder expands it (TreeViewItem does that itself).</summary>
    private void OnTreeDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is DirectoryViewModel { OpenOnSingleClick: false } vm
            && (e.Source as StyledElement)?.DataContext is FileTreeNodeViewModel { Entry: { IsDirectory: false } entry })
            vm.OpenCommand.Execute(entry);
    }

    /// <summary>Enter opens a file or goes into a folder, as in the list; several selected open their files.</summary>
    private void OnTreeKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || e.KeyModifiers != KeyModifiers.None)
            return;

        if (DataContext is DirectoryViewModel { HasSelection: true } vm)
        {
            vm.OpenSelectionCommand.Execute(null);
            e.Handled = true;
        }
    }
}
