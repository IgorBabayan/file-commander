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
        // Tunnel: runs before the TextBox's own handling of Up/Down/Tab/Enter
        Input.AddHandler(KeyDownEvent, OnInputKeyDown, RoutingStrategies.Tunnel);
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
        if (_viewModel is not { } vm)
            return;

        switch (e.Key)
        {
            case Key.Down when vm.IsSuggestionsOpen:
            case Key.Up when vm.IsSuggestionsOpen:
                var index = vm.MoveSelection(e.Key == Key.Down ? 1 : -1);
                if (index >= 0)
                    SuggestionList.ContainerFromIndex(index)?.BringIntoView();
                e.Handled = true;
                break;

            case Key.Tab:
                if (vm.CompleteSuggestion())
                    MoveCaretToEnd();
                // Handled either way: Tab moving focus away would cancel the edit
                e.Handled = true;
                break;

            case Key.Enter:
                if (!vm.OpenSelectedSuggestion())
                    vm.CommitEdit();
                e.Handled = true;
                break;

            case Key.Escape:
                // First Escape closes the suggestions, the second one leaves edit mode
                if (vm.IsSuggestionsOpen)
                    vm.CloseSuggestions();
                else
                    vm.CancelEdit();
                e.Handled = true;
                break;
        }
    }

    private void MoveCaretToEnd()
    {
        // After the binding has pushed the completed text into the box
        Dispatcher.UIThread.Post(() =>
        {
            Input.ClearSelection();
            Input.CaretIndex = Input.Text?.Length ?? 0;
        });
    }

    private void OnInputLostFocus(object? sender, RoutedEventArgs e)
    {
        // A click on a suggestion: its command finishes the edit itself
        if (SuggestionList.IsPointerOver)
            return;

        if (_viewModel is { IsEditing: true })
            _viewModel.CancelEdit();
    }
}
