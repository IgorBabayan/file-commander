using Avalonia.Controls.ApplicationLifetimes;

namespace File.Commander.Application.Updates;

/// <summary>
/// Settings → Advanced → Updates → "Automatically install updates on startup": shortly after the window opens,
/// looks for a newer release; when there is one, downloads and installs it as two actions in the Action center,
/// then restarts File Commander into the new version.
/// </summary>
public sealed class AutoUpdater(IUpdateService updates, ISettingsService settings, ActionCenterViewModel actionCenter)
{
    // The window is up and the first folder listed before the network is used
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(3);

    // Long enough to read "Installed File Commander …" before the window goes
    private static readonly TimeSpan RestartDelay = TimeSpan.FromSeconds(2);

    private static readonly TimeSpan BusyPollInterval = TimeSpan.FromSeconds(1);

    /// <summary>Call on the UI thread once the main window is shown. Never throws.</summary>
    public async Task RunOnStartupAsync(CancellationToken cancellationToken)
    {
        if (!settings.Current.AutoUpdate)
            return;

        if (!updates.CanInstall)
        {
            Trace.WriteLine("Automatic updates are on, but File Commander isn't running from an AppImage: skipped.");
            return;
        }

        try
        {
            await Task.Delay(StartupDelay, cancellationToken);

            // Not in the Action center: a check that finds nothing (or no network) isn't worth a line there
            var release = await updates.FindUpdateAsync(cancellationToken);
            if (release is null)
                return;

            var details = $"File Commander {updates.CurrentVersionText} → {release.VersionText}";

            string? downloaded = null;
            var download = await actionCenter.RunAsync(OperationKind.Download,
                OperationTitles.DownloadUpdate(release.VersionText), details,
                async progress => downloaded = await updates.DownloadAsync(release, progress));

            if (!download.IsSucceeded || downloaded is not { } file)
                return;

            var install = await actionCenter.RunAsync(OperationKind.Install,
                OperationTitles.InstallUpdate(release.VersionText), details,
                progress => updates.Install(file, progress));

            if (!install.IsSucceeded)
                return;

            await RestartAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The app is closing
        }
        catch (Exception ex)
        {
            // Offline, rate-limited, GitHub down…: tried again on the next start
            Trace.WriteLine($"Automatic update failed: {ex}");
        }
    }

    private async Task RestartAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(RestartDelay, cancellationToken);

        // A copy started meanwhile finishes first: restarting would stop it halfway
        while (actionCenter.RunningCount > 0)
            await Task.Delay(BusyPollInterval, cancellationToken);

        // Started first: if it can't start, this instance stays open and the new version runs next time
        updates.StartNewInstance();

        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }
}
