using Alife.Framework;
using NuGet.Frameworks;

namespace Alife.Client.Android;

sealed class AndroidNuGetEnvironmentInstaller(CSharpCompiler csharpCompiler) : NuGetEnvironmentInstaller(csharpCompiler)
{
    protected override NuGetFramework GetTargetFramework() => NuGetFramework.Parse("net10.0");
}
