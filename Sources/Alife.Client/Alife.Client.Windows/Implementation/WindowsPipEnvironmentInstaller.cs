using Alife.Foundation;
using Alife.Framework;

namespace Alife.Client.Windows;

public class WindowsPipEnvironmentInstaller : PipEnvironmentInstaller
{
    protected override async Task InstallRequirements(IEnumerable<KeyValuePair<string, string>> items)
    {
        string packageListOutput = Path.Combine(AlifePath.RuntimeFolderPath, "PluginEnvironment", "PipPackages.txt");
        await File.WriteAllLinesAsync(packageListOutput, items.Select(dep => $"{dep.Key}{dep.Value}"));
        AlifeUtility.Command("python", $"-m pip install -r \"{packageListOutput}\"");
    }
}