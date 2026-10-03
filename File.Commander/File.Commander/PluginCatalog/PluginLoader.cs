using File.Commander.Plugins;

namespace File.Commander.PluginCatalog;

public sealed record LoadedPlugin(IPlugin Plugin, string Directory);

/// <summary>
/// A plugin found on disk, described without loading its assembly, so disabled
/// plugins can still be listed in Settings.
/// </summary>
/// <param name="Key">Folder name (= assembly name). Used to enable/disable the plugin.</param>
public sealed record PluginDescriptor(string Key, string Directory, string AssemblyPath,
    string Name, string Version, string? Description);

public static class PluginLoader
{
    public const string REMOVAL_MARKER = ".remove-pending";
    
    public static IReadOnlyList<PluginDescriptor> Discover(string root)
    {
        if (!Directory.Exists(root))
            return [];

        var result = new List<PluginDescriptor>();
        foreach (var dir in Directory.GetDirectories(root).Order(StringComparer.OrdinalIgnoreCase))
        {
            if (IOFile.Exists(IOPath.Combine(dir, REMOVAL_MARKER)))
                continue;

            var key = IOPath.GetFileName(dir);
            var dll = IOPath.Combine(dir, key + ".dll");
            if (IOFile.Exists(dll))
                result.Add(Describe(key, dir, dll));
        }

        return result;
    }

    /// <summary>Loads the assembly and creates its plugins. Throws if it can't be loaded.</summary>
    public static IReadOnlyList<LoadedPlugin> Load(PluginDescriptor descriptor)
    {
        var asm = new PluginLoadContext(descriptor.AssemblyPath).LoadFromAssemblyPath(descriptor.AssemblyPath);

        return asm.GetExportedTypes()
            .Where(t => typeof(IPlugin).IsAssignableFrom(t) && t is { IsAbstract: false, IsInterface: false })
            .Select(t => new LoadedPlugin((IPlugin)Activator.CreateInstance(t)!, descriptor.Directory))
            .ToList();
    }

    private static PluginDescriptor Describe(string key, string dir, string dll)
    {
        var name = key;
        var version = "—";
        string? description = null;

        try
        {
            // Reads AssemblyTitle / InformationalVersion / AssemblyDescription from metadata
            // without loading the assembly (works on Linux and macOS too)
            var info = FileVersionInfo.GetVersionInfo(dll);

            if (!string.IsNullOrWhiteSpace(info.FileDescription))
                name = info.FileDescription;

            var raw = info.ProductVersion ?? info.FileVersion;
            if (!string.IsNullOrWhiteSpace(raw))
                version = raw.Split('+')[0]; // drop the "+commit" suffix

            if (!string.IsNullOrWhiteSpace(info.Comments))
                description = info.Comments;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Can't read plugin metadata from '{dll}': {ex.Message}");
        }

        return new PluginDescriptor(key, dir, dll, name, version, description);
    }
}
