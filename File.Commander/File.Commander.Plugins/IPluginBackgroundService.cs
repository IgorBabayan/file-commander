namespace File.Commander.Plugins;

/// <summary>
/// Long-running plugin work. Register implementations in <see cref="IPlugin.ConfigureServices"/>;
/// the host starts each one once, on a background thread, after the main window is shown.
/// </summary>
public interface IPluginBackgroundService
{
    /// <param name="cancellationToken">Cancelled when the app shuts down.</param>
    Task StartAsync(CancellationToken cancellationToken);
}