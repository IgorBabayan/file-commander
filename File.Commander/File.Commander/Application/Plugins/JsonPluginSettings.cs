using System.Text.Json;

namespace File.Commander.Application.Plugins;

sealed class JsonPluginSettings<T>(string filePath) : IPluginSettings<T> where T : class, new()
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly Lock _readLock = new();
    private T? _current;

    public event EventHandler<T>? Changed;

    public T Current
    {
        get
        {
            if (Volatile.Read(ref _current) is { } current)
                return current;

            lock (_readLock)
                return _current ??= Read();
        }
    }

    public async Task SaveAsync(T value, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(value);

        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            // Temp file + move: a crash mid-write can't leave a broken settings file
            var tempPath = filePath + ".tmp";
            await using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(stream, value, Options, cancellationToken);
            }

            IOFile.Move(tempPath, filePath, overwrite: true);
            Volatile.Write(ref _current, value);
        }
        finally
        {
            _writeGate.Release();
        }

        RaiseChanged(value);
    }

    private void RaiseChanged(T value)
    {
        if (Changed is not { } handlers)
            return;

        // A throwing subscriber must not turn a successful save into a failed one
        foreach (var handler in handlers.GetInvocationList().Cast<EventHandler<T>>())
        {
            try
            {
                handler(this, value);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Settings Changed handler for {typeof(T).Name} failed: {ex}");
            }
        }
    }

    private T Read()
    {
        try
        {
            if (!IOFile.Exists(filePath))
                return new T();

            using var stream = IOFile.OpenRead(filePath);
            return JsonSerializer.Deserialize<T>(stream, Options) ?? new T();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Unreadable settings fall back to defaults instead of failing the plugin
            Trace.WriteLine($"Can't read plugin settings '{filePath}': {ex.Message}");
            return new T();
        }
    }
}
