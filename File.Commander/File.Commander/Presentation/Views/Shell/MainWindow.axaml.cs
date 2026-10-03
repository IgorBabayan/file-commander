using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using File.Commander.Application.Path;

namespace File.Commander.Presentation.Views.Shell;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

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