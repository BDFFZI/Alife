using Alife.Foundation;
using Alife.Framework;

namespace Alife.Client.Windows;

class WindowsPipEnvironmentInstaller : PipEnvironmentInstaller
{
    protected override async Task InstallRequirements(IEnumerable<KeyValuePair<string, string>> items)
    {
        string packageListOutput = Path.Combine(AlifePath.RuntimeFolderPath, "PluginEnvironment", "PipPackages.txt");
        await File.WriteAllLinesAsync(packageListOutput, items.Select(dep => $"{dep.Key}{dep.Value}"));
        WindowsCommand.Run("python", $"-m pip install -r \"{packageListOutput}\"");
    }
}