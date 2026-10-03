using CommunityToolkit.Mvvm.ComponentModel;
using Material.Icons;

namespace File.Commander.Presentation.ViewModels.Browser;

/// <summary>
/// A row of the tree view: a file, a folder, or a placeholder ("Loading…", "Empty", an error).
/// A folder reads its children the first time it is expanded.
/// </summary>
public sealed partial class FileTreeNodeViewModel : ObservableObject
{
    private readonly Func<IComparer<FileEntryViewModel>>? _comparer;
    private readonly FolderOptions _options;
    private readonly CancellationToken _cancellationToken;
    private bool _loadStarted;

    public FileTreeNodeViewModel(FileEntryViewModel entry, Func<IComparer<FileEntryViewModel>> comparer,
        FolderOptions options, CancellationToken cancellationToken)
    {
        Entry = entry;
        Name = entry.DisplayName;
        _comparer = comparer;
        _options = options;
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

    public bool IsHidden => Entry?.IsHidden == true;

    public MaterialIconKind Icon => Entry?.Icon ?? MaterialIconKind.FileOutline;

    /// <summary>Tooltip. Null for placeholders, so they get none.</summary>
    public string? FullPath => Entry?.FullPath;

    public string SizeText => Entry?.SizeText ?? string.Empty;

    public string TypeText => Entry?.TypeText ?? string.Empty;

    public string ModifiedText => Entry?.ModifiedText ?? string.Empty;

    public string CreatedText => Entry?.CreatedText ?? string.Empty;

    public string AccessedText => Entry?.AccessedText ?? string.Empty;

    public string PermissionsText => Entry?.PermissionsText ?? string.Empty;

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
            var comparer = _comparer!();
            var entries = await Task.Run(() => DirectoryViewModel.ReadEntries(path, comparer, _options, token), token);

            // The order may have changed while the folder was being read
            if (_comparer() is var current && !ReferenceEquals(current, comparer))
                entries = entries.Order(current).ToList();

            Children = entries.Count == 0
                ? [Placeholder("Empty")]
                : entries.Select(e => new FileTreeNodeViewModel(e, _comparer, _options, token)).ToList();
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

    /// <summary>
    /// Reorders <paramref name="nodes"/> and every folder already read below them.
    /// Expanded folders stay expanded: the nodes are kept, only their order changes.
    /// </summary>
    internal static IReadOnlyList<FileTreeNodeViewModel> Sort(
        IReadOnlyList<FileTreeNodeViewModel> nodes, IComparer<FileEntryViewModel> comparer)
    {
        foreach (var node in nodes)
        {
            // Files and placeholders have no children; an unread folder only its placeholder
            if (node.Children.Count > 0)
                node.Children = Sort(node.Children, comparer);
        }

        // A single node (or a placeholder, which is always alone) has no order to change
        return nodes.Count < 2 ? nodes : nodes.OrderBy(n => n.Entry!, comparer).ToList();
    }

    private static FileTreeNodeViewModel Placeholder(string text) => new(text);
}
