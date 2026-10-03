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
    private static readonly EnumerationOptions EnumerationOptions = new() { IgnoreInaccessible = false };

    private readonly INavigator _navigator;
    private readonly CancellationTokenSource _cts = new();

    public DirectoryViewModel(string path, INavigator navigator)
    {
        _navigator = navigator;
        Location = Locations.Normalize(path);
        Title = Location == "/" ? "/" : IOPath.GetFileName(Location);
    }

    public override string Location { get; }

    public override string Title { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(IsEmpty))]
    public partial IReadOnlyList<FileEntryViewModel> Entries { get; set; } = [];

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
            var entries = await Task.Run(() => ReadEntries(path, token), token);
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

    protected override void OnDispose()
    {
        // Navigated away: stop reading a folder nobody looks at
        _cts.Cancel();
        base.OnDispose();
    }

    private static IReadOnlyList<FileEntryViewModel> ReadEntries(string path, CancellationToken token)
    {
        var directory = new DirectoryInfo(path);
        if (!directory.Exists)
            throw new DirectoryNotFoundException(path);

        var result = new List<FileEntryViewModel>();
        foreach (var info in directory.EnumerateFileSystemInfos("*", EnumerationOptions))
        {
            token.ThrowIfCancellationRequested();
            result.Add(FileEntryViewModel.From(info));
        }

        result.Sort(FileEntryViewModel.Comparer);
        return result;
    }
}
