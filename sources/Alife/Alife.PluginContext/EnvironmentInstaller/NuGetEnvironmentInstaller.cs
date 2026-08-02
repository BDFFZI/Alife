using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Alife.Foundation;

namespace Alife.PluginContext;

public class NuGetEnvironmentInstaller(string packagesResolverOutput) : IEnvironmentInstaller
{
    public event Func<Task>? PackagesUpdatedAsync;
    public HashSet<string> CompilingDlls { get; private set; } = new();
    public HashSet<string> RuntimeDlls { get; private set; } = new();
    public HashSet<string> UnmanagedDirectories { get; private set; } = new();

    public async Task InstallEnvironment(IEnumerable<KeyValuePair<string, string>> environment)
    {
        DependencyResolver resolver = new();
        resolver.AddDependencies(environment);
        ResolvePackages(resolver);
        if (GrabPackageList())
        {
            if (PackagesUpdatedAsync != null)
            {
                try
                {
                    await Task.WhenAll(PackagesUpdatedAsync.GetInvocationList()
                        .Cast<Func<Task>>()
                        .Select(func => func()));
                }
                catch (Exception e)
                {
                    AlifeLog.LogError(e);
                }
            }
        }
    }

    void ResolvePackages(DependencyResolver resolver)
    {
        Directory.CreateDirectory(packagesResolverOutput);

        string refs = string.Join("\n",
            resolver.GetDependencyLimit().Select(dep =>
            {
                string spec = FormatVersionSpec(dep.Min, dep.Max);
                return $"    <PackageReference Include=\"{dep.Name}\" Version=\"{spec}\" />";
            }));

        string csproj = $"""
                         <Project Sdk="Microsoft.NET.Sdk">
                           <PropertyGroup>
                             <TargetFramework>net10.0-windows10.0.19041.0</TargetFramework>
                             <UseWindowsForms>true</UseWindowsForms>
                           </PropertyGroup>
                           <ItemGroup>
                         {refs}
                           </ItemGroup>
                         </Project>
                         """;

        string projectFile = Path.Combine(packagesResolverOutput, "Project.csproj");
        File.WriteAllText(projectFile, csproj);
        AlifeUtility.Command("dotnet", $"restore \"{projectFile}\"");

        static string FormatVersionSpec(string min, string max)
        {
            bool hasMin = min != "0.0.0";
            bool hasMax = max != "99999.0.0";

            if (hasMin && hasMax && min == max)
                return min;
            if (hasMin && hasMax)
                return $"[{min},{max}]";
            if (hasMin)
                return $"[{min},)";
            if (hasMax)
                return $"(,{max}]";
            return "*";
        }
    }
    bool GrabPackageList()
    {
        string assetsFile = Path.Combine(packagesResolverOutput, "obj", "project.assets.json");
        string json = File.ReadAllText(assetsFile);
        using var doc = JsonDocument.Parse(json);

        string nugetCache = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".nuget", "packages");

        string rid = System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier;

        HashSet<string> compilingDlls = new();
        HashSet<string> runtimeDlls = new();
        HashSet<string> unmanagedDirectories = new();
        if (doc.RootElement.TryGetProperty("targets", out var targets))
        {
            string tfm = targets.EnumerateObject().First().Name;

            if (targets.TryGetProperty(tfm, out var tfmTarget))
            {
                foreach (var pkg in tfmTarget.EnumerateObject())
                {
                    string packageRoot = pkg.Name.Split('/')[0];
                    string packageVersion = pkg.Name.Split('/')[1];
                    string pkgDir = Path.Combine(nugetCache, packageRoot, packageVersion);

                    if (pkg.Value.TryGetProperty("compile", out var compile))
                    {
                        foreach (var dll in compile.EnumerateObject())
                        {
                            string dllPath = Path.Combine(pkgDir, dll.Name);
                            if (dllPath.EndsWith("_._"))
                            {
                                foreach (var file in Directory.GetFiles(Path.GetDirectoryName(dllPath)!, "*.dll"))
                                    compilingDlls.Add(file);
                            }
                            else
                            {
                                compilingDlls.Add(dllPath);
                            }
                        }
                    }

                    if (pkg.Value.TryGetProperty("runtime", out var runtime))
                    {
                        foreach (var dll in runtime.EnumerateObject())
                        {
                            string dllPath = Path.Combine(pkgDir, dll.Name);
                            if (dllPath.EndsWith("_._"))
                            {
                                foreach (var file in Directory.GetFiles(Path.GetDirectoryName(dllPath)!, "*.dll"))
                                    runtimeDlls.Add(file);
                            }
                            else
                            {
                                runtimeDlls.Add(dllPath);
                            }
                        }
                    }

                    string nativeDir = Path.Combine(pkgDir, "runtimes", rid, "native");
                    if (Directory.Exists(nativeDir))
                        unmanagedDirectories.Add(nativeDir);
                }
            }
        }

        if (compilingDlls.SetEquals(CompilingDlls) &&
            runtimeDlls.SetEquals(RuntimeDlls) &&
            unmanagedDirectories.SetEquals(UnmanagedDirectories))
            return false;

        CompilingDlls = compilingDlls;
        RuntimeDlls = runtimeDlls;
        UnmanagedDirectories = unmanagedDirectories;
        return true;
    }
}