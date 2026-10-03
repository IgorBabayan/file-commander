using System.Collections.Concurrent;
using File.Commander.Plugins;

namespace File.Commander.Application.Plugins;

sealed class PluginContext(string pluginDirectory, string dataDirectory)
    : IPluginContext
{
    // One store per settings type, so every consumer sees the same Current and Changed
    private readonly ConcurrentDictionary<Type, object> _settings = new();

    public string PluginDirectory { get; } = pluginDirectory;
    public string DataDirectory { get; } = dataDirectory;

    public IPluginSettings<T> GetSettings<T>() where T : class, new()
        => (IPluginSettings<T>)_settings.GetOrAdd(typeof(T),
            type => new JsonPluginSettings<T>(IOPath.Combine(DataDirectory, $"settings.{type.Name}.json")));
}