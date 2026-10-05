using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using File.Commander.Presentation.ViewModels.Browser;

namespace File.Commander.Presentation.Views.Browser;

/// <summary>A right click or the menu key (Shift+F10) on entries of a folder view: the window opens their menu.</summary>
public sealed class EntryMenuRequestedEventArgs(
    DirectoryViewModel page,
    IReadOnlyList<FileEntryViewModel> entries,
    Control target,
    bool atPointer) : RoutedEventArgs(DirectoryView.EntryMenuRequestedEvent)
{
    /// <summary>The folder page the entries are shown in.</summary>
    public DirectoryViewModel Page { get; } = page;

    /// <summary>What the menu acts on: the selection, or only the clicked entry when it isn't selected. Never empty.</summary>
    public IReadOnlyList<FileEntryViewModel> Entries { get; } = entries;

    /// <summary>The row or tile the menu belongs to.</summary>
    public Control Target { get; } = target;

    /// <summary>Opened with the pointer: the menu opens there. Otherwise (keyboard) below <see cref="Target"/>.</summary>
    public bool AtPointer { get; } = atPointer;
}

/// <summary>
/// The context menu of files and folders. The view only works out what it is for; MainWindow.EntryMenu.cs builds
/// it, as its items reach the shell (split view, tabs, the Action center).
/// </summary>
/// <remarks>
/// A right press already selects the entry under the pointer when it isn't selected, and keeps a selection that
/// contains it (Avalonia's ListBox and TreeView do that), so <see cref="DirectoryViewModel.SelectedEntries"/>
/// is up to date when the release asks for the menu.
/// </remarks>
public partial class DirectoryView
{
    /// <summary>Bubbles up to the window, which opens the menu.</summary>
    public static readonly RoutedEvent<EntryMenuRequestedEventArgs> EntryMenuRequestedEvent =
        RoutedEvent.Register<DirectoryView, EntryMenuRequestedEventArgs>("EntryMenuRequested", RoutingStrategies.Bubble);

    private void InitializeEntryMenu()
    {
        foreach (var control in new Control[] { FileList, GridList, FileTree })
            control.AddHandler(ContextRequestedEvent, OnEntryContextRequested);
    }

    private void OnEntryContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is not Control owner || _viewModel is not { } vm || !ReferenceEquals(owner, ActiveControl(vm)))
            return;

        var source = e.Source as Visual;
        var atPointer = e.TryGetPosition(owner, out _);
        var entry = EntryAt(source, owner);

        // On empty space there is nothing to act on; the menu key without a focused row acts on the selection
        IReadOnlyList<FileEntryViewModel> entries = entry is not null
            ? vm.SelectedEntries.Contains(entry) ? vm.SelectedEntries : [entry]
            : atPointer ? [] : vm.SelectedEntries;

        if (entries.Count == 0)
            return;

        e.Handled = true;
        RaiseEvent(new EntryMenuRequestedEventArgs(vm, entries, RowAt(source, owner) ?? owner, atPointer));
    }

    /// <summary>The row or tile under <paramref name="source"/>; for a tree row, its own line, not its children.</summary>
    private static Control? RowAt(Visual? source, Visual owner)
    {
        for (var visual = source; visual is not null && !ReferenceEquals(visual, owner); visual = visual.GetVisualParent())
        {
            switch (visual)
            {
                case ListBoxItem item:
                    return item;
                case TreeViewItem item:
                    return HeaderOf(item) as Control ?? item;
            }
        }

        return null;
    }
}
