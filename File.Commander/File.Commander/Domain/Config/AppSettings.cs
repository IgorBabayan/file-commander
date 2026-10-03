using System.Text.Json.Serialization;

namespace File.Commander.Domain.Config;

public class AppSettings
{
    [JsonPropertyName(nameof(LogFolder))]
    public string? LogFolder { get; set; }

    [JsonPropertyName(nameof(Addons))]
    public Dictionary<string, bool> Addons { get; set; } = new();
	
    [JsonPropertyName(nameof(AutoUpdate))]
    public bool AutoUpdate { get; set; }

    public bool IsAddonEnabled(string key) => !Addons.TryGetValue(key, out var enabled) || enabled;
}
