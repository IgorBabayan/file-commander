using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace File.Commander.Extensions;

static class Utils
{
    public static Window GetMainWindow()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime
            {
                MainWindow: not null
            } desktop)
        {
            return desktop.MainWindow;
        }

        throw new InvalidOperationException("MainWindow not found.");
    }

    /// <summary>The most recently opened visible window (an open dialog), or the main window.</summary>
    public static Window GetTopWindow()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var top = desktop.Windows.LastOrDefault(w => w.IsVisible);
            if (top is not null)
                return top;
        }

        return GetMainWindow();
    }
}