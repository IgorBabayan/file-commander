using Avalonia.Controls;

namespace File.Commander.Presentation.Views.Shell;

/// <summary>
/// One view of the content area. MainWindow makes it the active one on a click or focus inside it.
/// DataContext: <see cref="PaneViewModel"/>.
/// </summary>
public partial class PaneView : UserControl
{
    public PaneView() => InitializeComponent();
}
