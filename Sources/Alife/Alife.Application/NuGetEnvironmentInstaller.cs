using System.Runtime.Loader;
using System.Text.Json;
using Alife.Foundation;
using NuGet.Commands;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Frameworks;
using NuGet.LibraryModel;
using NuGet.Packaging.Signing;
using NuGet.ProjectModel;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

namespace Alife.Framework;

public abstract class NuGetEnvironmentInstaller : IEnvironmentInstaller
{
    public string EnvironmentName => "nuget";
    public async Task InstallEnvironment(IEnumerable<KeyValuePair<string, string>> environment)
    {
        DependencyResolver resolver = new();
        resolver.AddDependencies(environment);
        await ResolvePackages(resolver);
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
    /// 为不同的操作系统设置不同的目标框架
    /// </summary>
    protected abstract NuGetFramework GetTargetFramework();

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

    async Task ResolvePackages(DependencyResolver resolver)
    {
        Directory.CreateDirectory(packagesResolverOutput);
        string objDirectory = Path.Combine(packagesResolverOutput, "obj");
        Directory.CreateDirectory(objDirectory);

        NuGetFramework framework = GetTargetFramework();
        List<LibraryDependency> dependencies = resolver.GetDependencyLimit()
            .Select(dep => {
                string spec = FormatVersionSpec(dep.Min, dep.Max);
                return new LibraryDependency(new LibraryRange(dep.Name, VersionRange.Parse(spec), LibraryDependencyTarget.Package));
            })
            .ToList();

        TargetFrameworkInformation target = new()
        {
            FrameworkName = framework,
            AssetTargetFallback = true,
            Dependencies = [.. dependencies]
        };

        string projectFile = Path.Combine(packagesResolverOutput, "Project.csproj");
        PackageSpec spec = new([target])
        {
            FilePath = projectFile,
            Name = "Project",
            Version = new NuGetVersion(1, 0, 0),
            RestoreMetadata = new ProjectRestoreMetadata
            {
                ProjectStyle = ProjectStyle.PackageReference,
                ProjectPath = projectFile,
                ProjectName = "Project",
                ProjectUniqueName = projectFile,
                OutputPath = objDirectory,
                PackagesPath = GetGlobalPackagesFolder(),
                Sources = GetPackageSources(),
                TargetFrameworks = [new ProjectRestoreMetadataFrameworkInfo(framework)],
                OriginalTargetFrameworks = [framework.GetShortFolderName()],
                ConfigFilePaths = [],
                FallbackFolders = [],
                UsingMicrosoftNETSdk = true,
                RestoreAuditProperties = new RestoreAuditProperties()
            }
        };

        ISettings settings = Settings.LoadDefaultSettings(packagesResolverOutput);
        SourceCacheContext cache = new();
        ILogger logger = NullLogger.Instance;
        RestoreCommandProviders providers = new RestoreCommandProvidersCache().GetOrCreate(
            spec.RestoreMetadata.PackagesPath,
            [],
            spec.RestoreMetadata.Sources.Select(source => Repository.Factory.GetCoreV3(source)).ToList(),
            cache,
            logger,
            updateLastAccess: false);

        string lockFilePath = Path.Combine(objDirectory, "project.assets.json");
        RestoreRequest request = new(
            spec,
            providers,
            cache,
            ClientPolicyContext.GetClientPolicy(settings, logger),
            new PackageSourceMapping(new Dictionary<string, IReadOnlyList<string>>()),
            logger,
            new LockFileBuilderCache())
        {
            LockFilePath = lockFilePath,
            MSBuildProjectExtensionsPath = objDirectory,
            RestoreOutputPath = objDirectory,
            AllowNoOp = false,
            RestoreForceEvaluate = true,
            ProjectStyle = ProjectStyle.PackageReference,
            ValidateRuntimeAssets = true
        };

        RestoreResult result = await new RestoreCommand(request).ExecuteAsync(CancellationToken.None);
        if (!result.Success || result.LockFile is null)
        {
            string messages = string.Join("\n", result.LogMessages
                .Where(message => message.Level >= LogLevel.Warning)
                .Select(message => $"{message.Level} {message.Code}"));
            if (result.LockFile is null)
                throw new InvalidOperationException($"NuGet restore failed.\n{messages}");
        }

        new LockFileFormat().Write(lockFilePath, result.LockFile!);
    }

    static string GetGlobalPackagesFolder()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string folder = Path.Combine(home, ".nuget", "packages");
        Directory.CreateDirectory(folder);
        return folder;
    }

    static List<PackageSource> GetPackageSources()
    {
        ISettings settings = Settings.LoadDefaultSettings(null);
        List<PackageSource> sources = new PackageSourceProvider(settings)
            .LoadPackageSources()
            .Where(source => source.IsEnabled)
            .ToList();
        if (sources.Count == 0)
            sources.Add(new PackageSource("https://api.nuget.org/v3/index.json"));
        return sources;
    }

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

    bool GrabPackageList()
    {
        string assetsFile = Path.Combine(packagesResolverOutput, "obj", "project.assets.json");
        if (!File.Exists(assetsFile)) return false;
        string json = File.ReadAllText(assetsFile);
        using var doc = JsonDocument.Parse(json);

        string nugetCache = GetGlobalPackagesFolder();

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
