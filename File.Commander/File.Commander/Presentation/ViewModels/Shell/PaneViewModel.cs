using CommunityToolkit.Mvvm.ComponentModel;
using File.Commander.Presentation.Services;
using File.Commander.Presentation.ViewModels.Browser;
using File.Commander.Presentation.ViewModels.Pages;

namespace File.Commander.Presentation.ViewModels.Shell;

/// <summary>
/// One view of the content area, with its own location, history and layout. The window shows one,
/// or two side by side in split view; the title bar, address bar and sidebar act on the active one.
/// </summary>
public sealed partial class PaneViewModel : ViewModelBase, INavigator
{
    private readonly Stack<string> _back = new();
    private readonly Stack<string> _forward = new();
    private readonly Func<PaneViewModel, string, PageViewModel> _createPage;
    private PageViewModel _currentPage;
    private DirectoryViewMode _viewMode;

    /// <param name="createPage">Builds a page for this pane: the pane is the page's navigator, so its links open here.</param>
    public PaneViewModel(string location, DirectoryViewMode viewMode, Func<PaneViewModel, string, PageViewModel> createPage)
    {
        _createPage = createPage;
        _viewMode = viewMode; // Read by createPage
        _currentPage = createPage(this, Locations.Normalize(location));
    }

    /// <summary>After every navigation (and refresh), before the previous page is disposed.</summary>
    public event EventHandler? Navigated;

    /// <summary>What this view shows. Replaced (and the old one disposed) on every navigation.</summary>
    public PageViewModel CurrentPage
    {
        get => _currentPage;
        private set => SetProperty(ref _currentPage, value);
    }

    public string Location => CurrentPage.Location;

    /// <summary>Layout of folder pages in this view. Kept across navigation.</summary>
    public DirectoryViewMode ViewMode
    {
        get => _viewMode;
        set
        {
            if (!SetProperty(ref _viewMode, value))
                return;

            if (CurrentPage is DirectoryViewModel directory)
                directory.ViewMode = value;
        }
    }

    /// <summary>The view the title bar, address bar and sidebar act on.</summary>
    [ObservableProperty]
    public partial bool IsActive { get; set; }

    /// <summary>Another view is shown next to this one: the active one gets a highlighted border.</summary>
    [ObservableProperty]
    public partial bool IsSplit { get; set; }

    public bool CanGoBack => _back.Count > 0;

    public bool CanGoForward => _forward.Count > 0;

    public bool CanGoUp => ParentOf(Location) is not null;

    /// <summary>Opens <paramref name="location"/> as a new history entry. Clears Forward.</summary>
    public void Navigate(string location)
    {
        location = Locations.Normalize(location);
        if (Locations.AreEqual(location, Location))
            return;

        _back.Push(Location);
        _forward.Clear();
        Show(location);
    }

    public void GoBack()
    {
        if (!CanGoBack)
            return;

        _forward.Push(Location);
        Show(_back.Pop());
    }

    public void GoForward()
    {
        if (!CanGoForward)
            return;

        _back.Push(Location);
        Show(_forward.Pop());
    }

    public void GoUp()
    {
        if (ParentOf(Location) is { } parent)
            Navigate(parent);
    }

    /// <summary>Rebuilds the current page in place, without a history entry.</summary>
    public void Refresh() => Show(Location);

    // History stores locations, not pages: going back re-reads the folder, so it's never stale
    private void Show(string location)
    {
        var previous = CurrentPage;
        CurrentPage = _createPage(this, location);
        OnPropertyChanged(nameof(Location));
        Navigated?.Invoke(this, EventArgs.Empty);
        previous.Dispose();
    }

    private static string? ParentOf(string location)
        => Locations.IsVirtual(location) ? null : IOPath.GetDirectoryName(location); // null for "/"

    protected override void OnDispose()
    {
        CurrentPage.Dispose();
        base.OnDispose();
    }
}
