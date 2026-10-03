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

    /// <summary>Alt+Left / Alt+Right / Alt+Up, and Backspace for "up".</summary>
    private void OnNavigationKeyDown(object? sender, KeyEventArgs e)
    {
        // Leave Backspace and arrows alone while the user types (e.g. a future search box)
        if (ViewModel is not { } vm || e.Source is TextBox)
            return;

        var command = (e.Key, e.KeyModifiers) switch
        {
            (Key.Left, KeyModifiers.Alt) => vm.GoBackCommand,
            (Key.Right, KeyModifiers.Alt) => vm.GoForwardCommand,
            (Key.Up, KeyModifiers.Alt) or (Key.Back, KeyModifiers.None) => vm.GoUpCommand,
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
