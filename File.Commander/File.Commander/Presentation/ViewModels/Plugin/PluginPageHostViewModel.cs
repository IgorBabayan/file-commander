using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;

namespace File.Commander.Presentation.ViewModels.Plugin;

public sealed partial class PluginPageHostViewModel : ViewModelBase
{
    public PluginPage Page { get; }
    public IPluginPageViewModel Inner { get; }
    public Control View { get; }

    public PluginPageHostViewModel(PluginPage page, IPluginPageViewModel inner)
    {
        Page = page;
        Inner = inner;
        View = page.CreateView();
        View.DataContext = inner;
    }
}

public interface IPluginRegistry
{
    IReadOnlyList<PluginPageHostViewModel> Pages { get; }

    /// <summary>Whether the plugin loaded from <paramref name="pluginDirectory"/> has settings pages.</summary>
    bool HasSettings(string pluginDirectory);

    /// <summary>
    /// Fresh settings pages of the plugin loaded from <paramref name="pluginDirectory"/>, for one
    /// opening of the Settings window. Dispose the session when the window closes.
    /// Never throws: a broken page is skipped.
    /// </summary>
    PluginSettingsSession CreateSettingsSession(string pluginDirectory);
}

internal sealed class PluginRegistry(IEnumerable<LoadedPlugin> plugins, IServiceProvider sp) : IPluginRegistry
{
    private sealed record SettingsEntry(IPlugin Plugin, string Directory, IReadOnlyList<PluginSettingsPage> Pages);

    private IReadOnlyList<PluginPageHostViewModel>? _pages;
    private IReadOnlyList<SettingsEntry>? _settings;

    public IReadOnlyList<PluginPageHostViewModel> Pages => _pages ??= plugins
        .SelectMany(p => SafeGet(p.Plugin, plugin => plugin.GetPages()))
        .Select(page => ActivatorUtilities.CreateInstance<PluginPageHostViewModel>(
            sp, page, (IPluginPageViewModel)sp.GetRequiredService(page.ViewModelType)))
        .ToList();

    // One assembly (= one addon folder) may contain several IPlugin types
    private IReadOnlyList<SettingsEntry> Settings => _settings ??= plugins
        .Select(p => new SettingsEntry(p.Plugin, p.Directory, SafeGet(p.Plugin, plugin => plugin.GetSettingsPages())))
        .Where(e => e.Pages.Count > 0)
        .ToList();

    public bool HasSettings(string pluginDirectory) => Settings.Any(e => IsFrom(e, pluginDirectory));

    public PluginSettingsSession CreateSettingsSession(string pluginDirectory)
    {
        var entries = Settings.Where(e => IsFrom(e, pluginDirectory)).ToList();
        if (entries.Count == 0)
            return PluginSettingsSession.Empty;

        // Own scope: transient settings view models are disposed with it, not kept by the root container
        var scope = sp.CreateScope();
        var tabs = new List<PluginSettingsTabViewModel>();

        foreach (var (plugin, _, pages) in entries)
        {
            foreach (var page in pages)
            {
                try
                {
                    var inner = (IPluginSettingsViewModel)scope.ServiceProvider.GetRequiredService(page.ViewModelType);
                    var view = page.CreateView();
                    view.DataContext = inner;
                    tabs.Add(new PluginSettingsTabViewModel(plugin.Name, page, inner, view));
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"{plugin.Id}: settings page '{page.Title}' failed to create: {ex}");
                }
            }
        }

        return new PluginSettingsSession(scope, tabs);
    }

    private static bool IsFrom(SettingsEntry entry, string pluginDirectory)
        => string.Equals(entry.Directory, pluginDirectory, StringComparison.Ordinal);

    private static IReadOnlyList<T> SafeGet<T>(IPlugin plugin, Func<IPlugin, IEnumerable<T>> get)
    {
        try { return get(plugin).ToList(); }
        catch (Exception ex) { Trace.WriteLine($"{plugin.Id}: {ex}"); return []; }
    }
}
