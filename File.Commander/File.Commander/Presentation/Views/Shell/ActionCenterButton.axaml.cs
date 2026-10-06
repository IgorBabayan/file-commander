using Avalonia.Controls;

namespace File.Commander.Presentation.Views.Shell;

/// <summary>
/// The Action center button in the title bar, between split view and Search, with its panel.
/// DataContext: <see cref="ActionCenterViewModel"/>.
/// </summary>
public partial class ActionCenterButton : UserControl
{
    private ActionCenterViewModel? _viewModel;

    public ActionCenterButton()
    {
        InitializeComponent();

        // What finishes while the panel is open has been seen: no dot on the button afterwards
        if (Toggle.Flyout is { } flyout)
        {
            flyout.Opened += (_, _) => SetOpen(true);
            flyout.Closed += (_, _) => SetOpen(false);
        }
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        if (_viewModel is not null)
            _viewModel.OperationFailed -= OnOperationFailed;

        _viewModel = DataContext as ActionCenterViewModel;

        if (_viewModel is not null)
            _viewModel.OperationFailed += OnOperationFailed;

        base.OnDataContextChanged(e);
    }

    private void SetOpen(bool isOpen)
    {
        if (_viewModel is not null)
            _viewModel.IsOpen = isOpen;
    }

    /// <summary>A failed action opens the panel, so what couldn't be done doesn't go unnoticed.</summary>
    private void OnOperationFailed(object? sender, OperationViewModel operation)
    {
        if (Toggle.Flyout is not { IsOpen: false } flyout || !Toggle.IsEffectivelyVisible)
            return;

        // Only over the window the user is looking at
        if (TopLevel.GetTopLevel(this) is Window { IsActive: true })
        {
            operation.IsExpanded = operation.HasFailures;
            flyout.ShowAt(Toggle);
        }
    }
}
