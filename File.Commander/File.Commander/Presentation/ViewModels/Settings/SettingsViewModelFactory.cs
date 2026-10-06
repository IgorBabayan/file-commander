namespace File.Commander.Presentation.ViewModels.Settings;

/// <summary>Creates the Settings window's view model, one per opening, with what its pages need.</summary>
public interface ISettingsViewModelFactory
{
    /// <param name="section">The navigation key to open at, e.g. <see cref="SettingsViewModel.TrashSection"/>.</param>
    SettingsViewModel Create(string? section = null);
}

internal sealed class SettingsViewModelFactory(
    ISettingsService settings,
    IThemeCatalog themes,
    IPluginCatalog plugins,
    IPluginRegistry pluginRegistry,
    IPluginUninstaller pluginUninstaller,
    IDialogService dialogs,
    IUpdateService updates) : ISettingsViewModelFactory
{
    public SettingsViewModel Create(string? section = null)
        => new(settings, themes, plugins, pluginRegistry, pluginUninstaller, dialogs, updates, section);
}
