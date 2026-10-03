using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;
using File.Commander.Application.Path;
using File.Commander.Presentation.Services;
using File.Commander.Presentation.ViewModels.Computer;

namespace File.Commander.Presentation.ViewModels.Shell;

public partial class MainViewModel : ViewModelBase
{
    public SidebarViewModel Sidebar { get; }
    public ComputerViewModel Computer { get; }
    
#pragma warning disable CA1822
    public bool IsNotHyprland => !DesktopEnvironmentHelper.IsHyprland();
#pragma warning restore CA1822

    public MainViewModel()
    {
        var directories = SystemLocations.GetUserDirectories();
        var volumes = SystemLocations.GetVolumes();
        Sidebar = new SidebarViewModel(directories, volumes);
        Computer = new ComputerViewModel(directories, volumes);
    }

    [RelayCommand]
    public async Task Settings(CancellationToken cancellationToken = default)
    {
        /*var result = await _dialogService.ShowDialogAsync<SettingsViewModel, bool>(_settingsViewModel);*/
    }
}