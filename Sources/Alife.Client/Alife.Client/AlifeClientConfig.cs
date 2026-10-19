using Alife.Foundation;
using Newtonsoft.Json;

namespace Alife.Client;

public class AlifeClientConfig : AlifeApplicationConfig
{
    public int McpServerPort { get; set; } = 18765;
    public string McpServerAddress => $"http://127.0.0.1:{McpServerPort}";
    public string McpServerPath => "/mcp";

    public void Save()
    {
        AlifeConfig.SetString("AlifeClientConfig", JsonConvert.SerializeObject(this, Formatting.Indented));
    }
    public AlifeClientConfig()
    {
        string configString = AlifeConfig.GetString("AlifeClientConfig");
        if (!string.IsNullOrEmpty(configString))
            JsonConvert.PopulateObject(configString, this);
    }
}