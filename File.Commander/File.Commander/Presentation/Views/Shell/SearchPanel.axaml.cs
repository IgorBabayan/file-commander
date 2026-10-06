using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace File.Commander.Presentation.Views.Shell;

/// <summary>
/// The search bar and its options. Enter in a text box searches, Esc closes the bar or the dialog around it.
/// DataContext: <see cref="SearchViewModel"/>.
/// </summary>
public partial class SearchPanel : UserControl
{
    public SearchPanel()
    {
        InitializeComponent();

        // Tunnel: Enter must not reach the text box, Esc must not reach whatever would close only a part of the panel
        AddHandler(KeyDownEvent, OnPanelKeyDown, RoutingStrategies.Tunnel);
    }

    private SearchViewModel? ViewModel => DataContext as SearchViewModel;

    /// <summary>Puts the caret in the bar with its text selected, so typing replaces the last search.</summary>
    public void FocusQuery()
    {
        QueryBox.Focus();
        QueryBox.SelectAll();
    }

    private void OnPanelKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.None || ViewModel is not { } vm)
            return;

        // An open drop-down or calendar closes first, on its own
        if (e.Source is ComboBox { IsDropDownOpen: true } or CalendarDatePicker { IsDropDownOpen: true })
            return;

        if (e.Key == Key.Escape)
        {
            vm.CloseCommand.Execute(null);
            e.Handled = true;
        }
        // Only in the two text boxes: a date being typed is committed by the calendar's own Enter
        else if (e.Key == Key.Enter && (ReferenceEquals(e.Source, QueryBox) || ReferenceEquals(e.Source, ExtensionsBox)))
        {
            if (vm.SearchCommand.CanExecute(null))
                vm.SearchCommand.Execute(null);

            e.Handled = true;
        }
    }

    private void OnClearClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } vm)
            vm.Text = string.Empty;

        QueryBox.Focus();
    }
}
