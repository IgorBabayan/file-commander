using Avalonia.Controls;

namespace File.Commander.Presentation.Views.Shell;

/// <summary>
/// Customize Toolbar…: the palette of items that aren't on the toolbar, Restore Defaults / Undo and Done.
/// Shown over the window's content while customizing; dragging is done by MainWindow (MainWindow.Toolbar.cs).
/// DataContext: <see cref="ToolbarViewModel"/>.
/// </summary>
public partial class ToolbarCustomizer : UserControl
{
    public ToolbarCustomizer() => InitializeComponent();

    /// <summary>Items dropped here leave the toolbar.</summary>
    public Control DropArea => PaletteArea;

    /// <summary>Holds the palette's items (Border.palette-item, DataContext: <see cref="ToolbarItemInfo"/>).</summary>
    public Control ItemsHost => PaletteList;
}
