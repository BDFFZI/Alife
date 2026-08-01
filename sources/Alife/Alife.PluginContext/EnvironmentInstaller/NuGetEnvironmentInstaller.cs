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
    public HashSet<string> ManagedDirectories { get; private set; } = new();
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
            resolver.GetDependencyLimit().Select(dep => {
                string spec = FormatVersionSpec(dep.Min, dep.Max);
                return $"    <PackageReference Include=\"{dep.Name}\" Version=\"{spec}\" />";
            }));

        string csproj = $"""
                         <Project Sdk="Microsoft.NET.Sdk">
                           <PropertyGroup>
                             <TargetFramework>net10.0</TargetFramework>
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

        HashSet<string> managedDirs = new();
        HashSet<string> nativeDirs = new();

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

                    int managedCountBefore = managedDirs.Count;

                    if (pkg.Value.TryGetProperty("compile", out var compile))
                    {
                        foreach (var dll in compile.EnumerateObject())
                        {
                            string dllPath = Path.Combine(pkgDir, dll.Name);
                            string? dir = Path.GetDirectoryName(dllPath);
                            if (dir != null && Directory.Exists(dir))
                                managedDirs.Add(dir);
                        }
                    }

                    if (managedDirs.Count == managedCountBefore)
                    {
                        string[] fallbackDirs = ["lib", "lib_manual"];
                        List<string> candidates = [];
                        foreach (string sub in fallbackDirs)
                        {
                            string subDir = Path.Combine(pkgDir, sub);
                            if (!Directory.Exists(subDir))
                                continue;
                            foreach (string dir in Directory.GetDirectories(subDir))
                            {
                                if (Directory.GetFiles(dir, "*.dll").Length > 0)
                                    candidates.Add(dir);
                            }
                        }
                        if (candidates.Count > 0)
                        {
                            candidates.Sort();
                            managedDirs.Add(candidates[^1]);
                        }
                    }

                    string nativeDir = Path.Combine(pkgDir, "runtimes", rid, "native");
                    if (Directory.Exists(nativeDir))
                        nativeDirs.Add(nativeDir);
                }
            }
        }

        if (managedDirs.SetEquals(ManagedDirectories) && nativeDirs.SetEquals(UnmanagedDirectories))
            return false;

        ManagedDirectories = managedDirs;
        UnmanagedDirectories = nativeDirs;
        return true;
    }
}
