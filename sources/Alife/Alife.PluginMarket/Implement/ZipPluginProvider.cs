using Alife.Platform;
using Newtonsoft.Json;

namespace Alife.PluginMarket;

public class ZipPluginProvider(string zipUrl) : IPluginProvider
{
    readonly string repoDir = Path.Combine(AlifePath.TempFolderPath, "PluginRepo");

    public async Task<PluginPackage[]> GetPluginsAsync()
    {
        await FetchRepositoryAsync();
        return LoadPlugins();
    }

    async Task FetchRepositoryAsync()
    {
        if (Directory.Exists(repoDir))
            Directory.Delete(repoDir, true);

        await AlifeUtility.DownloadZipFileAsync(repoDir, zipUrl);
    }

    PluginPackage[] LoadPlugins()
    {
        if (!Directory.Exists(repoDir))
            return [];

        List<PluginPackage> plugins = new();
        foreach (string file in Directory.GetFiles(repoDir, "*.json", SearchOption.AllDirectories))
        {
            try
            {
                string json = File.ReadAllText(file);
                PluginPackage? plugin = JsonConvert.DeserializeObject<PluginPackage>(json);
                if (plugin != null && !string.IsNullOrEmpty(plugin.Id))
                    plugins.Add(plugin);
            }
            catch
            {
                // 忽略解析失败的文件
            }
        }

        return plugins.ToArray();
    }
}
