using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Alife.Platform;

namespace Alife.PluginSystem;

public class PipEnvironmentInstaller(string packageListOutput) : IEnvironmentInstaller
{
    static bool setuptoolsReady;

    public async Task InstallEnvironment(IEnumerable<KeyValuePair<string, string>> environment)
    {
        if (!setuptoolsReady)
        {
            AlifeUtility.Command("python", "-m pip install setuptools wheel --quiet");
            setuptoolsReady = true;
        }

        await File.WriteAllLinesAsync(
            packageListOutput,
            environment.Select(dep => $"{dep.Key}{dep.Value}")
        );
        AlifeUtility.Command("python", $"-m pip install -r \"{packageListOutput}\"");
    }
}
