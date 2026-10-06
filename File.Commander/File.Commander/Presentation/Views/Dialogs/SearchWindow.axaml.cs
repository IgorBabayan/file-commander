using Avalonia.Controls;
using Avalonia.Input;

namespace File.Commander.Presentation.Views.Dialogs;

/// <summary>
/// The search bar as a dialog: Ctrl+F while the Search button isn't on the toolbar. Closes when a search starts,
/// on Esc or on a click outside. DataContext: <see cref="SearchViewModel"/>.
/// </summary>
public partial class SearchWindow : Window
{
    private SearchViewModel? _subscribed;

    public SearchWindow() => InitializeComponent();

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_subscribed is not null)
            _subscribed.CloseRequested -= OnCloseRequested;

        _subscribed = DataContext as SearchViewModel;

        if (_subscribed is not null)
            _subscribed.CloseRequested += OnCloseRequested;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        Bar.FocusQuery();
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_subscribed is not null)
            _subscribed.CloseRequested -= OnCloseRequested;

        _subscribed = null;
        base.OnClosed(e);
    }

    private void OnCloseRequested(object? sender, EventArgs e) => Close(true);

    private void OnBackdropPressed(object? sender, PointerPressedEventArgs e) => Close(false);
}
