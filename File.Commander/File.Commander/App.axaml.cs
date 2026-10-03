using System.Linq;
using System.Threading;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using File.Commander.PluginCatalog;
using File.Commander.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace File.Commander;

public partial class App : Avalonia.Application
{
    private readonly CancellationTokenSource _pluginLifetime = new();
    private ServiceProvider? _provider;
    
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        var services = new ServiceCollection();
        services.RegisterViewModels()
            .RegisterPlugins();
        
        var provider = _provider = services.BuildServiceProvider();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            try
            {
                provider.GetRequiredService<IPluginUninstaller>().CompletePendingRemovals();
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Completing pending addon removals failed: {ex}");
            }
            
            desktop.MainWindow = new MainWindow
            {
                DataContext = provider.GetRequiredService<MainViewModel>()
            };
            
            StartPluginServices(provider);
        }

        base.OnFrameworkInitializationCompleted();
    }
    
    internal void DisposeServices()
    {
        /*if (_provider is null)
            return;

        // Stop plugin background work first: it may still be reading journals or writing to the DB
        _pluginLifetime.Cancel();

        _mainViewModel?.Dispose();
        // Stop background database work before disposing its dependencies.
        if (_mainViewModel is not null)
            _provider.GetRequiredService<IJournalWatchService>().Dispose();
        _provider.Dispose();
        Presentation.Converters.BiologyImageConverter.ClearCache();
        _provider = null;*/
    }
    
    private void StartPluginServices(IServiceProvider provider)
    {
        List<IPluginBackgroundService> pluginServices;
        try
        {
            pluginServices = provider.GetServices<IPluginBackgroundService>().ToList();
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Plugin background services could not be created: {ex}");
            return;
        }

        foreach (var service in pluginServices)
            _ = RunPluginServiceAsync(service, _pluginLifetime.Token);
    }
    
    private static async Task RunPluginServiceAsync(IPluginBackgroundService service, CancellationToken cancellationToken)
    {
        try
        {
            // Task.Run: a plugin doing synchronous work before its first await must not block the UI
            await Task.Run(() => service.StartAsync(cancellationToken), cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Plugin service {service.GetType().FullName} failed: {ex}");
        }
    }
}