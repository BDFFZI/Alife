using Alife.Framework;
using NuGet.Frameworks;

namespace Alife.Client.Windows;

class WindowsNuGetEnvironmentInstaller(CSharpCompiler csharpCompiler) : NuGetEnvironmentInstaller(csharpCompiler)
{
    protected override NuGetFramework GetTargetFramework() => NuGetFramework.Parse("net10.0-windows10.0.19041.0");
}
