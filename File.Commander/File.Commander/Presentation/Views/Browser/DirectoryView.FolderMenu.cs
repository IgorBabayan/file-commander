using Avalonia.Controls;
using Avalonia.Interactivity;

namespace File.Commander.Presentation.Views.Browser;

/// <summary>
/// A right click on the empty space of a folder view, or the menu key (Shift+F10) with nothing selected:
/// the window opens the menu of the folder itself.
/// </summary>
public sealed class FolderMenuRequestedEventArgs(DirectoryViewModel page, Control target, bool atPointer)
    : RoutedEventArgs(DirectoryView.FolderMenuRequestedEvent)
{
    /// <summary>The folder page the menu acts on: new items and pasted ones go into its folder.</summary>
    public DirectoryViewModel Page { get; } = page;

    /// <summary>The list, grid or tree the menu belongs to.</summary>
    public Control Target { get; } = target;

    /// <summary>Opened with the pointer: the menu opens there. Otherwise (keyboard) over <see cref="Target"/>.</summary>
    public bool AtPointer { get; } = atPointer;
}

/// <summary>
/// The context menu of a folder's empty space: New folder, New text document, Paste, Select all, Properties.
/// Like the menu of entries (DirectoryView.EntryMenu.cs), the view only asks for it; MainWindow.FolderMenu.cs builds it.
/// </summary>
public partial class DirectoryView
{
    /// <summary>Bubbles up to the window, which opens the menu.</summary>
    public static readonly RoutedEvent<FolderMenuRequestedEventArgs> FolderMenuRequestedEvent =
        RoutedEvent.Register<DirectoryView, FolderMenuRequestedEventArgs>("FolderMenuRequested", RoutingStrategies.Bubble);

    private void RequestFolderMenu(DirectoryViewModel vm, Control owner, bool atPointer)
        => RaiseEvent(new FolderMenuRequestedEventArgs(vm, owner, atPointer));
}
