using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using File.Commander.Presentation.Services;
using File.Commander.Presentation.ViewModels.Pages;

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

    // The entries TreeRoots was built from: switching views keeps the expanded folders
    private IReadOnlyList<FileEntryViewModel>? _treeSource;

    public DirectoryViewModel(string path, DirectoryViewMode viewMode, FileSortMode sortMode, FolderOptions options,
        FileColumnsViewModel columns, INavigator navigator)
    {
        _navigator = navigator;
        _options = options;
        Columns = columns;
        Location = Locations.Normalize(path);
        Title = Location == "/" ? "/" : IOPath.GetFileName(Location);
        ViewMode = viewMode;
        SortMode = sortMode;
    }

    public override string Location { get; }

    public override string Title { get; }

    /// <summary>Shared with the shell and every other folder page.</summary>
    public FileColumnsViewModel Columns { get; }

    /// <summary>Settings → Open file: Click. The view opens entries on a click instead of a double click.</summary>
    public bool OpenOnSingleClick => _options.OpenOnSingleClick;

    /// <summary>Set by the shell when the user picks another layout.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGridView), nameof(IsListView), nameof(IsTreeView), nameof(GridEntries))]
    public partial DirectoryViewMode ViewMode { get; set; }

    public bool IsGridView => ViewMode == DirectoryViewMode.Grid;

    public bool IsListView => ViewMode == DirectoryViewMode.List;

    public bool IsTreeView => ViewMode == DirectoryViewMode.Tree;

    /// <summary>Set by the shell when the user picks another order.</summary>
    [ObservableProperty]
    public partial FileSortMode SortMode { get; set; }

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

    [ObservableProperty]
    public partial FileEntryViewModel? SelectedEntry { get; set; }

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
            : Entries.Count == 1 ? "1 item" : $"{Entries.Count} items";

    /// <summary>Reads the folder on a background thread. Never throws.</summary>
    public async Task LoadAsync()
    {
        var token = _cts.Token;
        IsLoading = true;
        Error = null;

        try
        {
            var path = Location;
            var comparer = CurrentComparer();
            var options = _options;
            var entries = await Task.Run(() => ReadEntries(path, comparer, options, token), token);

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

    partial void OnViewModeChanged(DirectoryViewMode value) => EnsureTree();

    partial void OnEntriesChanged(IReadOnlyList<FileEntryViewModel> value) => EnsureTree();

    partial void OnSortModeChanged(FileSortMode value)
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

    private IComparer<FileEntryViewModel> CurrentComparer() => FileSorting.For(SortMode, _options.MixFilesAndFolders);

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
}
