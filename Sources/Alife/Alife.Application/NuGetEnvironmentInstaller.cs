using System.Runtime.Loader;
using System.Text.Json;
using Alife.Foundation;

namespace Alife.Framework;

public abstract class NuGetEnvironmentInstaller : IEnvironmentInstaller
{
    public string EnvironmentName => "nuget";
    public async Task InstallEnvironment(IEnumerable<KeyValuePair<string, string>> environment)
    {
        DependencyResolver resolver = new();
        resolver.AddDependencies(environment);
        ResolvePackages(resolver);
        await LoadResolvedEnvironment();
    }
    public async Task LoadResolvedEnvironment()
    {
        if (GrabPackageList())
        {
            HashSet<string> defaultAssemblies = AssemblyLoadContext.Default.Assemblies
                .Select(assembly => assembly.GetName().Name)
                .Where(name => !string.IsNullOrEmpty(name))
                .Cast<string>().ToHashSet();

            //重新设置编译环境
            string[] validCompilingDlls = this.compilingDlls
                .Where(dll => AlifeUtility.IsValidDll(dll, out var name) && !defaultAssemblies.Contains(name))
                .ToArray();
            csharpCompiler.SetBasicDlls(CSharpCompiler.GetAppCompilingAssemblies(), validCompilingDlls);

            //重载程序集运行环境
            if (PluginLoadContext.RootPluginContext != null)
                await PluginLoadContext.RootPluginContext.DisposeAsync();
            PluginLoadContext rootPluginContext =
                new("NuGetPackages", [.. unmanagedDirectories, AppContext.BaseDirectory]);
            IEnumerable<string> runtimeDllFiles = runtimeDlls
                .Where(file => AlifeUtility.IsValidDll(file, out string name) && !defaultAssemblies.Contains(name));
            foreach (string file in runtimeDllFiles)
                rootPluginContext.LoadDll(file);
            PluginLoadContext.RootPluginContext = rootPluginContext;
        }
    }

    /// <summary>
    /// 允许为不同的操作系统设置不同的架构目标来获得平台原生的开发体验
    /// </summary>
    /// <returns></returns>
    protected abstract string GetNuGetPropertyGroup();
    /// <summary>
    /// 不同的平台可调用的指令形式不同
    /// </summary>
    /// <param name="projectFile"></param>
    protected abstract void RestoreNuGetPackages(string projectFile);

    readonly CSharpCompiler csharpCompiler;
    readonly string packagesResolverOutput = Path.Combine(AlifePath.RuntimeFolderPath, "PluginEnvironment", "NuGetPackagesResolver");
    HashSet<string> compilingDlls = new();
    HashSet<string> runtimeDlls = new();
    HashSet<string> unmanagedDirectories = new();

    protected NuGetEnvironmentInstaller(CSharpCompiler csharpCompiler)
    {
        this.csharpCompiler = csharpCompiler;
        Directory.CreateDirectory(packagesResolverOutput);
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
                             {GetNuGetPropertyGroup()}
                           </PropertyGroup>
                           <ItemGroup>
                         {refs}
                           </ItemGroup>
                         </Project>
                         """;

        string projectFile = Path.Combine(packagesResolverOutput, "Project.csproj");
        File.WriteAllText(projectFile, csproj);
        RestoreNuGetPackages(projectFile);

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
        if (!File.Exists(assetsFile)) return false;
        string json = File.ReadAllText(assetsFile);
        using var doc = JsonDocument.Parse(json);

        string nugetCache = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".nuget", "packages");

        string rid = System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier;

        HashSet<string> newCompilingDlls = new();
        HashSet<string> newRuntimeDlls = new();
        HashSet<string> newUnmanagedDirectories = new();
        if (doc.RootElement.TryGetProperty("targets", out var targets))
        {
            string tfm = targets.EnumerateObject().First().Name;

            if (targets.TryGetProperty(tfm, out var tfmTarget))
            {
                foreach (var pkg in tfmTarget.EnumerateObject())
                {
                    string packageRoot = pkg.Name.Split('/')[0].ToLowerInvariant();
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
                                    newCompilingDlls.Add(file);
                            }
                            else
                            {
                                newCompilingDlls.Add(dllPath);
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
                                    newRuntimeDlls.Add(file);
                            }
                            else
                            {
                                newRuntimeDlls.Add(dllPath);
                            }
                        }
                    }

                    string nativeDir = Path.Combine(pkgDir, "runtimes", rid, "native");
                    if (Directory.Exists(nativeDir))
                        newUnmanagedDirectories.Add(nativeDir);
                }
            }
        }

        if (newCompilingDlls.SetEquals(compilingDlls) &&
            newRuntimeDlls.SetEquals(runtimeDlls) &&
            newUnmanagedDirectories.SetEquals(unmanagedDirectories))
            return false;

        compilingDlls = newCompilingDlls;
        runtimeDlls = newRuntimeDlls;
        unmanagedDirectories = newUnmanagedDirectories;
        return true;
    }
}