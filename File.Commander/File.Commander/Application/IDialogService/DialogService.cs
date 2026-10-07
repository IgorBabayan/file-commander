using Avalonia.Controls;
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
            PromptViewModel => _serviceProvider.GetRequiredService<PromptWindow>(),
            PasteConflictViewModel => _serviceProvider.GetRequiredService<PasteConflictWindow>(),
            PropertiesViewModel => _serviceProvider.GetRequiredService<PropertiesWindow>(),
            NetworkLoginViewModel => _serviceProvider.GetRequiredService<NetworkLoginWindow>(),
            SearchViewModel => _serviceProvider.GetRequiredService<SearchWindow>(),
            OpenWithViewModel => _serviceProvider.GetRequiredService<OpenWithWindow>(),
            
            _ => throw new InvalidOperationException($"No dialog registered for {typeof(TViewModel).Name}")
        };

        dialog.DataContext = viewModel;
        dialog.Width = owner.Bounds.Width;
        dialog.Height = owner.Bounds.Height;
        dialog.Position = owner.Position;
        return await dialog.ShowDialog<TResult?>(owner);
    }
}