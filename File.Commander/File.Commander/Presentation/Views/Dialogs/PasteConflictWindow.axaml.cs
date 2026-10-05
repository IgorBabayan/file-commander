using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using File.Commander.Presentation.ViewModels.Dialogs;

namespace File.Commander.Presentation.Views.Dialogs;

/// <summary>
/// Enter continues, Esc or a click outside cancels the paste. Picking Rename puts the cursor in the name box, with
/// the name (without its extension) selected. DataContext: <see cref="PasteConflictViewModel"/>.
/// </summary>
public partial class PasteConflictWindow : Window
{
    private PasteConflictViewModel? _subscribed;

    public PasteConflictWindow()
    {
        InitializeComponent();

        // Tunnel: Enter in the name box continues instead of reaching the box
        AddHandler(KeyDownEvent, OnDialogKeyDown, RoutingStrategies.Tunnel);
    }

    private PasteConflictViewModel? ViewModel => DataContext as PasteConflictViewModel;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        Subscribe(ViewModel);
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        if (ViewModel is { IsRename: true })
            FocusNameBox();
        else
            AcceptButton.Focus();
    }

    protected override void OnClosed(EventArgs e)
    {
        Subscribe(null);
        base.OnClosed(e);
    }

    private void Subscribe(PasteConflictViewModel? viewModel)
    {
        if (_subscribed is not null)
        {
            _subscribed.CloseRequested -= OnCloseRequested;
            _subscribed.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _subscribed = viewModel;

        if (_subscribed is not null)
        {
            _subscribed.CloseRequested += OnCloseRequested;
            _subscribed.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // After the binding enabled the box
        if (e.PropertyName == nameof(PasteConflictViewModel.Choice) && ViewModel is { IsRename: true })
            Dispatcher.UIThread.Post(FocusNameBox, DispatcherPriority.Loaded);
    }

    /// <summary>"report (2)" of "report (2).pdf" selected, so typing changes the name and keeps the extension.</summary>
    private void FocusNameBox()
    {
        NameBox.Focus();

        var text = NameBox.Text ?? string.Empty;
        var extension = ViewModel is { IsFolder: true } ? string.Empty : IOPath.GetExtension(text);
        var stem = extension.Length <= 1 || extension.Length == text.Length ? text.Length : text.Length - extension.Length;

        NameBox.SelectionStart = 0;
        NameBox.SelectionEnd = stem;
    }

    private void OnDialogKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.None || ViewModel is not { } vm)
            return;

        if (e.Key == Key.Escape)
        {
            vm.CancelCommand.Execute(null);
            e.Handled = true;
        }
        // A focused button or choice handles its own Enter: Cancel must not continue
        else if (e.Key == Key.Enter && e.Source is not (Button or RadioButton or CheckBox))
        {
            if (vm.AcceptCommand.CanExecute(null))
                vm.AcceptCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnCloseRequested(bool result) => Close(result);

    private void OnBackdropPressed(object? sender, PointerPressedEventArgs e) => Close(false);
}
