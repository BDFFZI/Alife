using System.Collections.Generic;

namespace Alife.PluginContext;

public struct PluginManifest
{
    public string Version { get; set; }
    public Dictionary<string, string>? Dependencies { get; set; }
    public Dictionary<string, Dictionary<string, string>>? Environments { get; set; }
}
