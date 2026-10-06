using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace File.Commander.Presentation.Views.Dialogs;

/// <summary>
/// The login a server asks for. Enter connects, Esc or a click outside cancels; the first empty field gets the focus.
/// DataContext: <see cref="NetworkLoginViewModel"/>.
/// </summary>
public partial class NetworkLoginWindow : Window
{
    private NetworkLoginViewModel? _subscribed;

    public NetworkLoginWindow()
    {
        InitializeComponent();

        // Tunnel: Enter in a text box connects instead of reaching the box
        AddHandler(KeyDownEvent, OnDialogKeyDown, RoutingStrategies.Tunnel);
    }

    private NetworkLoginViewModel? ViewModel => DataContext as NetworkLoginViewModel;

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

        if (ViewModel is { HasUser: true } vm && string.IsNullOrEmpty(vm.User))
            UserBox.Focus();
        else
            PasswordBox.Focus();
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
        // A focused button handles its own Enter: Cancel must not connect
        else if (e.Key == Key.Enter && e.Source is not Button)
        {
            vm.AcceptCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnCloseRequested(bool result) => Close(result);

    private void OnBackdropPressed(object? sender, PointerPressedEventArgs e) => Close(false);
}
