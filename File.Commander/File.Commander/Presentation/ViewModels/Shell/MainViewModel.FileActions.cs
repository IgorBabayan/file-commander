using System.ComponentModel;
using File.Commander.Presentation.ViewModels.Browser;

namespace File.Commander.Presentation.ViewModels.Shell;

/// <summary>
/// The file buttons of the title bar (MainWindow.axaml): New ▸ New folder / New text document, Select all, Copy and
/// Paste ▸ Paste / Paste without replace / Paste with replace. They run the same commands as the shortcuts in
/// Settings → Keymap, on the active view, so the buttons only have to be told when those commands can run.
/// </summary>
public partial class MainViewModel
{
    // The folder page whose selection Copy (and the other selection commands) follow
    private DirectoryViewModel? _selectionSource;

    /// <summary>The New drop-down: items can be created in the active view's folder (not in Recent, Trash…).</summary>
    public bool CanCreateInActiveFolder => CanCreateHere();

    /// <summary>The Paste drop-down: the active view's folder takes pasted items.</summary>
    public bool CanPasteInActiveFolder => CanPaste();

    /// <summary>Called whenever the active view or its page changes.</summary>
    private void UpdateFileActions()
    {
        WatchSelection(CurrentPage as DirectoryViewModel);

        OnPropertyChanged(nameof(CanCreateInActiveFolder));
        OnPropertyChanged(nameof(CanPasteInActiveFolder));

        NewFolderCommand.NotifyCanExecuteChanged();
        NewTextDocumentCommand.NotifyCanExecuteChanged();
        PasteCommand.NotifyCanExecuteChanged();
        PasteWithoutReplaceCommand.NotifyCanExecuteChanged();
        PasteWithReplaceCommand.NotifyCanExecuteChanged();
        NotifySelectionCommands();
    }

    /// <summary>Follows the selection of <paramref name="page"/> only; the previous page is let go.</summary>
    private void WatchSelection(DirectoryViewModel? page)
    {
        if (ReferenceEquals(page, _selectionSource))
            return;

        if (_selectionSource is not null)
            _selectionSource.PropertyChanged -= OnSelectionSourceChanged;

        _selectionSource = page;

        if (page is not null)
            page.PropertyChanged += OnSelectionSourceChanged;
    }

    private void OnSelectionSourceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DirectoryViewModel.HasSelection) or nameof(DirectoryViewModel.SelectedEntries))
            NotifySelectionCommands();
    }

    /// <summary>Copy on the title bar is enabled only with a selection; the other selection commands follow too.</summary>
    private void NotifySelectionCommands()
    {
        CopySelectionCommand.NotifyCanExecuteChanged();
        CutSelectionCommand.NotifyCanExecuteChanged();
        RenameSelectionCommand.NotifyCanExecuteChanged();
        TrashSelectionCommand.NotifyCanExecuteChanged();
        ShowSelectionPropertiesCommand.NotifyCanExecuteChanged();
    }
}
