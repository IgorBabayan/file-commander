using CommunityToolkit.Mvvm.ComponentModel;
using Material.Icons;

namespace File.Commander.Presentation.ViewModels.Browser;

/// <summary>
/// A row of the tree view: a file, a folder, or a placeholder ("Loading…", "Empty", an error).
/// A folder reads its children the first time it is expanded.
/// </summary>
public sealed partial class FileTreeNodeViewModel : ObservableObject
{
    private readonly CancellationToken _cancellationToken;
    private bool _loadStarted;

    public FileTreeNodeViewModel(FileEntryViewModel entry, CancellationToken cancellationToken)
    {
        Entry = entry;
        Name = entry.Name;
        _cancellationToken = cancellationToken;

        // A placeholder child makes the expander show before the folder is read
        if (entry.IsDirectory)
            Children = [Placeholder("Loading…")];
    }

    private FileTreeNodeViewModel(string text)
    {
        Name = text;
        IsPlaceholder = true;
    }

    /// <summary>Null for placeholders.</summary>
    public FileEntryViewModel? Entry { get; }

    public string Name { get; }

    public bool IsPlaceholder { get; }

    public bool IsDirectory => Entry?.IsDirectory == true;

    public MaterialIconKind Icon => Entry?.Icon ?? MaterialIconKind.FileOutline;

    /// <summary>Tooltip. Null for placeholders, so they get none.</summary>
    public string? FullPath => Entry?.FullPath;

    public string ModifiedText => Entry?.ModifiedText ?? string.Empty;

    public string SizeText => Entry?.SizeText ?? string.Empty;

    [ObservableProperty]
    public partial IReadOnlyList<FileTreeNodeViewModel> Children { get; set; } = [];

    /// <summary>Bound two-way to TreeViewItem.IsExpanded.</summary>
    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    partial void OnIsExpandedChanged(bool value)
    {
        if (value && IsDirectory && !_loadStarted)
            _ = LoadChildrenAsync();
    }

    /// <summary>Runs on the UI thread (started by the binding), reads the folder on a background one.</summary>
    private async Task LoadChildrenAsync()
    {
        _loadStarted = true;
        var token = _cancellationToken;

        try
        {
            var path = Entry!.FullPath;
            var entries = await Task.Run(() => DirectoryViewModel.ReadEntries(path, token), token);
            Children = entries.Count == 0
                ? [Placeholder("Empty")]
                : entries.Select(e => new FileTreeNodeViewModel(e, token)).ToList();
        }
        catch (OperationCanceledException)
        {
            // The page was left
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Collapsing and expanding again retries
            _loadStarted = false;
            Children = [Placeholder(ex is UnauthorizedAccessException ? "No permission" : ex.Message)];
        }
    }

    private static FileTreeNodeViewModel Placeholder(string text) => new(text);
}
