using Alife.Framework;

namespace Alife.Client.Windows;

class WindowsNuGetEnvironmentInstaller(CSharpCompiler csharpCompiler) : NuGetEnvironmentInstaller(csharpCompiler)
{
    protected override string GetNuGetPropertyGroup()
    {
        return """
               <TargetFramework>net10.0-windows10.0.19041.0</TargetFramework>
               <UseWindowsForms>true</UseWindowsForms>
               """;
    }
    protected override void RestoreNuGetPackages(string projectFile)
    {
        WindowsCommand.Run("dotnet", $"restore \"{projectFile}\"");
    }
}