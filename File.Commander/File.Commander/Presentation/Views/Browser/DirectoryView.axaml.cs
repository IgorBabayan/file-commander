using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
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

    public DirectoryView()
    {
        InitializeComponent();

        SizeChanged += (_, _) => FitColumns();

        // List and grid show the same entries, so they share the handlers
        foreach (var list in new[] { FileList, GridList })
        {
            list.Tapped += OnEntryTapped;
            list.DoubleTapped += OnEntryDoubleTapped;
            // handledEventsToo: ListBox may consume Enter itself
            list.AddHandler(KeyDownEvent, OnListKeyDown, RoutingStrategies.Bubble, handledEventsToo: true);
        }

        FileTree.Tapped += OnTreeTapped;
        FileTree.DoubleTapped += OnTreeDoubleTapped;
        // handledEventsToo: TreeView may consume Enter itself
        FileTree.AddHandler(KeyDownEvent, OnTreeKeyDown, RoutingStrategies.Bubble, handledEventsToo: true);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
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
        }

        _viewModel = viewModel;

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            _viewModel.Columns.PropertyChanged += OnColumnsPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DirectoryViewModel.ViewMode))
            FitColumns();
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

    /// <summary>Settings → Open file: Click. Ctrl/Shift+click still only selects.</summary>
    private void OnEntryTapped(object? sender, TappedEventArgs e)
    {
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

        if (DataContext is DirectoryViewModel { SelectedEntry: { } entry } vm)
        {
            vm.OpenCommand.Execute(entry);
            e.Handled = true;
        }
    }

    /// <summary>Single-click mode: a click opens a file. Folders still expand with the arrow or a double click.</summary>
    private void OnTreeTapped(object? sender, TappedEventArgs e)
    {
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

    /// <summary>Enter opens a file or goes into a folder, as in the list.</summary>
    private void OnTreeKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || e.KeyModifiers != KeyModifiers.None)
            return;

        if (DataContext is DirectoryViewModel vm && FileTree.SelectedItem is FileTreeNodeViewModel { Entry: { } entry })
        {
            vm.OpenCommand.Execute(entry);
            e.Handled = true;
        }
    }
}
