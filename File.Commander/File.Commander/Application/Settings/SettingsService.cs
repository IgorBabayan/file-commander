using System.Text.Json;

namespace File.Commander.Application.Settings;

public interface ISettingsService
{
    /// <summary>Last saved settings, or defaults when nothing is saved yet (or the file is unreadable).</summary>
    AppSettings Current { get; }

    /// <summary>Raised after a successful save, on the saving thread (the UI thread for everything in the app).</summary>
    event EventHandler<AppSettings>? Changed;

    /// <summary>Full overwrite. Saves are serialized, the last call wins.</summary>
    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);
}

sealed class SettingsService(string filePath) : ISettingsService
{
    public static string DefaultPath { get; } = IOPath.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "file-commander", "settings.json");

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private AppSettings _current = Load(filePath);

    public event EventHandler<AppSettings>? Changed;

    public AppSettings Current => Volatile.Read(ref _current);

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(IOPath.GetDirectoryName(filePath)!);

            // Temp file + move: a crash mid-write can't leave a broken settings file
            var tempPath = filePath + ".tmp";
            await using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(stream, settings, Options, cancellationToken);
            }

            IOFile.Move(tempPath, filePath, overwrite: true);
            Volatile.Write(ref _current, settings);
        }
        finally
        {
            _writeGate.Release();
        }

        RaiseChanged(settings);
    }

    /// <summary>Never throws: an unreadable file gives defaults. Also used at startup, before DI is built.</summary>
    public static AppSettings Load(string path)
    {
        try
        {
            if (!IOFile.Exists(path))
                return new AppSettings();

            using var stream = IOFile.OpenRead(path);
            return Normalize(JsonSerializer.Deserialize<AppSettings>(stream, Options));
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            Trace.WriteLine($"Can't read settings '{path}': {ex.Message}");
            return new AppSettings();
        }
    }

    // A hand-edited file with "Basic": null must not crash the app
    private static AppSettings Normalize(AppSettings? settings) => settings is null
        ? new AppSettings()
        : settings with
        {
            Addons = settings.Addons ?? new(),
            Basic = settings.Basic ?? new(),
            Sidebar = settings.Sidebar ?? new(),
            Workspace = settings.Workspace ?? new(),
            Advanced = settings.Advanced ?? new(),
            Keymap = settings.Keymap ?? new(),
            Columns = settings.Columns ?? new(),
        };

    private void RaiseChanged(AppSettings settings)
    {
        if (Changed is not { } handlers)
            return;

        // A throwing subscriber must not turn a successful save into a failed one
        foreach (var handler in handlers.GetInvocationList().Cast<EventHandler<AppSettings>>())
        {
            try
            {
                handler(this, settings);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Settings Changed handler failed: {ex}");
            }
        }
    }
}
