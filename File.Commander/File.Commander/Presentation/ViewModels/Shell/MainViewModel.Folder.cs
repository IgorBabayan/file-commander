using CommunityToolkit.Mvvm.Input;
using File.Commander.Presentation.Services;
using File.Commander.Presentation.ViewModels.Browser;
using File.Commander.Presentation.ViewModels.Dialogs;
using Material.Icons;

namespace File.Commander.Presentation.ViewModels.Shell;

/// <summary>
/// What the context menu of a folder's empty space does (the menu itself is built by MainWindow.FolderMenu.cs):
/// new items, paste, select all and the folder's properties. New folder and New text document also have
/// shortcuts in Settings → Keymap → Files, which act on the active view's folder.
/// </summary>
public partial class MainViewModel
{
    private const string NewFolderName = "New folder";
    private const string NewTextDocumentName = "New text document.txt";

    /// <summary>
    /// Items can be created or pasted in the folder of <paramref name="page"/>. Not in Recent or Trash: they list
    /// items from all over, not the content of one folder.
    /// </summary>
    public static bool CanCreateIn(DirectoryViewModel page) => !Locations.IsVirtual(page.Location);

    /// <summary>Paste has something to do in <paramref name="page"/>: files are on the clipboard and the folder takes them.</summary>
    public static async Task<bool> CanPasteIntoAsync(DirectoryViewModel page)
        => CanCreateIn(page) && await FileClipboard.GetAsync() is { Paths.Count: > 0 };

    /// <summary>New folder (Ctrl+Shift+N): asks for the name, then creates it in the folder of <paramref name="page"/>.</summary>
    public Task CreateFolderAsync(DirectoryViewModel page) => CreateItemAsync(page, folder: true);

    /// <summary>New text document: asks for the name, then creates an empty file in the folder of <paramref name="page"/>.</summary>
    public Task CreateTextDocumentAsync(DirectoryViewModel page) => CreateItemAsync(page, folder: false);

    /// <summary>Properties of the folder <paramref name="page"/> shows (or of Recent, or of the trash).</summary>
    public async Task ShowFolderPropertiesAsync(DirectoryViewModel page)
    {
        var icon = page.Location switch
        {
            Locations.Trash => MaterialIconKind.TrashCanOutline,
            Locations.Recent => MaterialIconKind.ClockOutline,
            _ => MaterialIconKind.FolderOutline,
        };

        // Described like the same folder on the sidebar: a drive's usage, a folder's contents, the trash's items
        using var properties = PropertiesViewModel.For(new SidebarItem(page.Title, icon, page.Location),
            _appliedSettings.Advanced!.EmptyTrashOnAllDrives);
        await _dialogService.ShowDialogAsync<PropertiesViewModel, bool>(properties);
    }

    private bool CanCreateHere() => CurrentPage is DirectoryViewModel page && CanCreateIn(page);

    [RelayCommand(CanExecute = nameof(CanCreateHere))]
    private Task NewFolder() => CurrentPage is DirectoryViewModel page ? CreateFolderAsync(page) : Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(CanCreateHere))]
    private Task NewTextDocument()
        => CurrentPage is DirectoryViewModel page ? CreateTextDocumentAsync(page) : Task.CompletedTask;

    /// <summary>
    /// Suggests a free name ("New folder", "New folder (2)"…), asks for the name, creates the item and refreshes
    /// the views showing the folder. A name that is taken or invalid is reported in a notice.
    /// </summary>
    private async Task CreateItemAsync(DirectoryViewModel page, bool folder)
    {
        if (!CanCreateIn(page))
            return;

        var location = Locations.Normalize(page.Location);
        var suggested = IOPath.GetFileName(FileOperations.UniquePath(location,
            folder ? NewFolderName : NewTextDocumentName));

        using var prompt = PromptViewModel.ForInput(
            folder ? "New folder" : "New text document",
            folder
                ? $"The name of the new folder in “{page.Title}”."
                : $"The name of the new text document in “{page.Title}”.",
            suggested,
            "Create");

        if (!await _dialogService.ShowDialogAsync<PromptViewModel, bool>(prompt))
            return;

        var name = prompt.InputText.Trim();
        var problem = folder ? FileOperations.CreateFolder(location, name) : FileOperations.CreateFile(location, name);
        if (problem is not null)
        {
            await ShowNoticeAsync(folder ? "Can't create the folder" : "Can't create the text document", problem);
            return;
        }

        RefreshFolders([location]);
    }
}
