using System.Reflection;
using System.Runtime.Loader;

namespace File.Commander.PluginCatalog;

sealed class PluginLoadContext(string mainAssemblyPath)
    : AssemblyLoadContext(isCollectible: false)
{
    // Shared with the host (resolved from the Default context). EF Core and SQLite are shared
    // so plugins use the host's provider and native e_sqlite3 instead of loading a second copy.
    private static readonly string[] SharedPrefixes =
    [
        "ED.Assistant.Plugins.Abstractions", "Avalonia", "CommunityToolkit.Mvvm",
        "Material.Icons", "Microsoft.Extensions.", "System.",
        "Microsoft.EntityFrameworkCore", "Microsoft.Data.Sqlite", "SQLitePCLRaw"
    ];

    private readonly AssemblyDependencyResolver _resolver = new(mainAssemblyPath);

    protected override Assembly? Load(AssemblyName name)
    {
        if (SharedPrefixes.Any(p => name.Name!.StartsWith(p, StringComparison.Ordinal)))
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
