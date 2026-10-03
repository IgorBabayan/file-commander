using CommunityToolkit.Mvvm.Input;
using File.Commander.Application.Path;
using File.Commander.Presentation.Services;
using File.Commander.Presentation.ViewModels.Browser;
using File.Commander.Presentation.ViewModels.Computer;
using File.Commander.Presentation.ViewModels.Pages;
using Material.Icons;

namespace File.Commander.Presentation.ViewModels.Shell;

public partial class MainViewModel : ViewModelBase, INavigator
{
    private readonly Stack<string> _back = new();
    private readonly Stack<string> _forward = new();
    private PageViewModel _currentPage;

    public SidebarViewModel Sidebar { get; }

    public AddressBarViewModel AddressBar { get; }

#pragma warning disable CA1822
    public bool IsNotHyprland => !DesktopEnvironmentHelper.IsHyprland();
#pragma warning restore CA1822

    public MainViewModel()
    {
        Sidebar = new SidebarViewModel(SystemLocations.GetUserDirectories(), SystemLocations.GetVolumes());
        Sidebar.NavigationRequested += (_, location) => Navigate(location);
        AddressBar = new AddressBarViewModel(this);

        _currentPage = CreatePage(Locations.Computer);
        Sidebar.Select(Locations.Computer);
        AddressBar.Update(Locations.Computer);
    }

    /// <summary>What the content area shows. Replaced (and the old one disposed) on every navigation.</summary>
    public PageViewModel CurrentPage
    {
        get => _currentPage;
        private set => SetProperty(ref _currentPage, value);
    }

    /// <summary>Opens <paramref name="location"/> as a new history entry. Clears Forward.</summary>
    public void Navigate(string location)
    {
        location = Locations.Normalize(location);
        if (Locations.AreEqual(location, CurrentPage.Location))
            return;

        _back.Push(CurrentPage.Location);
        _forward.Clear();
        Show(location);
    }

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void GoBack()
    {
        _forward.Push(CurrentPage.Location);
        Show(_back.Pop());
    }

    private bool CanGoBack() => _back.Count > 0;

    [RelayCommand(CanExecute = nameof(CanGoForward))]
    private void GoForward()
    {
        _back.Push(CurrentPage.Location);
        Show(_forward.Pop());
    }

    private bool CanGoForward() => _forward.Count > 0;

    [RelayCommand(CanExecute = nameof(CanGoUp))]
    private void GoUp()
    {
        if (ParentOf(CurrentPage.Location) is { } parent)
            Navigate(parent);
    }

    private bool CanGoUp() => ParentOf(CurrentPage.Location) is not null;

    [RelayCommand]
    private void GoComputer() => Navigate(Locations.Computer);

    /// <summary>Rebuilds the current page in place, without a history entry.</summary>
    [RelayCommand]
    private void Refresh() => Show(CurrentPage.Location);

    [RelayCommand]
    public async Task Settings(CancellationToken cancellationToken = default)
    {
        /*var result = await _dialogService.ShowDialogAsync<SettingsViewModel, bool>(_settingsViewModel);*/
    }

    protected override void OnDispose()
    {
        CurrentPage.Dispose();
        base.OnDispose();
    }

    private void Show(string location)
    {
        var previous = CurrentPage;
        CurrentPage = CreatePage(location);
        previous.Dispose();

        Sidebar.Select(location);
        AddressBar.Update(location);

        GoBackCommand.NotifyCanExecuteChanged();
        GoForwardCommand.NotifyCanExecuteChanged();
        GoUpCommand.NotifyCanExecuteChanged();
    }

    // History stores locations, not pages: going back re-reads the folder, so it's never stale
    private PageViewModel CreatePage(string location)
    {
        switch (location)
        {
            case Locations.Computer:
            {
                // Re-query so mounted/unmounted drives and free space are current
                var volumes = SystemLocations.GetVolumes();
                AddressBar.Volumes = volumes;
                return new ComputerViewModel(SystemLocations.GetUserDirectories(), volumes, this);
            }
            case Locations.Recent:
                return new PlaceholderPageViewModel(location, "Recent", MaterialIconKind.ClockOutline);
            case Locations.Trash:
                return new PlaceholderPageViewModel(location, "Trash", MaterialIconKind.TrashCanOutline);
            case Locations.Network:
                return new PlaceholderPageViewModel(location, "Network", MaterialIconKind.LanConnect);
        }

        var directory = new DirectoryViewModel(location, this);
        _ = directory.LoadAsync(); // never throws, reports errors through Error
        return directory;
    }

    private static string? ParentOf(string location)
        => Locations.IsVirtual(location) ? null : IOPath.GetDirectoryName(location); // null for "/"
}
