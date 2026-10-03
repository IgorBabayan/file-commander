using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using File.Commander.Presentation.ViewModels.Browser;

namespace File.Commander.Presentation.Views.Browser;

public partial class DirectoryView : UserControl
{
    public DirectoryView()
    {
        InitializeComponent();

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
