using Alife.Foundation;
using Alife.Framework;

namespace Alife.Client.Windows;

public class WindowsPluginRuntime : IPluginRuntime
{
    public string GetNuGetPropertyGroup()
    {
        return """
               <TargetFramework>net10.0-windows10.0.19041.0</TargetFramework>
               <UseWindowsForms>true</UseWindowsForms>
               """;
    }
    public void RestoreNuGetPackages(string projectFile)
    {
        AlifeUtility.Command("dotnet", $"restore \"{projectFile}\"");
    }
}