using System.ComponentModel;
using System.Runtime.CompilerServices;
using File.Commander.Plugins;

namespace File.Commander.Themes;

/// <summary>
/// Settings → Plugins → Extra Themes. Notifies on every change, so the host saves it at once, like the rest of
/// the Settings window, and the Theme drop-down follows.
/// </summary>
public sealed class ThemesSettingsViewModel : IPluginSettingsViewModel, INotifyPropertyChanged
{
    private readonly IPluginSettings<ThemesSettings> _settings;

    public ThemesSettingsViewModel(IPluginSettings<ThemesSettings> settings)
    {
        _settings = settings;
        Themes = Palettes.All.Select(theme => new ThemeToggle(theme.Id, theme.Title, theme.IsDark)).ToList();

        foreach (var toggle in Themes)
            toggle.PropertyChanged += (_, _) => OnPropertyChanged(nameof(Themes));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>One per theme of the plugin, in the order the drop-down lists them.</summary>
    public IReadOnlyList<ThemeToggle> Themes { get; }

    public Task LoadAsync(CancellationToken cancellationToken)
    {
        var hidden = _settings.Current.Hidden;
        foreach (var toggle in Themes)
            toggle.IsShown = !hidden.Contains(toggle.Id, StringComparer.Ordinal);

        return Task.CompletedTask;
    }

    public Task SaveAsync(CancellationToken cancellationToken)
        => _settings.SaveAsync(_settings.Current with
        {
            Hidden = Themes.Where(toggle => !toggle.IsShown).Select(toggle => toggle.Id).ToArray(),
        }, cancellationToken);

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>A theme's check box: listed in the Theme drop-down or not.</summary>
public sealed class ThemeToggle(string id, string title, bool isDark) : INotifyPropertyChanged
{
    private bool _isShown = true;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Id { get; } = id;

    public string Title { get; } = title;

    public bool IsDark { get; } = isDark;

    public bool IsShown
    {
        get => _isShown;
        set
        {
            if (_isShown == value)
                return;

            _isShown = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsShown)));
        }
    }
}
