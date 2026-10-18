using Alife.Foundation;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Alife.Framework;

public struct PluginManifest
{
    public string Version { get; set; }
    public Dictionary<string, string>? Dependencies { get; set; }
    public Dictionary<string, Dictionary<string, string>>? Environments { get; set; }

    /// <summary>
    /// 平台特定清单段（键为 "Android"/"Windows"/"Linux" 等平台名，与 AlifeContext.PlatformName 对应）。
    /// 反序列化时捕获所有未知顶级键，平台段结构见 <see cref="PluginManifestPlatform"/>。
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JToken>? PlatformSections { get; set; }

    /// <summary>
    /// 获取当前平台生效的环境依赖：根 Environments 与当前平台段的 Environments 合并（平台项覆盖根项）。
    /// </summary>
    public readonly Dictionary<string, Dictionary<string, string>> GetEffectiveEnvironments()
    {
        Dictionary<string, Dictionary<string, string>> result = new();
        if (Environments != null)
        {
            foreach (KeyValuePair<string, Dictionary<string, string>> environment in Environments)
                result[environment.Key] = environment.Value;
        }

        if (PlatformSections != null &&
            PlatformSections.TryGetValue(AlifeContext.PlatformName, out JToken? sectionToken))
        {
            PluginManifestPlatform? section = sectionToken.ToObject<PluginManifestPlatform>();
            if (section.HasValue && section.Value.Environments != null)
            {
                foreach (KeyValuePair<string, Dictionary<string, string>> environment in section.Value.Environments)
                    result[environment.Key] = environment.Value;
            }
        }

        return result;
    }
}

public struct PluginManifestPlatform
{
    public Dictionary<string, Dictionary<string, string>>? Environments { get; set; }
}
