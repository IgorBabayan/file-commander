using Avalonia.Controls;
using Avalonia.Threading;

namespace File.Commander.Presentation.Views.Shell;

/// <summary>
/// The Search button in the title bar, with the search bar under it. A click or Ctrl+F opens the bar; while the
/// button isn't on the toolbar, Ctrl+F opens the search dialog instead (MainWindow). DataContext: <see cref="SearchViewModel"/>.
/// </summary>
public partial class SearchButton : UserControl
{
    private SearchViewModel? _viewModel;

    // Shown again after Browse…: keeps the folder that was just picked
    private bool _reopening;

    public SearchButton()
    {
        InitializeComponent();

        if (Toggle.Flyout is Flyout flyout)
        {
            flyout.Opening += OnFlyoutOpening;
            flyout.Opened += (_, _) => Dispatcher.UIThread.Post(() => Bar?.FocusQuery(), DispatcherPriority.Loaded);
        }
    }

    private SearchPanel? Bar => (Toggle.Flyout as Flyout)?.Content as SearchPanel;

    /// <summary>Opens the bar under the button. False when the button isn't on the toolbar (or can't be used now).</summary>
    public bool TryOpen()
    {
        if (Toggle.Flyout is not { } flyout || !Toggle.IsEffectivelyVisible || !Toggle.IsEffectivelyEnabled)
            return false;

        if (flyout.IsOpen)
            Bar?.FocusQuery();
        else
            flyout.ShowAt(Toggle);

        return true;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.CloseRequested -= OnCloseRequested;
            _viewModel.ReopenRequested -= OnReopenRequested;
        }

        _viewModel = DataContext as SearchViewModel;

        if (_viewModel is not null)
        {
            _viewModel.CloseRequested += OnCloseRequested;
            _viewModel.ReopenRequested += OnReopenRequested;
        }

        base.OnDataContextChanged(e);
    }

    /// <summary>Each opening looks in the active view's folder again.</summary>
    private void OnFlyoutOpening(object? sender, EventArgs e)
    {
        if (!_reopening)
            _viewModel?.Prepare();
    }

    private void OnCloseRequested(object? sender, EventArgs e) => Toggle.Flyout?.Hide();

    /// <summary>The system's folder chooser took the focus and so closed the bar: shows it again.</summary>
    private void OnReopenRequested(object? sender, EventArgs e)
        => Dispatcher.UIThread.Post(Reopen, DispatcherPriority.Background); // once the window has the focus back

    private void Reopen()
    {
        if (Toggle.Flyout is not { IsOpen: false } flyout || !Toggle.IsEffectivelyVisible)
            return;

        _reopening = true;
        try
        {
            flyout.ShowAt(Toggle);
        }
        finally
        {
            _reopening = false;
        }
    }
}
