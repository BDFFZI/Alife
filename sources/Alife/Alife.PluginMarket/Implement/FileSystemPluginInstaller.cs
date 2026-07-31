using Alife.Platform;

namespace Alife.PluginMarket;

public class FileSystemPluginInstaller(string directory) : IPluginInstaller
{
    public async Task InstallPlugin(PluginPackage pluginPackage, string version)
    {
        if (pluginPackage.Releases == null)
            throw new Exception($"Plugin {pluginPackage.Id} is not released");

        PluginRelease? pluginRelease = pluginPackage.Releases.GetValueOrDefault(version);
        if (pluginRelease == null)
            throw new Exception($"Plugin {pluginPackage.Id} version {version} not released");

        string pluginDirectory = Path.Combine(directory, pluginPackage.Id);

        if (!string.IsNullOrWhiteSpace(pluginRelease.File))
        {
            string extension = Path.GetExtension(pluginRelease.File);
            switch (extension)
            {
                case ".zip":
                    await UninstallPlugin(pluginPackage.Id);
                    await AlifeUtility.DownloadZipFileAsync(pluginRelease.File, pluginDirectory);
                    break;
                default:
                    throw new Exception($"Plugin {pluginPackage.Id} file type is not supported");
            }
        }
        else
        {
            Directory.CreateDirectory(pluginDirectory);
        }
    }
    public Task UninstallPlugin(string pluginId)
    {
        string pluginPath = Path.Combine(directory, pluginId);
        if (Directory.Exists(pluginPath))
            Directory.Delete(pluginPath, true);
        return Task.CompletedTask;
    }
}
