using Avalonia.Controls;
using File.Commander.Presentation.ViewModels.Settings;
using File.Commander.Presentation.Views.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace File.Commander.Application.IDialogService;

class DialogService : IDialogService
{
    private readonly IServiceProvider _serviceProvider;

    public DialogService(IServiceProvider serviceProvider) => _serviceProvider = serviceProvider;

    public async Task<TResult?> ShowDialogAsync<TViewModel, TResult>(TViewModel viewModel)
        where TViewModel : ViewModelBase
    {
        // The topmost window, so a dialog opened from another dialog shows above it
        var owner = Utils.GetTopWindow();
        Window dialog = viewModel switch
        {
            SettingsViewModel => _serviceProvider.GetRequiredService<SettingsWindow>(),
            global::File.Commander.Presentation.ViewModels.Dialogs.PromptViewModel
                => _serviceProvider.GetRequiredService<global::File.Commander.Presentation.Views.Dialogs.PromptWindow>(),
            global::File.Commander.Presentation.ViewModels.Dialogs.PropertiesViewModel
                => _serviceProvider.GetRequiredService<global::File.Commander.Presentation.Views.Dialogs.PropertiesWindow>(),
            /*ConfirmDialogViewModel => _serviceProvider.GetRequiredService<ConfirmDialogWindow>(),
            ImportFolderViewModel => _serviceProvider.GetRequiredService<ImportFolderWindow>(),
            PluginSettingsDialogViewModel => _serviceProvider.GetRequiredService<PluginSettingsDialogWindow>(),*/

            _ => throw new InvalidOperationException($"No dialog registered for {typeof(TViewModel).Name}")
        };

        dialog.DataContext = viewModel;
        dialog.Width = owner.Bounds.Width;
        dialog.Height = owner.Bounds.Height;
        dialog.Position = owner.Position;
        return await dialog.ShowDialog<TResult?>(owner);
    }
}