using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using File.Commander.Presentation.Services;
using File.Commander.Presentation.ViewModels.Helpers;
using File.Commander.Presentation.ViewModels.Pages;
using Material.Icons;

namespace File.Commander.Presentation.ViewModels.Browser;

public sealed partial class DirectoryViewModel : PageViewModel
{
    // Defaults skip Hidden | System, i.e. dot files on Linux.
    // IgnoreInaccessible = false, so an unreadable folder reports an error instead of looking empty.
    private static readonly EnumerationOptions WithoutHidden = new() { IgnoreInaccessible = false };

    // Skips nothing: dot files included
    private static readonly EnumerationOptions WithHidden = new() { IgnoreInaccessible = false, AttributesToSkip = 0 };

    private readonly INavigator _navigator;
    private readonly FolderOptions _options;
    private readonly CancellationTokenSource _cts = new();

    // Where the entries come from: the folder at Location, or another list such as Recent
    private readonly Func<IComparer<FileEntryViewModel>, FolderOptions, CancellationToken, IReadOnlyList<FileEntryViewModel>> _read;

    // The entries TreeRoots was built from: switching views keeps the expanded folders
    private IReadOnlyList<FileEntryViewModel>? _treeSource;

    public DirectoryViewModel(string path, DirectoryViewMode viewMode, FileSort sort, FolderOptions options,
        FileColumnsViewModel columns, INavigator navigator)
        : this(Locations.Normalize(path), null, null, "This folder is empty", viewMode, sort, options, columns, navigator)
    {
    }

    private DirectoryViewModel(string location, string? title,
        Func<IComparer<FileEntryViewModel>, FolderOptions, CancellationToken, IReadOnlyList<FileEntryViewModel>>? read,
        string emptyText, DirectoryViewMode viewMode, FileSort sort, FolderOptions options,
        FileColumnsViewModel columns, INavigator navigator)
    {
        _navigator = navigator;
        _options = options;
        _read = read ?? ((comparer, folderOptions, token) => ReadEntries(location, comparer, folderOptions, token));
        Columns = columns;
        Location = location;
        Title = title ?? (location == "/" ? "/" : IOPath.GetFileName(location));
        EmptyText = emptyText;
        ViewMode = viewMode;
        Sort = sort;
    }

    /// <summary>
    /// The Recent page: the same views, filled with recently used files from all over instead of one folder.
    /// Opens newest first, whatever the sort menu says; the menu and the headers still re-sort it.
    /// </summary>
    /// <param name="maxAgeDays">Settings → File history: only what was used in this many days. 0: any time.</param>
    /// <param name="includeFolders">Settings → File history: list recently used folders too.</param>
    public static DirectoryViewModel ForRecent(DirectoryViewMode viewMode, FolderOptions options,
        FileColumnsViewModel columns, INavigator navigator, int maxAgeDays = 0, bool includeFolders = true)
        => new(Locations.Recent, "Recent",
            (comparer, folderOptions, token) => ReadRecent(comparer, folderOptions, maxAgeDays, includeFolders, token),
            "No recent files", viewMode, FileSort.Of(FileSortMode.NewestFirst), options, columns, navigator);

    /// <summary>
    /// The Trash page: the same views, filled with what's in the trash folders instead of one folder.
    /// Items are listed under the name they had before they were trashed; folders open as usual.
    /// </summary>
    /// <param name="allDrives">
    /// Settings → Trash: the trash folders of other drives too, so the page lists what Empty trash would empty.
    /// </param>
    public static DirectoryViewModel ForTrash(DirectoryViewMode viewMode, FileSort sort, FolderOptions options,
        FileColumnsViewModel columns, INavigator navigator, bool allDrives = true)
        => new(Locations.Trash, "Trash",
            (comparer, folderOptions, token) => ReadTrash(comparer, folderOptions, allDrives, token),
            "Trash is empty", viewMode, sort, options, columns, navigator);

    public override string Location { get; }

    public override string Title { get; }

    /// <summary>Shown when there are no entries: "This folder is empty", "No recent files".</summary>
    public string EmptyText { get; }

    /// <summary>Shared with the shell and every other folder page.</summary>
    public FileColumnsViewModel Columns { get; }

    /// <summary>Settings → Open file: Click. The view opens entries on a click instead of a double click.</summary>
    public bool OpenOnSingleClick => _options.OpenOnSingleClick;

    /// <summary>Set by the shell when the user picks another layout.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGridView), nameof(IsListView), nameof(IsTreeView), nameof(GridEntries),
        nameof(CurrentEntry))]
    public partial DirectoryViewMode ViewMode { get; set; }

    public bool IsGridView => ViewMode == DirectoryViewMode.Grid;

    public bool IsListView => ViewMode == DirectoryViewMode.List;

    public bool IsTreeView => ViewMode == DirectoryViewMode.Tree;

    /// <summary>Set by the shell from the sort menu, or by a header click for this folder only.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SortArrow))]
    public partial FileSort Sort { get; set; }

    /// <summary>Drawn next to the sorted column's header.</summary>
    public MaterialIconKind SortArrow => Sort.Descending ? MaterialIconKind.ArrowDown : MaterialIconKind.ArrowUp;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(IsEmpty), nameof(GridEntries))]
    public partial IReadOnlyList<FileEntryViewModel> Entries { get; set; } = [];

    /// <summary>
    /// <see cref="Entries"/> while the grid is shown, empty otherwise: its WrapPanel doesn't
    /// virtualize, so a hidden grid would still create a tile for every entry.
    /// </summary>
    public IReadOnlyList<FileEntryViewModel> GridEntries => IsGridView ? Entries : [];

    /// <summary>Top level of the tree view. Built only while the tree is shown.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<FileTreeNodeViewModel> TreeRoots { get; set; } = [];

    /// <summary>
    /// The focused selection of the list and the grid (the first selected entry). Set by the view:
    /// several entries may be selected, see <see cref="SelectedEntries"/>.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentEntry))]
    public partial FileEntryViewModel? SelectedEntry { get; set; }

    /// <summary>The focused selection of the tree: may be inside a subfolder, or a placeholder row. Set by the view.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentEntry))]
    public partial FileTreeNodeViewModel? SelectedTreeNode { get; set; }

    /// <summary>What is selected in the view shown now. Null when nothing (or a placeholder) is. Read by the info panel.</summary>
    public FileEntryViewModel? CurrentEntry => IsTreeView ? SelectedTreeNode?.Entry : SelectedEntry;

    /// <summary>
    /// Everything selected in the view shown now (Ctrl/Shift+click, Shift+arrows, Ctrl+A…), in view order.
    /// Never holds placeholders. The view reports it through <see cref="UpdateSelection"/>.
    /// </summary>
    public IReadOnlyList<FileEntryViewModel> SelectedEntries { get; private set; } = [];

    public bool HasSelection => SelectedEntries.Count > 0;

    /// <summary>
    /// This page changed <see cref="SelectedEntries"/> itself (select all, a view switch, a new order):
    /// the view should show that selection. Raised before the bindings see a new
    /// <see cref="Entries"/> or <see cref="ViewMode"/>, so the view applies it later.
    /// </summary>
    public event EventHandler? SelectionRequested;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(IsEmpty))]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(HasError), nameof(IsEmpty))]
    public partial string? Error { get; set; }

    public bool HasError => Error is not null;

    public bool IsEmpty => !IsLoading && Error is null && Entries.Count == 0;

    public override string StatusText => IsLoading
        ? "Loading…"
        : Error is not null
            ? string.Empty
            : SelectedEntries.Count == 0
                ? ItemsText
                : $"{ItemsText} · {SelectionText()}";

    private string ItemsText => Entries.Count == 1 ? "1 item" : $"{Entries.Count} items";

    /// <summary>"3 selected (4.2 MB)": the size counts files only, as the Size column does.</summary>
    private string SelectionText()
    {
        long bytes = 0;
        var hasSize = false;
        foreach (var entry in SelectedEntries)
        {
            if (entry is { IsDirectory: false, Size: { } size })
            {
                bytes += size;
                hasSize = true;
            }
        }

        var text = $"{SelectedEntries.Count} selected";
        return hasSize ? $"{text} ({SizeFormatter.Format(bytes)})" : text;
    }

    /// <summary>Called by the view whenever its selection changes. Doesn't raise <see cref="SelectionRequested"/>.</summary>
    public void UpdateSelection(IReadOnlyList<FileEntryViewModel> entries)
    {
        if (entries.Count == 0 && SelectedEntries.Count == 0)
            return;

        SelectedEntries = entries;
        OnPropertyChanged(nameof(SelectedEntries));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(StatusText));
    }

    /// <summary>Every entry of the list or grid; in the tree, every visible row (folders already expanded).</summary>
    public void SelectAll() => RequestSelection(SelectableEntries());

    public void SelectNone() => RequestSelection([]);

    /// <summary>Selects what isn't selected, among what <see cref="SelectAll"/> would select.</summary>
    public void InvertSelection()
    {
        var selected = SelectedEntries.ToHashSet();
        RequestSelection(SelectableEntries().Where(e => !selected.Contains(e)).ToList());
    }

    /// <summary>
    /// The tree's rows as drawn, top to bottom: the roots, and the children of every expanded folder.
    /// Placeholders are skipped.
    /// </summary>
    public IEnumerable<FileTreeNodeViewModel> VisibleTreeNodes() => VisibleNodes(TreeRoots);

    private static IEnumerable<FileTreeNodeViewModel> VisibleNodes(IEnumerable<FileTreeNodeViewModel> nodes)
    {
        foreach (var node in nodes)
        {
            if (node.IsPlaceholder)
                continue;

            yield return node;

            if (!node.IsExpanded)
                continue;

            foreach (var child in VisibleNodes(node.Children))
                yield return child;
        }
    }

    private IReadOnlyList<FileEntryViewModel> SelectableEntries() =>
        IsTreeView ? VisibleTreeNodes().Select(n => n.Entry!).ToList() : Entries;

    private void RequestSelection(IReadOnlyList<FileEntryViewModel> entries)
    {
        UpdateSelection(entries);
        SelectionRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// After a view switch or a new <see cref="Entries"/>: keeps what is still shown. The same entries
    /// survive a new order; a subfolder's entries don't survive leaving the tree.
    /// </summary>
    private void KeepSelection()
    {
        if (SelectedEntries.Count == 0)
        {
            // Still asks the view: a view that was hidden may hold an older selection
            SelectionRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        var shown = SelectableEntries().ToHashSet();
        RequestSelection(SelectedEntries.Where(shown.Contains).ToList());
    }

    /// <summary>Reads the folder on a background thread. Never throws.</summary>
    public async Task LoadAsync()
    {
        var token = _cts.Token;
        IsLoading = true;
        Error = null;

        try
        {
            var read = _read;
            var comparer = CurrentComparer();
            var options = _options;
            var entries = await Task.Run(() => read(comparer, options, token), token);

            // The order may have changed while the folder was being read
            if (CurrentComparer() is var current && !ReferenceEquals(current, comparer))
                entries = entries.Order(current).ToList();

            if (!token.IsCancellationRequested)
                Entries = entries;
        }
        catch (OperationCanceledException)
        {
        }
        catch (UnauthorizedAccessException)
        {
            Error = "You don't have permission to open this folder.";
        }
        catch (DirectoryNotFoundException)
        {
            Error = "This folder doesn't exist anymore.";
        }
        catch (IOException ex)
        {
            Error = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Enter. One entry opens as on a double click. Several open every file among them; folders are
    /// skipped, as there is only one view to go into.
    /// </summary>
    [RelayCommand]
    private void OpenSelection()
    {
        if (SelectedEntries.Count == 0)
            Open(CurrentEntry);
        else
            OpenEntries(SelectedEntries);
    }

    /// <summary>
    /// Open in the context menu, and Enter. One entry opens as on a double click. Several open every file
    /// among them; folders are skipped, as there is only one view to go into.
    /// </summary>
    public void OpenEntries(IReadOnlyList<FileEntryViewModel> entries)
    {
        if (entries.Count == 1)
        {
            Open(entries[0]);
            return;
        }

        foreach (var entry in entries.Where(e => !e.IsDirectory).ToList())
            Open(entry);
    }

    [RelayCommand]
    private void Open(FileEntryViewModel? entry)
    {
        if (entry is null)
            return;

        if (entry.IsDirectory)
        {
            _navigator.Navigate(entry.FullPath);
            return;
        }

        try
        {
            // On Linux UseShellExecute goes through xdg-open, i.e. the user's default app
            Process.Start(new ProcessStartInfo(entry.FullPath) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            Trace.WriteLine($"Can't open '{entry.FullPath}': {ex.Message}");
        }
    }

    /// <summary>
    /// A header click: sorts this folder by that column, or flips the direction. Not saved:
    /// the next folder opened uses the sort menu's order again.
    /// </summary>
    [RelayCommand]
    private void SortBy(FileSortColumn column) => Sort = Sort.ClickedOn(column);

    partial void OnViewModeChanged(DirectoryViewMode value)
    {
        EnsureTree();
        KeepSelection();
    }

    partial void OnEntriesChanged(IReadOnlyList<FileEntryViewModel> value)
    {
        EnsureTree();
        KeepSelection();
    }

    partial void OnSortChanged(FileSort value)
    {
        var comparer = FileSorting.For(value, _options.MixFilesAndFolders);
        var sorted = Entries.Order(comparer).ToList();

        // Reorder the tree in place rather than rebuilding it, so expanded folders stay expanded
        if (ReferenceEquals(_treeSource, Entries))
        {
            _treeSource = sorted;
            TreeRoots = FileTreeNodeViewModel.Sort(TreeRoots, comparer);
        }

        Entries = sorted;
    }

    private void EnsureTree()
    {
        if (!IsTreeView || ReferenceEquals(_treeSource, Entries))
            return;

        _treeSource = Entries;
        TreeRoots = Entries.Select(e => new FileTreeNodeViewModel(e, CurrentComparer, _options, _cts.Token)).ToList();
    }

    private IComparer<FileEntryViewModel> CurrentComparer() => FileSorting.For(Sort, _options.MixFilesAndFolders);

    protected override void OnDispose()
    {
        // Navigated away: stop reading a folder nobody looks at
        _cts.Cancel();
        base.OnDispose();
    }

    /// <summary>Also used by the tree view to read subfolders.</summary>
    internal static IReadOnlyList<FileEntryViewModel> ReadEntries(string path, IComparer<FileEntryViewModel> comparer,
        FolderOptions options, CancellationToken token)
    {
        var directory = new DirectoryInfo(path);
        if (!directory.Exists)
            throw new DirectoryNotFoundException(path);

        var result = new List<FileEntryViewModel>();
        foreach (var info in directory.EnumerateFileSystemInfos("*", options.ShowHidden ? WithHidden : WithoutHidden))
        {
            token.ThrowIfCancellationRequested();
            result.Add(FileEntryViewModel.From(info, options.ShowExtensions));
        }

        result.Sort(comparer);
        return result;
    }

    private static IReadOnlyList<FileEntryViewModel> ReadTrash(IComparer<FileEntryViewModel> comparer,
        FolderOptions options, bool allDrives, CancellationToken token)
    {
        // Found here, on the background thread: it lists the mounted drives
        var bins = TrashBins.Find(allDrives);

        var result = new List<FileEntryViewModel>();
        foreach (var item in TrashContents.Read(bins, token))
        {
            token.ThrowIfCancellationRequested();

            // Same rule as in folders, by the name it had: dot files only while hidden files are shown
            if (!options.ShowHidden && item.Name.StartsWith('.'))
                continue;

            result.Add(FileEntryViewModel.From(item.Info, options.ShowExtensions, item.Name));
        }

        result.Sort(comparer);
        return result;
    }

    private static IReadOnlyList<FileEntryViewModel> ReadRecent(IComparer<FileEntryViewModel> comparer,
        FolderOptions options, int maxAgeDays, bool includeFolders, CancellationToken token)
    {
        DateTime? usedSince = maxAgeDays > 0 ? DateTime.UtcNow.AddDays(-maxAgeDays) : null;

        var result = new List<FileEntryViewModel>();
        foreach (var info in RecentFiles.Read(token, usedSince))
        {
            token.ThrowIfCancellationRequested();

            if (!includeFolders && info is DirectoryInfo)
                continue;

            // Same rule as in folders: dot files only while hidden files are shown
            if (!options.ShowHidden && info.Name.StartsWith('.'))
                continue;

            result.Add(FileEntryViewModel.From(info, options.ShowExtensions));
        }

        result.Sort(comparer);
        return result;
    }
}
