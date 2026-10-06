using File.Commander.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace File.Commander.Themes;

/// <summary>
/// Adds color themes to Settings → Basic → Appearance → Theme, and a page under Settings → Plugins to choose
/// which of them the drop-down lists.
/// </summary>
public sealed class ThemesPlugin : IPlugin
{
    private IPluginSettings<ThemesSettings>? _settings;

    /// <summary>Never change it: picked themes are stored as "&lt;Id&gt;/&lt;theme id&gt;", and addon.json names it too.</summary>
    public string Id => "file-commander.themes";

    public string Name => "Extra Themes";

    public Version Version => typeof(ThemesPlugin).Assembly.GetName().Version ?? new Version(1, 0, 0);

    public void ConfigureServices(IServiceCollection services, IPluginContext context)
    {
        var settings = context.GetSettings<ThemesSettings>();
        _settings = settings;
        services.AddSingleton(settings);
        services.AddTransient<ThemesSettingsViewModel>();
    }

    public IEnumerable<PluginPage> GetPages() => [];

    public IEnumerable<PluginSettingsPage> GetSettingsPages()
        => [new PluginSettingsPage("Themes", typeof(ThemesSettingsViewModel), () => new ThemesSettingsView())];

    /// <summary>Every theme the user didn't hide on this plugin's settings page.</summary>
    public IEnumerable<PluginTheme> GetThemes()
    {
        var hidden = _settings?.Current.Hidden ?? [];
        return Palettes.All.Where(theme => !hidden.Contains(theme.Id, StringComparer.Ordinal));
    }
}
