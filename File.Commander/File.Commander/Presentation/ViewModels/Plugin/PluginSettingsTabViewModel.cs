using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;

namespace File.Commander.Presentation.ViewModels.Plugin;

public sealed partial class PluginSettingsTabViewModel(
    string pluginName, PluginSettingsPage page, IPluginSettingsViewModel inner, Control view) : ObservableObject
{
    public string PluginName { get; } = pluginName;
    public string Title => page.Title;
    public IPluginSettingsViewModel Inner { get; } = inner;
    public Control View { get; } = view;

    /// <summary>The page's title is shown above it when its plugin has more than one page.</summary>
    public bool ShowTitle { get; internal set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? Error { get; set; }

    public bool HasError => Error is not null;

    /// <summary>
    /// False when LoadAsync failed. The page is then disabled and never saved,
    /// so half-loaded fields can't overwrite stored settings.
    /// </summary>
    [ObservableProperty]
    public partial bool IsLoaded { get; set; }

    internal async Task LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Inner.LoadAsync(cancellationToken);
            IsLoaded = true;
            Error = null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Trace.WriteLine($"{PluginName}/{Title}: loading settings failed: {ex}");
            IsLoaded = false;
            Error = $"Failed to load settings: {ex.Message}";
        }
    }

    internal string? Validate()
    {
        if (!IsLoaded)
            return null;

        try
        {
            Error = Inner.Validate();
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"{PluginName}/{Title}: validation threw: {ex}");
            Error = ex.Message;
        }

        return Error;
    }

    internal async Task<bool> SaveAsync(CancellationToken cancellationToken)
    {
        if (!IsLoaded)
            return true;

        try
        {
            await Inner.SaveAsync(cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Trace.WriteLine($"{PluginName}/{Title}: saving settings failed: {ex}");
            Error = $"Failed to save: {ex.Message}";
            return false;
        }
    }
}

/// <summary>Settings tabs plus the DI scope their view models live in.</summary>
public sealed class PluginSettingsSession(IServiceScope? scope, IReadOnlyList<PluginSettingsTabViewModel> tabs)
    : IDisposable
{
    public static PluginSettingsSession Empty { get; } = new(null, []);

    public IReadOnlyList<PluginSettingsTabViewModel> Tabs { get; } = tabs;

    public void Dispose()
    {
        try
        {
            scope?.Dispose();
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Disposing plugin settings view models failed: {ex}");
        }
    }
}
