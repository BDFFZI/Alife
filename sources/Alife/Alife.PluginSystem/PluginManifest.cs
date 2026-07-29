using System.Collections.Generic;

namespace Alife.PluginSystem;

public struct PluginManifest
{
    public string Version { get; set; }
    public Dictionary<string, string>? Dependencies { get; set; }
    public Dictionary<string, Dictionary<string, string>>? Environments { get; set; }
}
