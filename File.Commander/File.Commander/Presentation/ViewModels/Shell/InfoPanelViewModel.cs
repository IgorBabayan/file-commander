using System.ComponentModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using File.Commander.Presentation.Services;
using File.Commander.Presentation.ViewModels.Browser;
using File.Commander.Presentation.ViewModels.Info;
using File.Commander.Presentation.ViewModels.Pages;

namespace File.Commander.Presentation.ViewModels.Shell;

/// <summary>
/// The info panel on the right (Space), like Finder's Get Info. Shows the selected file or folder,
/// or the open folder itself when nothing is selected. Stays open across navigation; only folder pages show it.
/// </summary>
public sealed partial class InfoPanelViewModel : ViewModelBase
{
    private DirectoryViewModel? _page;

    /// <summary>The user wants it shown. Kept while on pages without files (Computer…), where it just hides.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsShown))]
    public partial bool IsOpen { get; set; }

    /// <summary>Bound to the panel: open, and there is a folder to describe.</summary>
    public bool IsShown => IsOpen && _page is not null;

    /// <summary>What is described. Kept after closing, so the slide-out animation has content.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasItem))]
    public partial ItemInfoViewModel? Item { get; set; }

    public bool HasItem => Item is not null;

    // Disclosure triangles, like Finder's. Kept per window, not per item.
    [ObservableProperty]
    public partial bool IsGeneralExpanded { get; set; } = true;

    [ObservableProperty]
    public partial bool IsMoreInfoExpanded { get; set; } = true;

    [ObservableProperty]
    public partial bool IsNameExpanded { get; set; }

    [ObservableProperty]
    public partial bool IsPermissionsExpanded { get; set; } = true;

    /// <summary>Called by the shell on every navigation.</summary>
    public void Attach(PageViewModel page)
    {
        if (_page is not null)
            _page.PropertyChanged -= OnPagePropertyChanged;

        _page = page as DirectoryViewModel;

        if (_page is not null)
            _page.PropertyChanged += OnPagePropertyChanged;

        OnPropertyChanged(nameof(IsShown));
        Update();
    }

    [RelayCommand]
    private void Close() => IsOpen = false;

    partial void OnIsOpenChanged(bool value) => Update();

    partial void OnItemChanged(ItemInfoViewModel? oldValue, ItemInfoViewModel? newValue)
    {
        // After the bindings moved to the new item, so nothing draws a disposed preview
        if (oldValue is not null)
            Dispatcher.UIThread.Post(oldValue.Dispose, DispatcherPriority.Background);
    }

    private void OnPagePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DirectoryViewModel.CurrentEntry))
            Update();
    }

    /// <summary>Describes what is selected now. Does nothing while closed: nobody would see it.</summary>
    private void Update()
    {
        if (!IsShown || _page is null)
            return;

        var entry = _page.CurrentEntry ?? FolderEntry(_page.Location);

        // Same entry again (e.g. re-selected): keep the counted size and the decoded preview
        if (ReferenceEquals(Item?.Entry, entry))
            return;

        // The folder itself, described twice in a row: re-reading it would only restart the count
        if (_page.CurrentEntry is null && Item is { } current && Locations.AreEqual(current.FullPath, entry.FullPath))
            return;

        Item = new ItemInfoViewModel(entry);
    }

    private static FileEntryViewModel FolderEntry(string location)
        => FileEntryViewModel.From(new DirectoryInfo(location));

    protected override void OnDispose()
    {
        if (_page is not null)
            _page.PropertyChanged -= OnPagePropertyChanged;

        Item?.Dispose();
        base.OnDispose();
    }
}
