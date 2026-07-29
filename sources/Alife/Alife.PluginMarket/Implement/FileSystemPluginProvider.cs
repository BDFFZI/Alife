using Newtonsoft.Json;

namespace Alife.PluginMarket;

public class FileSystemPluginProvider(string directoryPath) : IPluginProvider
{
    public Task<PluginPackage[]> GetPluginsAsync()
    {
        if (!Directory.Exists(directoryPath))
            return Task.FromResult(Array.Empty<PluginPackage>());

        string[] files = Directory.GetFiles(directoryPath, "*.json");
        List<PluginPackage> plugins = new();

        foreach (string file in files)
        {
            try
            {
                string json = File.ReadAllText(file);
                PluginPackage? plugin = JsonConvert.DeserializeObject<PluginPackage>(json);
                if (plugin != null)
                    plugins.Add(plugin);
            }
            catch
            {
                // 忽略解析失败的文件
            }
        }

        return Task.FromResult(plugins.ToArray());
    }
}
