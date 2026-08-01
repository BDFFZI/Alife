using Newtonsoft.Json;

namespace Alife.PluginMarket;
public class PluginRelease
{
    [JsonProperty("date")] 
    public DateTime Date { get; set; } = DateTime.MinValue;

    [JsonProperty("note")]
    public string? Note { get; set; }

    [JsonProperty("file")]
    public string File { get; set; } = string.Empty;

    [JsonProperty("dependencies")]
    public Dictionary<string, string>? Dependencies { get; set; }

    [JsonProperty("environments")]
    public Dictionary<string, Dictionary<string, string>>? Environments { get; set; }
}