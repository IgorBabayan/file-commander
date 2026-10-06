using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using File.Commander.Presentation.ViewModels.Shell;

namespace File.Commander.Presentation.Views.Shell;

/// <summary>
/// New, Select all, Copy and Paste in the title bar, between the navigation buttons and Computer.
/// DataContext: <see cref="MainViewModel"/>.
/// </summary>
public partial class FileActionsBar : UserControl
{
    public FileActionsBar() => InitializeComponent();

    /// <summary>
    /// A row of the New or Paste menu was picked: the menu closes. Posted, so the row's command runs first
    /// while the menu still holds its DataContext.
    /// </summary>
    private void OnMenuRowClick(object? sender, RoutedEventArgs e)
        => Dispatcher.UIThread.Post(() =>
        {
            NewButton.Flyout?.Hide();
            PasteButton.Flyout?.Hide();
        });
}
