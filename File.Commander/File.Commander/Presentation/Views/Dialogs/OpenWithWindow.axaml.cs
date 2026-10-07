using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace File.Commander.Presentation.Views.Dialogs;

/// <summary>
/// The search box has the focus, as in GNOME Files: typing filters the apps, Up and Down move through them, Enter opens
/// the file in the one selected. A double click opens it too; Esc or a click outside cancels.
/// DataContext: <see cref="OpenWithViewModel"/>.
/// </summary>
public partial class OpenWithWindow : Window
{
    private OpenWithViewModel? _subscribed;

    public OpenWithWindow()
    {
        InitializeComponent();

        // Tunnel: the keys reach the dialog before the search box or the list handle them
        AddHandler(KeyDownEvent, OnDialogKeyDown, RoutingStrategies.Tunnel);
        AppList.DoubleTapped += OnAppDoubleTapped;
    }

    private OpenWithViewModel? ViewModel => DataContext as OpenWithViewModel;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        Subscribe(ViewModel);
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        SearchBox.Focus();
    }

    protected override void OnClosed(EventArgs e)
    {
        Subscribe(null);
        base.OnClosed(e);
    }

    private void Subscribe(OpenWithViewModel? viewModel)
    {
        if (_subscribed is not null)
            _subscribed.CloseRequested -= OnCloseRequested;

        _subscribed = viewModel;

        if (_subscribed is not null)
            _subscribed.CloseRequested += OnCloseRequested;
    }

    private void OnDialogKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.None || ViewModel is not { } vm)
            return;

        switch (e.Key)
        {
            case Key.Escape:
                vm.CancelCommand.Execute(null);
                e.Handled = true;
                break;

            // A focused button or checkbox handles its own Enter: Cancel must not open
            case Key.Enter when e.Source is not (Button or CheckBox):
                if (vm.OpenCommand.CanExecute(null))
                    vm.OpenCommand.Execute(null);
                e.Handled = true;
                break;

            // While typing in the search box, the arrows move through the apps
            case Key.Down or Key.Up when SearchBox.IsFocused:
                MoveSelection(vm, e.Key == Key.Down ? 1 : -1);
                e.Handled = true;
                break;
        }
    }

    private void MoveSelection(OpenWithViewModel vm, int step)
    {
        if (vm.Rows.Count == 0)
            return;

        var index = vm.SelectedRow is { } row ? vm.Rows.IndexOf(row) : -1;
        var next = Math.Clamp(index + step, 0, vm.Rows.Count - 1);
        vm.SelectedRow = vm.Rows[next];
        AppList.ScrollIntoView(next);
    }

    private void OnAppDoubleTapped(object? sender, TappedEventArgs e)
    {
        // On a row, not on the empty space below the last one
        if (e.Source is Control { DataContext: OpenWithRow } && ViewModel is { } vm && vm.OpenCommand.CanExecute(null))
            vm.OpenCommand.Execute(null);
    }

    private void OnCloseRequested(bool result) => Close(result);

    private void OnBackdropPressed(object? sender, PointerPressedEventArgs e) => Close(false);
}
