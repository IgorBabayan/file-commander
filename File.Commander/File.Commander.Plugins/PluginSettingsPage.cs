using Avalonia.Controls;

namespace File.Commander.Plugins;

/// <summary>
/// A page the plugin adds to the Settings window, under Plugins → its own entry. Shown only while the plugin
/// is loaded. A plugin with several pages gets one entry with every page under its title.
/// </summary>
/// <param name="Title">Shown above the page when the plugin has more than one.</param>
/// <param name="ViewModelType">
/// Implements <see cref="IPluginSettingsViewModel"/>. Register it as <b>transient</b> in
/// <see cref="IPlugin.ConfigureServices"/>: the host resolves a fresh instance in its own scope
/// every time Settings opens and disposes that scope when the window closes.
/// </param>
/// <param name="CreateView">Called every time Settings opens. Must return a new control each call.</param>
public sealed record PluginSettingsPage(
    string Title,
    Type ViewModelType,
    Func<Control> CreateView);

/// <summary>
/// View model of a plugin settings page. The Settings window has no Save button: <see cref="LoadAsync"/> runs
/// when it opens, and when the view model implements <see cref="System.ComponentModel.INotifyPropertyChanged"/>,
/// every property change is followed by <see cref="Validate"/> and, when that passes, <see cref="SaveAsync"/>.
/// A view model that doesn't notify gets a Save button under its page instead. All calls happen on the UI thread.
/// </summary>
public interface IPluginSettingsViewModel
{
    /// <summary>Fill the editable fields from stored settings. Changes raised meanwhile aren't saved.</summary>
    Task LoadAsync(CancellationToken cancellationToken);

    /// <summary>Error text shown under the page, or null if the page can be saved. Nothing is saved while it fails.</summary>
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
