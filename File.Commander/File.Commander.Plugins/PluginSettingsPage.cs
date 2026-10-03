using Avalonia.Controls;

namespace File.Commander.Plugins;

/// <summary>A tab the plugin adds to the Settings window. Shown only while the plugin is loaded.</summary>
/// <param name="Title">Tab header.</param>
/// <param name="ViewModelType">
/// Implements <see cref="IPluginSettingsViewModel"/>. Register it as <b>transient</b> in
/// <see cref="IPlugin.ConfigureServices"/>: the host resolves a fresh instance in its own scope
/// every time Settings opens and disposes that scope on the next opening.
/// </param>
/// <param name="CreateView">Called every time Settings opens. Must return a new control each call.</param>
public sealed record PluginSettingsPage(
    string Title,
    Type ViewModelType,
    Func<Control> CreateView);

/// <summary>
/// View model of a plugin settings tab. It follows the Settings dialog:
/// <see cref="LoadAsync"/> on open, <see cref="Validate"/> then <see cref="SaveAsync"/> on Save,
/// nothing on Cancel. All calls happen on the UI thread.
/// </summary>
public interface IPluginSettingsViewModel
{
    /// <summary>Fill the editable fields from stored settings.</summary>
    Task LoadAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Error text to keep the dialog open, or null if the page can be saved.
    /// Every page is validated before any page (or the host) saves.
    /// </summary>
    string? Validate() => null;

    Task SaveAsync(CancellationToken cancellationToken);
}

/// <summary>Typed, persisted plugin settings. Get it from <see cref="IPluginContext.GetSettings{T}"/>.</summary>
/// <typeparam name="T">Use a record with init-only properties and update it with <c>with { }</c>.</typeparam>
public interface IPluginSettings<T> where T : class, new()
{
    /// <summary>Last saved value, or defaults when nothing is saved yet (or the file is unreadable).</summary>
    T Current { get; }

    Task SaveAsync(T value, CancellationToken cancellationToken = default);

    /// <summary>
    /// Raised after a successful save, on the saving thread (usually the UI thread).
    /// Background services subscribe to apply changes without a restart.
    /// </summary>
    event EventHandler<T>? Changed;
}
