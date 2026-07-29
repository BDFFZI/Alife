using Newtonsoft.Json;

namespace Alife.PluginMarket;

public class PluginPackage
{
    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;

    [JsonProperty("author")]
    public string Author { get; set; } = string.Empty;

    [JsonProperty("description")]
    public string Description { get; set; } = string.Empty;

    [JsonProperty("tags")]
    public List<string>? Tags { get; set; }

    [JsonProperty("source")]
    public string Source { get; set; } = string.Empty;
    
    [JsonProperty("dependencies")]
    public Dictionary<string, string>? Dependencies { get; set; }

    [JsonProperty("environments")]
    public Dictionary<string, Dictionary<string, string>>? Environments { get; set; }
    
    [JsonProperty("releases")]
    public Dictionary<string, PluginRelease>? Releases { get; set; }

    public Dictionary<string, string>? GetDependencies(string version)
    {
        return Releases?.GetValueOrDefault(version)?.Dependencies ?? Dependencies;
    }

    public Dictionary<string, Dictionary<string, string>>? GetEnvironments(string version)
    {
        return Releases?.GetValueOrDefault(version)?.Environments ?? Environments;
    }
}
