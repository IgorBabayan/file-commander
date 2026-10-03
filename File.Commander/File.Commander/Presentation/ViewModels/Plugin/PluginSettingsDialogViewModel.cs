using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace File.Commander.Presentation.ViewModels.Plugin;

public sealed partial class PluginSettingsDialogViewModel : ViewModelBase
{
	private readonly IPluginRegistry _pluginRegistry;
	private PluginSettingsSession _session = PluginSettingsSession.Empty;

	[ObservableProperty]
	public partial string Title { get; set; } = "Plugin settings";

	[ObservableProperty]
	public partial PluginSettingsTabViewModel? SelectedTab { get; set; }

	/// <summary>Why Save kept the dialog open.</summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(HasSaveError))]
	public partial string? SaveError { get; set; }

	public bool HasSaveError => SaveError is not null;

	public ObservableCollection<PluginSettingsTabViewModel> Tabs { get; } = [];

	public bool HasTabs => Tabs.Count > 0;

	public event Action<bool?>? CloseRequested;

	public PluginSettingsDialogViewModel(IPluginRegistry pluginRegistry)
	{
		_pluginRegistry = pluginRegistry;
	}

	public async Task InitializeAsync(InstalledPlugin plugin, CancellationToken cancellationToken = default)
	{
		Title = $"{plugin.Descriptor.Name} settings";
		SaveError = null;

		Tabs.Clear();
		_session.Dispose();
		_session = _pluginRegistry.CreateSettingsSession(plugin.Descriptor.Directory);

		foreach (var tab in _session.Tabs)
		{
			await tab.LoadAsync(cancellationToken);
			Tabs.Add(tab);
		}

		SelectedTab = Tabs.FirstOrDefault();
		OnPropertyChanged(nameof(HasTabs));
	}

	protected override void OnDispose()
	{
		Tabs.Clear();
		_session.Dispose();

		base.OnDispose();
	}

	[RelayCommand]
	private async Task Save(CancellationToken cancellationToken = default)
	{
		SaveError = null;

		// Validate every page first, so nothing is written when one of them is invalid
		var invalid = Tabs
			.Select(tab => (Tab: tab, Error: tab.Validate()))
			.Where(x => x.Error is not null)
			.ToList();

		if (invalid.Count > 0)
		{
			SelectedTab = invalid[0].Tab;
			SaveError = string.Join("; ", invalid.Select(x => $"{x.Tab.Title}: {x.Error}"));
			return;
		}

		var failed = new List<PluginSettingsTabViewModel>();
		foreach (var tab in Tabs)
		{
			if (!await tab.SaveAsync(cancellationToken))
				failed.Add(tab);
		}

		// Pressing Save again is safe: every save is a full overwrite
		if (failed.Count > 0)
		{
			SelectedTab = failed[0];
			SaveError = $"Couldn't save: {string.Join(", ", failed.Select(t => t.Title))}";
			return;
		}

		CloseRequested?.Invoke(true);
	}

	[RelayCommand]
	private void Cancel() => CloseRequested?.Invoke(false);
}
