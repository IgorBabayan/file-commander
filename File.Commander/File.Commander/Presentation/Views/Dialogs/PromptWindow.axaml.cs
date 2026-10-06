using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace File.Commander.Presentation.Views.Dialogs;

/// <summary>Enter presses the main button, Esc or a click outside cancels. DataContext: <see cref="PromptViewModel"/>.</summary>
public partial class PromptWindow : Window
{
    private PromptViewModel? _subscribed;

    public PromptWindow()
    {
        InitializeComponent();

        // Tunnel: Enter in the text box confirms instead of reaching the box
        AddHandler(KeyDownEvent, OnDialogKeyDown, RoutingStrategies.Tunnel);
    }

    private PromptViewModel? ViewModel => DataContext as PromptViewModel;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_subscribed is not null)
            _subscribed.CloseRequested -= OnCloseRequested;

        _subscribed = ViewModel;

        if (_subscribed is not null)
            _subscribed.CloseRequested += OnCloseRequested;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        if (ViewModel is { HasInput: true })
        {
            InputBox.Focus();
            InputBox.SelectAll();
        }
        else
        {
            AcceptButton.Focus();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_subscribed is not null)
            _subscribed.CloseRequested -= OnCloseRequested;

        _subscribed = null;
        base.OnClosed(e);
    }

    private void OnDialogKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.None || ViewModel is not { } vm)
            return;

        if (e.Key == Key.Escape)
        {
            vm.CancelCommand.Execute(null);
            e.Handled = true;
        }
        // A focused button handles its own Enter: Cancel must not confirm
        else if (e.Key == Key.Enter && e.Source is not Button)
        {
            vm.AcceptCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnCloseRequested(bool result) => Close(result);

    private void OnBackdropPressed(object? sender, PointerPressedEventArgs e) => Close(false);
}
