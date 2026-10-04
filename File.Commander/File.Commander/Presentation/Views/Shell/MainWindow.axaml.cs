using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using File.Commander.Application.Path;

namespace File.Commander.Presentation.Views.Shell;

public partial class MainWindow : Window
{
    private MainViewModel? _subscribed;

    public MainWindow()
    {
        InitializeComponent();

        // Tunnel: works wherever the pointer or focus is, before a child control can swallow the event
        AddHandler(PointerPressedEvent, OnNavigationPointerPressed, RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, OnNavigationKeyDown, RoutingStrategies.Tunnel);

        // Tab into a view: it becomes the active one, like a click
        AddHandler(GotFocusEvent, (_, e) => ActivatePaneOf(e.Source));
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_subscribed is not null)
            _subscribed.PropertyChanged -= OnViewModelPropertyChanged;

        _subscribed = ViewModel;

        if (_subscribed is not null)
            _subscribed.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // After the old tab is hidden and the new one shown
        if (e.PropertyName is nameof(MainViewModel.ActiveTab))
            Dispatcher.UIThread.Post(FocusActivePane, DispatcherPriority.Loaded);
    }

    /// <summary>
    /// Another tab was selected: keyboard focus would stay in the now hidden one, so the arrow keys
    /// would move its selection. Moves focus to the file list of the selected tab's active view.
    /// </summary>
    private void FocusActivePane()
    {
        if (ViewModel is not { } vm)
            return;

        var paneView = this.GetVisualDescendants()
            .OfType<PaneView>()
            .FirstOrDefault(view => ReferenceEquals(view.DataContext, vm.ActivePane) && view.IsEffectivelyVisible);

        var candidates = paneView?.GetVisualDescendants()
            .OfType<InputElement>()
            .Where(element => element.Focusable && element.IsEffectivelyVisible && element.IsEffectivelyEnabled)
            .ToList() ?? [];

        // The file list; on a page without one (Computer…) its first control, e.g. a card
        var target = candidates.FirstOrDefault(element => element is ListBox or TreeView)
                     ?? candidates.FirstOrDefault();

        // Nothing to focus in the view: at least take focus out of the hidden tab
        if (target is null || !target.Focus())
            Focus();
    }

    /// <summary>A click on a tab in the tab bar selects it, a middle click closes it.</summary>
    private void OnTabPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is not Control { DataContext: TabViewModel tab })
            return;

        var command = e.InitialPressMouseButton switch
        {
            MouseButton.Left => tab.SelectCommand,
            MouseButton.Middle => tab.CloseCommand,
            _ => null,
        };

        if (TryExecute(command))
            e.Handled = true;
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs args)
    {
        if (!args.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        if (args.ClickCount == 2 && !DesktopEnvironmentHelper.IsHyprland())
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;

            return;
        }

        BeginMoveDrag(args);
    }

    /// <summary>Any click into a view makes it the active one. Mouse side buttons: back / forward.</summary>
    private void OnNavigationPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (ViewModel is not { } vm)
            return;

        // First, so the view itself and the side buttons below act on the view that was clicked
        ActivatePaneOf(e.Source);

        var properties = e.GetCurrentPoint(this).Properties;
        var command = properties.IsXButton1Pressed ? vm.GoBackCommand
            : properties.IsXButton2Pressed ? vm.GoForwardCommand
            : null;

        if (TryExecute(command))
            e.Handled = true;
    }

    /// <summary>Split view: makes the view that contains <paramref name="source"/> the active one (selected tab only).</summary>
    private void ActivatePaneOf(object? source)
    {
        if (ViewModel is { } vm
            && source is Avalonia.Visual visual
            && visual.FindAncestorOfType<PaneView>(includeSelf: true)?.DataContext is PaneViewModel pane)
            vm.ActivatePane(pane);
    }

    /// <summary>
    /// Shortcuts of Settings → Basic → Keymap (Ctrl+L, F5, Ctrl+1/2/3, Alt+Left… by default).
    /// While the user types (address bar, a future search box) Backspace, arrows and plain keys stay with the text box.
    /// </summary>
    private void OnNavigationKeyDown(object? sender, KeyEventArgs e)
    {
        if (ViewModel is { } vm && vm.HandleKey(e.Key, e.KeyModifiers, isTyping: e.Source is TextBox))
            e.Handled = true;
    }

    private static bool TryExecute(IRelayCommand? command)
    {
        if (command is null || !command.CanExecute(null))
            return false;

        command.Execute(null);
        return true;
    }

    private void OnMinimizeClick(object? sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void OnMaximizeClick(object? sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.TryShutdown();
        }
    }
}
