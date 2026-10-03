using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.Input;
using File.Commander.Application.Path;

namespace File.Commander.Presentation.Views.Shell;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Tunnel: works wherever the pointer or focus is, before a child control can swallow the event
        AddHandler(PointerPressedEvent, OnNavigationPointerPressed, RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, OnNavigationKeyDown, RoutingStrategies.Tunnel);
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

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

    /// <summary>Mouse side buttons: back / forward.</summary>
    private void OnNavigationPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (ViewModel is not { } vm)
            return;

        var properties = e.GetCurrentPoint(this).Properties;
        var command = properties.IsXButton1Pressed ? vm.GoBackCommand
            : properties.IsXButton2Pressed ? vm.GoForwardCommand
            : null;

        if (TryExecute(command))
            e.Handled = true;
    }

    /// <summary>
    /// Ctrl+L / Alt+D: type a path, F5: refresh, Ctrl+1 / Ctrl+2 / Ctrl+3: grid / list / tree,
    /// Ctrl+H: show/hide hidden files, Alt+Left / Alt+Right / Alt+Up and Backspace: history and "up".
    /// </summary>
    private void OnNavigationKeyDown(object? sender, KeyEventArgs e)
    {
        if (ViewModel is not { } vm)
            return;

        var global = (e.Key, e.KeyModifiers) switch
        {
            (Key.L, KeyModifiers.Control) or (Key.D, KeyModifiers.Alt) => vm.AddressBar.BeginEditCommand,
            (Key.F5, KeyModifiers.None) => vm.RefreshCommand,
            (Key.D1 or Key.NumPad1, KeyModifiers.Control) => vm.ShowGridViewCommand,
            (Key.D2 or Key.NumPad2, KeyModifiers.Control) => vm.ShowListViewCommand,
            (Key.D3 or Key.NumPad3, KeyModifiers.Control) => vm.ShowTreeViewCommand,
            _ => null,
        };

        if (TryExecute(global))
        {
            e.Handled = true;
            return;
        }

        // Leave Backspace and arrows alone while the user types (address bar, a future search box)
        if (e.Source is TextBox)
            return;

        var command = (e.Key, e.KeyModifiers) switch
        {
            (Key.Left, KeyModifiers.Alt) => vm.GoBackCommand,
            (Key.Right, KeyModifiers.Alt) => vm.GoForwardCommand,
            (Key.Up, KeyModifiers.Alt) or (Key.Back, KeyModifiers.None) => vm.GoUpCommand,
            (Key.H, KeyModifiers.Control) => vm.ToggleHiddenFilesCommand,
            _ => null,
        };

        if (TryExecute(command))
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
