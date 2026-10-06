using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace File.Commander.Presentation.Views.Browser;

/// <summary>
/// Dragging entries out of the view: a press on an entry (no Ctrl or Shift) that moves past the drag
/// threshold drags the selection, or just that entry when it isn't selected. Folders dropped on the
/// sidebar become favorites. A press on empty space, or Ctrl+press on an entry, still draws the rubber band.
/// </summary>
public partial class DirectoryView
{
    // The entry the left button went down on, when a move should drag it instead of drawing the band
    private FileEntryViewModel? _dragEntry;

    // That press: Avalonia starts a drag from the press event (its pointer, source and modifiers),
    // not from the move that crossed the threshold
    private PointerPressedEventArgs? _dragPress;

    /// <summary>The entry under <paramref name="source"/>, or null on empty space or a placeholder row.</summary>
    private static FileEntryViewModel? EntryAt(Visual? source, Visual owner)
    {
        for (var visual = source; visual is not null && !ReferenceEquals(visual, owner); visual = visual.GetVisualParent())
        {
            switch (visual)
            {
                case ListBoxItem { DataContext: FileEntryViewModel entry }:
                    return entry;
                case TreeViewItem { DataContext: FileTreeNodeViewModel node }:
                    return node.Entry;
                case ListBoxItem or TreeViewItem:
                    return null;
            }
        }

        return null;
    }

    /// <summary>Called instead of starting the band once the press on <paramref name="entry"/> moved far enough.</summary>
    private void StartEntryDrag(FileEntryViewModel entry, PointerEventArgs e)
    {
        if (_viewModel is not { } vm || _dragPress is not { } press)
            return;

        // The press already selected the entry when it wasn't; a multi-selection that contains it drags whole
        IReadOnlyList<FileEntryViewModel> entries = vm.SelectedEntries.Contains(entry) ? vm.SelectedEntries : [entry];

        EndBand();

        // The release ends the drag: the Tapped after it (if any) is not a click
        _suppressTap = true;
        e.Handled = true;

        _ = DragEntriesAsync(press, entries.Select(x => x.FullPath).ToList());
    }

    private static async Task DragEntriesAsync(PointerPressedEventArgs press, IReadOnlyList<string> paths)
    {
        try
        {
            // Link: what the sidebar does with a folder. Copy and Move are left for future drop targets.
            await DragDrop.DoDragDropAsync(press, FileDragData.Create(paths),
                DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Drag failed: {ex}");
        }
    }
}
