using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace File.Commander.Presentation.ViewModels.Settings;

/// <summary>
/// Settings → Plugins: the installed plugins (turn on or off, delete), then one section per loaded plugin
/// that has settings pages. Plugins are loaded at startup, so turning one on or off applies on the next start.
/// One instance per opening of the Settings window; dispose it when the window closes.
/// </summary>
public sealed partial class PluginsSettingsViewModel : ObservableObject, IDisposable
{
    private readonly IPluginUninstaller _uninstaller;
    private readonly IDialogService _dialogs;
    private readonly Action _addonsChanged;

    // Deleted (or deleted on the next start) in this opening: dropped from settings.json's Addons
    private readonly HashSet<string> _removedKeys = new(StringComparer.Ordinal);

    /// <param name="addonsChanged">A plugin was turned on or off, or deleted: save <see cref="ToAddons"/>.</param>
    /// <param name="pluginSettingsSaved">A plugin settings page saved: what plugins offer (themes…) may have changed.</param>
    public PluginsSettingsViewModel(IPluginCatalog catalog, IPluginRegistry registry, IPluginUninstaller uninstaller,
        IDialogService dialogs, AppSettings current, Action addonsChanged, Action pluginSettingsSaved)
    {
        _uninstaller = uninstaller;
        _dialogs = dialogs;
        _addonsChanged = addonsChanged;
        FolderHint = $"Plugins are installed in {catalog.Root}";

        foreach (var plugin in catalog.Installed)
            Items.Add(new PluginItemViewModel(this, plugin, current.IsAddonEnabled(plugin.Descriptor.Key)));

        var sections = new List<PluginSettingsSectionViewModel>();
        foreach (var plugin in catalog.Installed)
        {
            if (plugin.Status != PluginLoadStatus.Loaded || !registry.HasSettings(plugin.Descriptor.Directory))
                continue;

            var session = registry.CreateSettingsSession(plugin.Descriptor.Directory);
            if (session.Tabs.Count == 0)
            {
                session.Dispose();
                continue;
            }

            var section = new PluginSettingsSectionViewModel(plugin, session);
            section.Saved += (_, _) => pluginSettingsSaved();
            sections.Add(section);
        }

        Sections = sections;
    }

    public ObservableCollection<PluginItemViewModel> Items { get; } = [];

    /// <summary>Fixed for one opening: a plugin deleted meanwhile keeps its section until the next start.</summary>
    public IReadOnlyList<PluginSettingsSectionViewModel> Sections { get; }

    public bool HasPlugins => Items.Count > 0;

    /// <summary>Where plugins go: each in a folder named like its assembly.</summary>
    public string FolderHint { get; }

    /// <summary>A plugin was turned on or off, or deleted while it runs: the change waits for a restart.</summary>
    public bool NeedsRestart => Items.Any(item => item.NeedsRestart);

    /// <summary>settings.json's Addons as this page has them: every listed plugin's switch, deleted ones dropped.</summary>
    internal Dictionary<string, bool> ToAddons(Dictionary<string, bool>? stored)
    {
        var addons = stored is null
            ? new Dictionary<string, bool>(StringComparer.Ordinal)
            : new Dictionary<string, bool>(stored, StringComparer.Ordinal);

        foreach (var key in _removedKeys)
            addons.Remove(key);

        foreach (var item in Items)
        {
            if (!item.IsRemovalPending)
                addons[item.Key] = item.IsEnabled;
        }

        return addons;
    }

    internal void OnEnabledChanged()
    {
        OnPropertyChanged(nameof(NeedsRestart));
        _addonsChanged();
    }

    /// <summary>
    /// Delete: asks first, then removes the plugin's folder and data. A plugin that runs (or whose folder is
    /// locked) is deleted on the next start, before it loads.
    /// </summary>
    internal async Task RemoveAsync(PluginItemViewModel item)
    {
        using (var confirm = PromptViewModel.ForConfirmation(
                   $"Delete “{item.Name}”?",
                   "The plugin and its settings are deleted from this computer. This can't be undone.",
                   "Delete",
                   destructive: true))
        {
            if (!await _dialogs.ShowDialogAsync<PromptViewModel, bool>(confirm))
                return;
        }

        item.IsBusy = true;
        item.Error = null;
        try
        {
            var result = await _uninstaller.RemoveAsync(item.Plugin);
            _removedKeys.Add(item.Key);

            if (result == PluginRemovalResult.Removed)
            {
                Items.Remove(item);
                OnPropertyChanged(nameof(HasPlugins));
            }
            else
            {
                item.IsRemovalPending = true;
            }

            _addonsChanged();
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Can't delete plugin '{item.Plugin.Descriptor.Directory}': {ex}");
            item.Error = $"Couldn't delete it: {ex.Message}";
        }
        finally
        {
            item.IsBusy = false;
            OnPropertyChanged(nameof(NeedsRestart));
        }
    }

    public void Dispose()
    {
        foreach (var section in Sections)
            section.Dispose();
    }
}

/// <summary>A row of Settings → Plugins → Plugins list.</summary>
public sealed partial class PluginItemViewModel : ObservableObject
{
    private readonly PluginsSettingsViewModel _owner;
    private readonly bool _ready;

    internal PluginItemViewModel(PluginsSettingsViewModel owner, InstalledPlugin plugin, bool isEnabled)
    {
        _owner = owner;
        Plugin = plugin;
        IsEnabled = isEnabled;
        IsRemovalPending = plugin.IsRemovalPending;
        Error = plugin.Status == PluginLoadStatus.Failed ? plugin.Error ?? "It failed to load." : null;
        _ready = true;
    }

    public InstalledPlugin Plugin { get; }

    /// <summary>Its folder name: the key of settings.json's Addons.</summary>
    public string Key => Plugin.Descriptor.Key;

    public string Name => Plugin.Descriptor.Name;

    public string Version => Plugin.Descriptor.Version is "—" ? string.Empty : $"v{Plugin.Descriptor.Version}";

    public string? Description => Plugin.Descriptor.Description;

    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

    /// <summary>Whether it is in this process now: loaded, or tried and failed.</summary>
    private bool Runs => Plugin.Status != PluginLoadStatus.Disabled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(NeedsRestart))]
    public partial bool IsEnabled { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(NeedsRestart), nameof(CanToggle))]
    [NotifyCanExecuteChangedFor(nameof(RemoveCommand))]
    public partial bool IsRemovalPending { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanToggle))]
    [NotifyCanExecuteChangedFor(nameof(RemoveCommand))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? Error { get; set; }

    public bool HasError => !string.IsNullOrEmpty(Error);

    public bool CanToggle => !IsBusy && !IsRemovalPending;

    /// <summary>A pending deletion always finishes on the next start; a switch only matters when it differs from now.</summary>
    public bool NeedsRestart => IsRemovalPending || IsEnabled != Runs;

    public string StatusText => (IsRemovalPending, IsEnabled, Plugin.Status) switch
    {
        (true, _, _) => "Deleted when File Commander restarts",
        (false, true, PluginLoadStatus.Loaded) => "Running",
        (false, true, PluginLoadStatus.Failed) => "Failed to load",
        (false, true, _) => "Turns on when File Commander restarts",
        (false, false, PluginLoadStatus.Disabled) => "Off",
        _ => "Turns off when File Commander restarts",
    };

    partial void OnIsEnabledChanged(bool value)
    {
        if (_ready)
            _owner.OnEnabledChanged();
    }

    private bool CanRemove() => !IsBusy && !IsRemovalPending;

    [RelayCommand(CanExecute = nameof(CanRemove))]
    private Task Remove() => _owner.RemoveAsync(this);
}

/// <summary>
/// Settings → Plugins → one plugin: its settings pages, loaded when the window opens. Pages whose view model
/// notifies are saved as they change, like the rest of the window; the others get a Save button.
/// </summary>
public sealed partial class PluginSettingsSectionViewModel : ObservableObject, IDisposable
{
    private readonly PluginSettingsSession _session;
    private readonly CancellationTokenSource _cts = new();
    private readonly HashSet<PluginSettingsTabViewModel> _dirty = [];
    private bool _saving;
    private bool _disposed;

    internal PluginSettingsSectionViewModel(InstalledPlugin plugin, PluginSettingsSession session)
    {
        _session = session;
        NavKey = "plugin:" + plugin.Descriptor.Key;
        Title = plugin.Descriptor.Name;

        foreach (var page in Pages)
            page.ShowTitle = Pages.Count > 1;

        HasSaveButton = Pages.Any(page => page.Inner is not INotifyPropertyChanged);
        _ = LoadAsync();
    }

    /// <summary>A page saved: what the plugin offers (themes…) may depend on it.</summary>
    public event EventHandler? Saved;

    /// <summary>The Key of its navigation entry and the Tag of its heading.</summary>
    public string NavKey { get; }

    public string Title { get; }

    public IReadOnlyList<PluginSettingsTabViewModel> Pages => _session.Tabs;

    public bool HasSaveButton { get; }

    [ObservableProperty]
    public partial string? SavedText { get; set; }

    private async Task LoadAsync()
    {
        try
        {
            foreach (var page in Pages)
            {
                await page.LoadAsync(_cts.Token);

                // After loading: filling the fields isn't a change to save
                if (page.IsLoaded && page.Inner is INotifyPropertyChanged notifying && !_disposed)
                    notifying.PropertyChanged += OnPageChanged;
            }
        }
        catch (OperationCanceledException)
        {
            // The window closed while a page was loading
        }
    }

    /// <summary>Saves the changed page; changes made while saving are saved right after, one save at a time.</summary>
    private async void OnPageChanged(object? sender, PropertyChangedEventArgs e)
    {
        var page = Pages.FirstOrDefault(p => ReferenceEquals(p.Inner, sender));
        if (page is null || _disposed)
            return;

        _dirty.Add(page);
        if (_saving)
            return;

        _saving = true;
        try
        {
            while (_dirty.Count > 0 && !_disposed)
            {
                var next = _dirty.First();
                _dirty.Remove(next);
                await SavePageAsync(next);
            }
        }
        finally
        {
            _saving = false;
        }
    }

    /// <summary>Save, for the pages that don't save as they change.</summary>
    [RelayCommand]
    private async Task Save()
    {
        SavedText = null;
        var manual = Pages.Where(page => page.Inner is not INotifyPropertyChanged).ToList();

        // Every page first, so nothing is written while one of them is invalid
        if (manual.Select(page => page.Validate()).ToList().Any(error => error is not null))
            return;

        var saved = true;
        foreach (var page in manual)
            saved &= await SavePageAsync(page, validated: true);

        if (saved)
            SavedText = "Saved";
    }

    private async Task<bool> SavePageAsync(PluginSettingsTabViewModel page, bool validated = false)
    {
        if (!validated && page.Validate() is not null)
            return false;

        // Not cancelled by closing the window: the last change should still be stored
        if (!await page.SaveAsync(CancellationToken.None))
            return false;

        Saved?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _cts.Cancel();

        foreach (var page in Pages)
        {
            if (page.Inner is INotifyPropertyChanged notifying)
                notifying.PropertyChanged -= OnPageChanged;
        }

        _session.Dispose();
        _cts.Dispose();
    }
}
