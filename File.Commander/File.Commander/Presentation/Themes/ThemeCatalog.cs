using Avalonia.Styling;

namespace File.Commander.Presentation.Themes;

/// <summary>An entry of the Theme drop-down: a built-in Catppuccin flavor, or a theme added by a plugin.</summary>
/// <param name="Key">The built-in theme's name ("Mocha"), or "&lt;plugin id&gt;/&lt;theme id&gt;".</param>
/// <param name="BuiltIn">Null for a plugin theme.</param>
public sealed record ThemeOption(string Key, string Title, AppTheme? BuiltIn);

/// <summary>
/// Every theme the app can show: the four Catppuccin flavors of App.axaml, then the themes of the loaded plugins
/// (<see cref="IPlugin.GetThemes"/>). Plugins are asked again on every call, so a plugin may change its list.
/// </summary>
public interface IThemeCatalog
{
    /// <summary>Built-in themes first, then the plugins' in load order.</summary>
    IReadOnlyList<ThemeOption> Options { get; }

    /// <summary>The <see cref="ThemeOption.Key"/> of the theme <paramref name="basic"/> shows.</summary>
    string KeyOf(BasicSettings basic);

    /// <summary>
    /// Shows the theme of <paramref name="basic"/> in every window: the plugin theme when it is still offered,
    /// else the built-in one. UI thread only.
    /// </summary>
    void Apply(Avalonia.Application app, BasicSettings basic);
}

internal sealed class ThemeCatalog(IEnumerable<LoadedPlugin> plugins) : IThemeCatalog
{
    private static readonly ThemeOption[] BuiltIns =
    [
        new(nameof(AppTheme.Latte), "Catppuccin Latte", AppTheme.Latte),
        new(nameof(AppTheme.Frappe), "Catppuccin Frappé", AppTheme.Frappe),
        new(nameof(AppTheme.Macchiato), "Catppuccin Macchiato", AppTheme.Macchiato),
        new(nameof(AppTheme.Mocha), "Catppuccin Mocha", AppTheme.Mocha),
    ];

    // Plugin themes already added to the app's ThemeDictionaries, by key. A theme whose colors changed
    // gets a new variant (the old one stays, unused): replacing the palette of the variant on screen
    // wouldn't repaint it.
    private readonly Dictionary<string, (PluginTheme Theme, ThemeVariant Variant)> _added = new(StringComparer.Ordinal);
    private int _generation;

    public IReadOnlyList<ThemeOption> Options
        => [.. BuiltIns, .. PluginThemes().Select(theme => new ThemeOption(theme.Key, theme.Theme.Title, null))];

    public string KeyOf(BasicSettings basic)
        => Find(basic.PluginThemeKey) is { } found ? found.Key : basic.Theme.ToString();

    public void Apply(Avalonia.Application app, BasicSettings basic)
    {
        var variant = Find(basic.PluginThemeKey) is { } found
            ? VariantOf(app, found.Key, found.Theme)
            : CatppuccinThemes.For(basic.Theme);

        if (app.RequestedThemeVariant != variant)
            app.RequestedThemeVariant = variant;
    }

    private (string Key, PluginTheme Theme)? Find(string? key)
    {
        if (string.IsNullOrEmpty(key))
            return null;

        foreach (var theme in PluginThemes())
        {
            if (theme.Key == key)
                return theme;
        }

        return null;
    }

    private ThemeVariant VariantOf(Avalonia.Application app, string key, PluginTheme theme)
    {
        if (_added.TryGetValue(key, out var known) && known.Theme == theme)
            return known.Variant;

        // Inherits Dark or Light, so Fluent's own controls pick the matching base look
        var variant = new ThemeVariant($"Plugin:{key}:{++_generation}", theme.IsDark ? ThemeVariant.Dark : ThemeVariant.Light);
        app.Resources.ThemeDictionaries[variant] = PluginThemeResources.Build(theme.Palette, theme.IsDark);

        _added[key] = (theme, variant);
        return variant;
    }

    /// <summary>Never throws: a plugin whose list can't be read adds nothing. Duplicate and invalid entries are skipped.</summary>
    private List<(string Key, PluginTheme Theme)> PluginThemes()
    {
        var result = new List<(string Key, PluginTheme Theme)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var loaded in plugins)
        {
            string pluginId;
            List<PluginTheme> themes;
            try
            {
                pluginId = loaded.Plugin.Id;
                themes = loaded.Plugin.GetThemes().ToList();
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Plugin in '{loaded.Directory}' failed to list its themes: {ex}");
                continue;
            }

            foreach (var theme in themes)
            {
                // A plugin built without nullable checks may still hand out nulls
                if (theme is null || string.IsNullOrWhiteSpace(theme.Id) || theme.Palette is null)
                    continue;

                var key = $"{pluginId}/{theme.Id}";
                if (seen.Add(key))
                    result.Add((key, theme));
            }
        }

        return result;
    }
}
