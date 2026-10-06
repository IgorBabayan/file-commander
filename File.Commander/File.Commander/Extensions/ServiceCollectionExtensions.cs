using Microsoft.Extensions.DependencyInjection;

namespace File.Commander.Extensions;

static class ServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection RegisterViewModels() 
            => services
                .AddSingleton<ActionCenterViewModel>()
                .AddSingleton<MainViewModel>();

        public IServiceCollection RegisterPlugins()
        {
            var pluginsRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "file-commander", "plugins");

            var catalog = PluginCatalog.PluginCatalog.Create(pluginsRoot);
            var startupSettings = LoadStartupSettings();
            
            foreach (var loaded in catalog.LoadEnabled(startupSettings.IsAddonEnabled))
            {
                try
                {
                    var dataDir = Directory.CreateDirectory(
                        Path.Combine(pluginsRoot, "_data", loaded.Plugin.Id)).FullName;

                    // Register into a scratch collection first: a plugin that throws halfway
                    // through ConfigureServices leaves nothing behind in the host container
                    var pluginServices = new ServiceCollection();
                    loaded.Plugin.ConfigureServices(pluginServices,
                        new PluginContext(loaded.Directory, dataDir));

                    foreach (var descriptor in pluginServices)
                        services.Add(descriptor);

                    services.AddSingleton(loaded);
                }
                catch (Exception ex)
                {
                    catalog.MarkFailed(loaded.Directory, ex);
                    Trace.WriteLine($"Plugin in '{loaded.Directory}' failed to register services: {ex}");
                }
            }
        
            return services.AddSingleton(catalog)
                .AddSingleton<IPluginCatalog>(catalog)
                .AddSingleton<IPluginUninstaller, PluginUninstaller>()
                .AddSingleton<IPluginRegistry, PluginRegistry>();
        }

        public IServiceCollection RegisterServices()
        {
            return services
                .AddSingleton<IDesktopService, DesktopService>()    
                .AddSingleton<IDialogService, DialogService>()
                .AddSingleton<ISettingsService>(_ => new SettingsService(SettingsService.DefaultPath))
                .AddSingleton<IKeymapService, KeymapService>()
                .AddSingleton<IThemeCatalog, ThemeCatalog>()
                .AddSingleton<ISettingsViewModelFactory, SettingsViewModelFactory>()
                .AddTransient<SettingsWindow>()
                .AddTransient<PromptWindow>()
                .AddTransient<PasteConflictWindow>()
                .AddTransient<PropertiesWindow>()
                .AddTransient<NetworkLoginWindow>();
        }
        
        private static AppSettings LoadStartupSettings()
        {
            try
            {
                return SettingsService.Load(SettingsService.DefaultPath);
            }
            catch (Exception ex)
            {
                // A broken or unreadable config must not stop the app: every addon counts as enabled
                Trace.WriteLine($"Can't read settings at startup: {ex}");
                return new AppSettings();
            }
        }
    }
}