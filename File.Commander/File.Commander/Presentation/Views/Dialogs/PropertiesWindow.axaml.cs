using Avalonia.Controls;
using Avalonia.Input;
using File.Commander.Presentation.ViewModels.Dialogs;

namespace File.Commander.Presentation.Views.Dialogs;

/// <summary>Esc, Enter, Close or a click outside closes it. DataContext: <see cref="PropertiesViewModel"/>.</summary>
public partial class PropertiesWindow : Window
{
    private PropertiesViewModel? _subscribed;

    public PropertiesWindow() => InitializeComponent();

    private PropertiesViewModel? ViewModel => DataContext as PropertiesViewModel;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_subscribed is not null)
            _subscribed.CloseRequested -= Close;

        _subscribed = ViewModel;

        if (_subscribed is not null)
            _subscribed.CloseRequested += Close;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        CloseButton.Focus();
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_subscribed is not null)
            _subscribed.CloseRequested -= Close;

        _subscribed = null;
        base.OnClosed(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (!e.Handled && e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None)
        {
            Close();
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    private void OnBackdropPressed(object? sender, PointerPressedEventArgs e) => Close();
}
