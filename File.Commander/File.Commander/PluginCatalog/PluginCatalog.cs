namespace File.Commander.PluginCatalog;

public enum PluginLoadStatus
{
    Disabled,
    Loaded,
    Failed
}

public sealed class InstalledPlugin(PluginDescriptor descriptor)
{
    public PluginDescriptor Descriptor { get; } = descriptor;

    /// <summary>What happened at this app start.</summary>
    public PluginLoadStatus Status { get; internal set; } = PluginLoadStatus.Disabled;

    public string? Error { get; internal set; }

    /// <summary>Removal was requested but has to finish on the next start.</summary>
    public bool IsRemovalPending { get; internal set; }
}

public interface IPluginCatalog
{
    /// <summary>Folder plugins are installed into.</summary>
    string Root { get; }

    IReadOnlyList<InstalledPlugin> Installed { get; }
}

internal sealed class PluginCatalog : IPluginCatalog
{
    private readonly List<InstalledPlugin> _installed;

    public string Root { get; }
    public IReadOnlyList<InstalledPlugin> Installed => _installed;

    private PluginCatalog(string root)
    {
        Root = root;
        _installed = PluginLoader.Discover(root).Select(d => new InstalledPlugin(d)).ToList();
    }

    /// <summary>Called after an addon has been removed from disk.</summary>
    internal void Forget(InstalledPlugin plugin) => _installed.Remove(plugin);

    public static PluginCatalog Create(string root) => new(root);

    /// <summary>
    /// Loads every enabled plugin. Disabled plugins are never loaded into the process.
    /// Never throws: a broken plugin is marked <see cref="PluginLoadStatus.Failed"/>.
    /// </summary>
    public IReadOnlyList<LoadedPlugin> LoadEnabled(Func<string, bool> isEnabled)
    {
        var result = new List<LoadedPlugin>();

        foreach (var plugin in Installed)
        {
            if (!isEnabled(plugin.Descriptor.Key))
            {
                plugin.Status = PluginLoadStatus.Disabled;
                continue;
            }

            try
            {
                var loaded = PluginLoader.Load(plugin.Descriptor);
                if (loaded.Count == 0)
                {
                    plugin.Status = PluginLoadStatus.Failed;
                    plugin.Error = "No IPlugin implementation found.";
                    continue;
                }

                result.AddRange(loaded);
                plugin.Status = PluginLoadStatus.Loaded;
            }
            catch (Exception ex)
            {
                plugin.Status = PluginLoadStatus.Failed;
                plugin.Error = ex.Message;
                Trace.WriteLine($"Failed to load plugin from '{plugin.Descriptor.Directory}': {ex}");
            }
        }

        return result;
    }

    /// <summary>For failures after loading, e.g. in ConfigureServices.</summary>
    public void MarkFailed(string directory, Exception ex)
    {
        var plugin = Installed.FirstOrDefault(p =>
            string.Equals(p.Descriptor.Directory, directory, StringComparison.Ordinal));

        if (plugin is null)
            return;

        plugin.Status = PluginLoadStatus.Failed;
        plugin.Error = ex.Message;
    }
}