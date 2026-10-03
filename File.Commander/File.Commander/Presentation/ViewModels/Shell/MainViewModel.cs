using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;
using File.Commander.Application.Path;

namespace File.Commander.Presentation.ViewModels.Shell;

public partial class MainViewModel : ViewModelBase
{
#pragma warning disable CA1822
    public bool IsNotHyprland => !DesktopEnvironmentHelper.IsHyprland();
#pragma warning restore CA1822

    [RelayCommand]
    public async Task Settings(Window? owner)
    {
        
    }
}