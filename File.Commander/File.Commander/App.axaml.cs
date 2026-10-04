using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using File.Commander.Application.Settings;
using File.Commander.Presentation.Themes;
using File.Commander.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace File.Commander;

public partial class App : Avalonia.Application
{
    private readonly CancellationTokenSource _pluginLifetime = new();
    private ServiceProvider? _provider;
    private MainViewModel? _mainViewModel;
    private ISettingsService? _settings;
    
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        var services = new ServiceCollection();
        services.RegisterServices()
            .RegisterViewModels()
            .RegisterPlugins();
        
        var provider = _provider = services.BuildServiceProvider();

        // Before the first window, so it opens in the stored theme
        _settings = provider.GetRequiredService<ISettingsService>();
        ApplyTheme(_settings.Current.Basic!.Theme);
        _settings.Changed += OnSettingsChanged;

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
                DataContext = _mainViewModel = provider.GetRequiredService<MainViewModel>()
            };
            
            StartPluginServices(provider);
        }

        base.OnFrameworkInitializationCompleted();
    }
    
    internal void DisposeServices()
    {
        if (_provider is null)
            return;

        // Stop plugin background work first: it may still be reading journals or writing to the DB
        _pluginLifetime.Cancel();

        if (_settings is not null)
            _settings.Changed -= OnSettingsChanged;

        _mainViewModel?.Dispose();
       
        _provider.Dispose();
        //Presentation.Converters.BiologyImageConverter.ClearCache();
        _provider = null;
    }
    
    private void OnSettingsChanged(object? sender, AppSettings settings)
    {
        var theme = settings.Basic!.Theme;
        if (Dispatcher.UIThread.CheckAccess())
            ApplyTheme(theme);
        else
            Dispatcher.UIThread.Post(() => ApplyTheme(theme));
    }

    /// <summary>
    /// Windows don't set a variant of their own, so they all follow this one. The Theme*.axaml
    /// palettes are the app's ThemeDictionaries; every DynamicResource updates at once.
    /// </summary>
    private void ApplyTheme(AppTheme theme)
    {
        var variant = CatppuccinThemes.For(theme);
        if (RequestedThemeVariant != variant)
            RequestedThemeVariant = variant;
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