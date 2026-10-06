using System.Reflection;
using System.Runtime.Loader;

namespace File.Commander.PluginCatalog;

sealed class PluginLoadContext(string mainAssemblyPath)
    : AssemblyLoadContext(isCollectible: false)
{
    // Shared with the host (resolved from the Default context): a plugin that loaded its own copy of the SDK
    // would see another IPlugin type, and PluginLoader would find no plugin in it. Matched by exact name, as a
    // plugin's own assembly may well be called "File.Commander.Plugins.Something".
    private static readonly HashSet<string> SharedNames = new(StringComparer.Ordinal) { "File.Commander.Plugins" };

    private static readonly string[] SharedPrefixes =
    [
        "Avalonia", "CommunityToolkit.Mvvm", "Material.Icons", "Microsoft.Extensions.", "System.",
    ];

    private readonly AssemblyDependencyResolver _resolver = new(mainAssemblyPath);

    protected override Assembly? Load(AssemblyName name)
    {
        if (name.Name is not { } assemblyName
            || SharedNames.Contains(assemblyName)
            || SharedPrefixes.Any(p => assemblyName.StartsWith(p, StringComparison.Ordinal)))
            return null;

        var path = _resolver.ResolveAssemblyToPath(name);
        return path is null ? null : LoadFromAssemblyPath(path);
    }

    protected override IntPtr LoadUnmanagedDll(string name)
    {
        var path = _resolver.ResolveUnmanagedDllToPath(name);
        return path is null ? IntPtr.Zero : LoadUnmanagedDllFromPath(path);
    }
}
