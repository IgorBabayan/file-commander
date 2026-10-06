using Avalonia.Controls;
using Material.Icons;
using Microsoft.Extensions.DependencyInjection;

namespace File.Commander.Plugins;

public interface IPlugin
{
    string Id { get; }
    string Name { get; }
    Version Version { get; }

    void ConfigureServices(IServiceCollection services, IPluginContext context);

    IEnumerable<PluginPage> GetPages();

    /// <summary>
    /// Tabs this plugin adds to the Settings window. Default: none.
    /// A default member, so plugins built against an older Abstractions still load.
    /// </summary>
    IEnumerable<PluginSettingsPage> GetSettingsPages() => [];

    /// <summary>
    /// Color themes this plugin adds to Settings → Basic → Appearance → Theme. Default: none.
    /// Asked again every time Settings opens and when a theme is applied, so the list may depend on the
    /// plugin's own settings. A default member, so plugins built against an older SDK still load.
    /// </summary>
    IEnumerable<PluginTheme> GetThemes() => [];
}

public sealed record PluginPage(
    string Title,
    MaterialIconKind Icon,
    Type ViewModelType,          // must implement IPluginPageViewModel, registered in ConfigureServices
    Func<Control> CreateView);

public interface IPluginPageViewModel
{
    /// <summary>Called on a background thread. Marshal UI changes to Dispatcher.UIThread.</summary>
    Task OnJournalChangedAsync(PluginJournalSnapshot snapshot, CancellationToken ct);
    void OnNavigatedTo() { }
    void OnNavigatedFrom() { }
}

public interface IPluginContext
{
    string PluginDirectory { get; }
    string DataDirectory { get; }   // writable, per-plugin

    /// <summary>
    /// Settings of type <typeparamref name="T"/>, stored as JSON in <see cref="DataDirectory"/>
    /// (so they are deleted together with the addon's data). Returns the same instance for the same type.
    /// The host does not register it: call <c>services.AddSingleton(context.GetSettings&lt;MySettings&gt;())</c>.
    /// </summary>
    IPluginSettings<T> GetSettings<T>() where T : class, new();
}

public sealed record PluginJournalSnapshot();