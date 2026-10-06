using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;

namespace File.Commander.Presentation.ViewModels.Shell;

/// <summary>
/// One tab of the window (Ctrl+T). Has its own views (one, or two side by side in split view),
/// its own active view and its own info panel. The title bar, address bar and sidebar act on the selected tab.
/// </summary>
public sealed partial class TabViewModel : ViewModelBase
{
    private readonly Func<string, DirectoryViewMode, PaneViewModel> _createPane;
    private PaneViewModel _activePane;

    public TabViewModel(string location, DirectoryViewMode viewMode,
        Func<string, DirectoryViewMode, PaneViewModel> createPane)
    {
        _createPane = createPane;
        _activePane = AddPane(location, viewMode, Panes.Count);
        UpdatePaneStates();
        InfoPanel.Attach(_activePane.CurrentPage);
    }

    /// <summary>The active view changed, or it navigated. The shell follows it while this tab is selected.</summary>
    public event EventHandler? ActivePageChanged;

    /// <summary>The tab's close button or a middle click on it.</summary>
    public event EventHandler? CloseRequested;

    /// <summary>A click on the tab.</summary>
    public event EventHandler? SelectRequested;

    /// <summary>The views of this tab: one, or two side by side in split view.</summary>
    public ObservableCollection<PaneViewModel> Panes { get; } = [];

    /// <summary>The details panel on the right, toggled with Space. Each tab has its own.</summary>
    public InfoPanelViewModel InfoPanel { get; } = new();

    /// <summary>The view the shell acts on while this tab is selected.</summary>
    public PaneViewModel ActivePane => _activePane;

    public bool IsSplit => Panes.Count > 1;

    /// <summary>The tab the window shows. Only one at a time; the others keep their state hidden.</summary>
    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    /// <summary>The tab can be closed: it isn't the last one.</summary>
    [ObservableProperty]
    public partial bool CanClose { get; set; }

    /// <summary>The active view's page title, shown on the tab.</summary>
    public string Title => ActivePane.CurrentPage.Title;

    /// <summary>The active view's location, shown as the tab's tooltip.</summary>
    public string Location => ActivePane.Location;

    public MaterialIconKind Icon => ActivePane.CurrentPage switch
    {
        ComputerViewModel => MaterialIconKind.Monitor,
        NetworkViewModel => MaterialIconKind.LanConnect,
        PlaceholderPageViewModel placeholder => placeholder.Icon,
        _ => MaterialIconKind.Folder,
    };

    /// <summary>Makes <paramref name="pane"/> the view the shell acts on. Ignores views of other tabs.</summary>
    public void ActivatePane(PaneViewModel pane)
    {
        if (ReferenceEquals(pane, _activePane) || !Panes.Contains(pane))
            return;

        _activePane = pane;
        UpdatePaneStates();
        OnActivePageChanged();
    }

    /// <summary>
    /// Splits the tab into two views, the new one opening the active view's folder in the same layout
    /// with a history of its own. While split, closes the other view and keeps the active one.
    /// </summary>
    public void ToggleSplitView()
    {
        if (IsSplit)
        {
            foreach (var pane in Panes.Where(p => !ReferenceEquals(p, _activePane)).ToList())
                ClosePane(pane);
        }
        else
        {
            AddPane(_activePane.Location, _activePane.ViewMode, Panes.IndexOf(_activePane) + 1);
        }

        UpdatePaneStates();
        OnPropertyChanged(nameof(IsSplit));
    }

    [RelayCommand]
    private void Select() => SelectRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand(CanExecute = nameof(CanClose))]
    private void Close() => CloseRequested?.Invoke(this, EventArgs.Empty);

    partial void OnCanCloseChanged(bool value) => CloseCommand.NotifyCanExecuteChanged();

    private PaneViewModel AddPane(string location, DirectoryViewMode viewMode, int index)
    {
        var pane = _createPane(location, viewMode);
        pane.Navigated += OnPaneNavigated;
        Panes.Insert(index, pane);
        return pane;
    }

    private void ClosePane(PaneViewModel pane)
    {
        pane.Navigated -= OnPaneNavigated;
        Panes.Remove(pane);
        pane.Dispose();
    }

    /// <summary>Only the active view drives the tab; the other one keeps its own path.</summary>
    private void OnPaneNavigated(object? sender, EventArgs e)
    {
        if (ReferenceEquals(sender, _activePane))
            OnActivePageChanged();
    }

    private void OnActivePageChanged()
    {
        InfoPanel.Attach(_activePane.CurrentPage);
        OnPropertyChanged(nameof(ActivePane));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Location));
        OnPropertyChanged(nameof(Icon));
        ActivePageChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdatePaneStates()
    {
        var split = IsSplit;
        foreach (var pane in Panes)
        {
            pane.IsSplit = split;
            pane.IsActive = ReferenceEquals(pane, _activePane);
        }
    }

    protected override void OnDispose()
    {
        InfoPanel.Dispose();

        foreach (var pane in Panes)
        {
            pane.Navigated -= OnPaneNavigated;
            pane.Dispose();
        }

        base.OnDispose();
    }
}
