using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using File.Commander.Presentation.ViewModels.Browser;

namespace File.Commander.Presentation.Views.Browser;

public partial class DirectoryView : UserControl
{
    public DirectoryView()
    {
        InitializeComponent();

        FileList.DoubleTapped += OnEntryDoubleTapped;
        // handledEventsToo: ListBox may consume Enter itself
        FileList.AddHandler(KeyDownEvent, OnListKeyDown, RoutingStrategies.Bubble, handledEventsToo: true);
    }

    private void OnEntryDoubleTapped(object? sender, TappedEventArgs e)
    {
        // A double-click on empty space has the page as DataContext, not an entry: ignore it
        if (DataContext is DirectoryViewModel vm && (e.Source as StyledElement)?.DataContext is FileEntryViewModel entry)
            vm.OpenCommand.Execute(entry);
    }

    private void OnListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || e.KeyModifiers != KeyModifiers.None)
            return;

        if (DataContext is DirectoryViewModel { SelectedEntry: { } entry } vm)
        {
            vm.OpenCommand.Execute(entry);
            e.Handled = true;
        }
    }
}
