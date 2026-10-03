using System.Text.Json;

namespace File.Commander.PluginCatalog;

public enum PluginRemovalResult
{
    Removed,
    PendingRestart
}

public interface IPluginUninstaller
{
    /// <summary>Drops the addon's tables, then deletes its data and folder (or schedules it for the next start).</summary>
    Task<PluginRemovalResult> RemoveAsync(InstalledPlugin plugin, CancellationToken cancellationToken = default);

    /// <summary>Finishes removals scheduled in a previous session. Call after the host migration.</summary>
    void CompletePendingRemovals();
}

internal sealed class PluginUninstaller(
    PluginCatalog catalog) : IPluginUninstaller
{
    private const string MANIFEST_FILE = "addon.json";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private sealed record AddonManifest(string? Id, List<string>? Tables);

    public async Task<PluginRemovalResult> RemoveAsync(InstalledPlugin plugin, CancellationToken cancellationToken = default)
    {
        if (plugin.IsRemovalPending)
            return PluginRemovalResult.PendingRestart;

        // Its assembly is in the process: the DLL is locked (Windows) and its background
        // service may still be using its tables. Finish on the next start, before it loads.
        if (plugin.Status != PluginLoadStatus.Disabled)
        {
            SchedulePending(plugin);
            return PluginRemovalResult.PendingRestart;
        }

        var removed = await Task.Run(() => Remove(plugin.Descriptor.Directory), cancellationToken);

        if (!removed)
        {
            SchedulePending(plugin);
            return PluginRemovalResult.PendingRestart;
        }

        catalog.Forget(plugin);
        return PluginRemovalResult.Removed;
    }

    public void CompletePendingRemovals()
    {
        if (!Directory.Exists(catalog.Root))
            return;

        var pending = Directory.GetDirectories(catalog.Root)
            .Where(d => IOFile.Exists(IOPath.Combine(d, PluginLoader.REMOVAL_MARKER)))
            .ToList();

        if (pending.Count == 0)
            return;

        foreach (var directory in pending)
        {
            try
            {
                Remove(directory);
            }
            catch (Exception ex)
            {
                // Marker stays, so it is retried on the next start
                Trace.WriteLine($"Pending removal of '{directory}' failed: {ex}");
            }
        }
    }

    /// <summary>
    /// Tables first, then files: the manifest naming the tables lives in the folder being deleted.
    /// Throws if the tables can't be dropped (nothing is deleted then).
    /// Returns false if the folder is locked.
    /// </summary>
    private bool Remove(string directory)
    {
        var manifest = ReadManifest(directory);
        if (manifest is not null)
        {
            DeleteDataDirectory(manifest.Id);
        }

        try
        {
            Directory.Delete(directory, recursive: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.WriteLine($"Can't delete addon folder '{directory}' now: {ex.Message}");
            return false;
        }
    }

    private void DeleteDataDirectory(string? id)
    {
        if (string.IsNullOrWhiteSpace(id) || id is "." or ".."
            || id.IndexOfAny(IOPath.GetInvalidFileNameChars()) >= 0)
            return;

        var dataDir = IOPath.Combine(catalog.Root, "_data", id);
        if (Directory.Exists(dataDir))
            Directory.Delete(dataDir, recursive: true);
    }

    private static AddonManifest? ReadManifest(string directory)
    {
        var path = IOPath.Combine(directory, MANIFEST_FILE);
        if (!IOFile.Exists(path))
        {
            Trace.WriteLine($"No {MANIFEST_FILE} in '{directory}': its tables are left in the database.");
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<AddonManifest>(IOFile.ReadAllText(path), JsonOptions);
        }
        catch (JsonException ex)
        {
            Trace.WriteLine($"Invalid {MANIFEST_FILE} in '{directory}': {ex.Message}");
            return null;
        }
    }

    private static void SchedulePending(InstalledPlugin plugin)
    {
        IOFile.WriteAllText(IOPath.Combine(plugin.Descriptor.Directory, PluginLoader.REMOVAL_MARKER), string.Empty);
        plugin.IsRemovalPending = true;
    }
}