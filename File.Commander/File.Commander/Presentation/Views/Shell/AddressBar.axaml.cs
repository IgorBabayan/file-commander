using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace File.Commander.Presentation.Views.Shell;

public partial class AddressBar : UserControl
{
    private AddressBarViewModel? _viewModel;

    public AddressBar()
    {
        InitializeComponent();

        Bar.PointerPressed += OnBarPointerPressed;
        // handledEventsToo: TextBox may consume Enter/Escape itself
        Input.AddHandler(KeyDownEvent, OnInputKeyDown, RoutingStrategies.Bubble, handledEventsToo: true);
        Input.LostFocus += OnInputLostFocus;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        if (_viewModel is not null)
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;

        _viewModel = DataContext as AddressBarViewModel;

        if (_viewModel is not null)
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        base.OnDataContextChanged(e);
    }

    /// <summary>A click on the empty part of the bar (not a breadcrumb) switches to typing a path.</summary>
    private void OnBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_viewModel is null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        if (!_viewModel.IsEditing)
            _viewModel.BeginEdit();

        // The bar sits in the title bar: don't let this click start a window drag
        e.Handled = true;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(AddressBarViewModel.IsEditing) || _viewModel is not { IsEditing: true })
            return;

        // The TextBox becomes visible in this same pass; focus it once it can take focus
        Dispatcher.UIThread.Post(() =>
        {
            Input.Focus();
            Input.SelectAll();
        });
    }

    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                _viewModel?.CommitEdit();
                e.Handled = true;
                break;
            case Key.Escape:
                _viewModel?.CancelEdit();
                e.Handled = true;
                break;
        }
    }

    private void OnInputLostFocus(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is { IsEditing: true })
            _viewModel.CancelEdit();
    }
}
