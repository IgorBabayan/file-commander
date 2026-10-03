using Avalonia.Controls;
using Avalonia.Interactivity;

namespace File.Commander.Presentation.Views.Shell;

/// <summary>Sort button in the title bar. DataContext: <see cref="MainViewModel"/>.</summary>
public partial class SortButton : UserControl
{
    public SortButton() => InitializeComponent();

    /// <summary>Picking an order closes the menu, like a regular menu item.</summary>
    private void OnOptionClick(object? sender, RoutedEventArgs e) => Toggle.Flyout?.Hide();
}
