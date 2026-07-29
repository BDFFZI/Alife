using Alife.Framework;
using Alife.Platform;
using Alife.PluginMarket;

namespace Alife.Components.Services;

public class PluginMarketConfig
{
   
}

public class PluginMarketService
{
    public event Action? OnInstalled;

    public string SourceUrl
    {
        get => GetConfig().SourceUrl;
        set
        {
            var config = GetConfig();
            config.SourceUrl = value;
            SaveConfig(config);

            var onlineProvider = new ZipPluginProvider(value);
            pluginMarket = new Alife.PluginMarket.PluginMarket
            (onlineProvider, localInstaller, localInstaller,
                new Dictionary<string, IEnvironmentInstaller> {
                    { "nuget", nugetInstaller },
                    { "pip", pipInstaller }
                });

            pluginMarket.FetchLocalPlugins();
            LoadModuleNugetEnvironment();
        }
    }

    public bool IsInstalled(string pluginId)
    {
        return GetInstalledPlugins().ContainsKey(pluginId);
    }
    public bool HasUpdate(PluginPackage pluginPackage)
    {
        string? installedVersion = GetInstalledVersion(pluginPackage.Id);
        if (installedVersion == null || pluginPackage.Releases == null)
            return false;

        string? latestVersion = pluginPackage.Releases.Keys
            .Where(IsClientCompatible)
            .OrderByDescending(v => v, Comparer<string>.Create(VersionResolver.CompareVersions))
            .FirstOrDefault();

        return latestVersion != null && latestVersion != installedVersion;
    }
    public bool IsClientCompatible(string pluginVersion)
    {
        string clientVersion = updateService.GetCurrentVersion();
        return VersionResolver.GetMajorVersion(pluginVersion) <= VersionResolver.GetMajorVersion(clientVersion);
    }

    public PluginPackage[] GetAllPlugins()
    {
        return pluginMarket.GetAllPluginPackages().ToArray();
    }
    public Dictionary<string, string> GetInstalledPlugins()
    {
        return pluginMarket.GetInstalledPlugins();
    }

    public string? GetInstalledVersion(string pluginId)
    {
        return GetInstalledPlugins().GetValueOrDefault(pluginId);
    }
    public string? GetLatestVersion(PluginPackage pluginPackage)
    {
        return pluginPackage.Releases?.Keys
            .Where(IsClientCompatible)
            .OrderByDescending(v => v, Comparer<string>.Create(VersionResolver.CompareVersions))
            .FirstOrDefault();
    }

    public async Task FetchOnlinePluginsAsync()
    {
        await pluginMarket.SyncOnlinePluginPackagesAsync();
        pluginMarket.FetchLocalPlugins();
    }
    public void RefreshLocalPlugins()
    {
        pluginMarket.FetchLocalPlugins();
    }

    public List<string> GetDependents(string pluginId)
    {
        return pluginMarket.GetPluginDependents(pluginId);
    }
    public async Task InstallPlugin(PluginPackage pluginPackage, string version)
    {
        await installLock.WaitAsync();
        try
        {
            await Task.Run(async () => {
                await pluginMarket.InstallPlugin(pluginPackage, version);
                LoadModuleNugetEnvironment();
                try
                {
                    moduleSystem.ReloadModules();
                }
                catch
                {
                    await pluginMarket.UninstallPlugin(pluginPackage);
                    throw;
                }
            });
        }
        finally
        {
            installLock.Release();
        }
        OnInstalled?.Invoke();
    }
    public async Task InstallPlugins(IEnumerable<(PluginPackage pluginPackage, string version)> plugins)
    {
        await installLock.WaitAsync();
        try
        {
            await Task.Run(async () => {
                await pluginMarket.InstallPlugins(plugins);
                LoadModuleNugetEnvironment();
                moduleSystem.ReloadModules();
            });
        }
        finally
        {
            installLock.Release();
        }
        OnInstalled?.Invoke();
    }
    public async Task UninstallPlugin(PluginPackage pluginPackage)
    {
        await installLock.WaitAsync();
        try
        {
            await Task.Run(async () => {
                await pluginMarket.UninstallPlugin(pluginPackage);
                LoadModuleNugetEnvironment();
                moduleSystem.ReloadModules();
            });
        }
        finally
        {
            installLock.Release();
        }
        OnInstalled?.Invoke();
    }

    public List<PluginPackage> GetForceUpgradedPlugins()
    {
        return GetAllPlugins()
            .Where(NeedForceUpgrade)
            .ToList();
    }

    readonly StorageSystem storageSystem;
    readonly ModuleSystem moduleSystem;
    readonly UpdateService updateService;
    readonly SemaphoreSlim installLock = new(1, 1);

    readonly FileSystemPluginInstaller localInstaller;
    readonly NuGetEnvironmentInstaller nugetInstaller;
    readonly PipEnvironmentInstaller pipInstaller;
    Alife.PluginMarket.PluginMarket pluginMarket;

    const string ConfigKey = "Settings/PluginMarketConfig";
    readonly PluginMarketConfig defaultConfig = new();

    public PluginMarketService(ModuleSystem moduleSystem, StorageSystem storageSystem, UpdateService updateService)
    {
        this.moduleSystem = moduleSystem;
        this.storageSystem = storageSystem;
        this.updateService = updateService;

        //创建基础插件市场功能
        localInstaller = new FileSystemPluginInstaller(Path.Combine(AlifePath.StorageFolderPath, "Plugins"));
        nugetInstaller = new NuGetEnvironmentInstaller(Path.Combine(AlifePath.RuntimeFolderPath, "NugetPackages.txt"), Path.Combine(AlifePath.RuntimeFolderPath, "NugetRestoreProject"));
        pipInstaller = new PipEnvironmentInstaller(Path.Combine(AlifePath.RuntimeFolderPath, "PipPackages.txt"));
        pluginMarket = new Alife.PluginMarket.PluginMarket(
            new ZipPluginProvider(SourceUrl),
            localInstaller,
            localInstaller,
            new() {
                { "nuget", nugetInstaller },
                { "pip", pipInstaller }
            });

        //拉取插件信息
        try
        {
            FetchOnlinePluginsAsync().Wait();
            RefreshLocalPlugins();
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
        }

        //编译装载插件
        try
        {
            LoadModuleNugetEnvironment();//添加Nuget环境
            moduleSystem.ReloadModules();
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
        }
    }

    PluginMarketConfig GetConfig()
    {
        return storageSystem.GetObject(ConfigKey, defaultConfig) ?? defaultConfig;
    }
    void SaveConfig(PluginMarketConfig config)
    {
        storageSystem.SetObject(ConfigKey, config);
    }

    void LoadModuleNugetEnvironment()
    {
        var (managed, native) = nugetInstaller.ReadPackageList();
        if (managed.Length == 0 && native.Length == 0)
        {
            RegeneratePackageList();
            (managed, native) = nugetInstaller.ReadPackageList();
        }
        moduleSystem.SetExtraContext(managed, native);
    }
    void RegeneratePackageList()
    {
        var installed = pluginMarket.GetInstalledPlugins();
        if (installed.Count == 0) return;

        List<KeyValuePair<string, string>> manifest = new();
        foreach (var (pluginId, version) in installed)
        {
            PluginPackage? plugin = pluginMarket.GetAllPluginPackages().FirstOrDefault(p => p.Id == pluginId);
            if (plugin == null) continue;
            var envs = plugin.GetEnvironments(version);
            if (envs != null && envs.TryGetValue("nuget", out var nuget))
                manifest.AddRange(nuget);
        }

        if (manifest.Count > 0)
            nugetInstaller.InstallEnvironment(manifest);
    }
    bool NeedForceUpgrade(PluginPackage pluginPackage)
    {
        string? installedVersion = GetInstalledVersion(pluginPackage.Id);
        if (installedVersion == null || pluginPackage.Releases == null)
            return false;

        string clientVersion = updateService.GetCurrentVersion();
        int clientMajor = VersionResolver.GetMajorVersion(clientVersion);
        int installedMajor = VersionResolver.GetMajorVersion(installedVersion);

        if (installedMajor >= clientMajor)
            return false;

        return pluginPackage.Releases.Keys.Any(v => VersionResolver.GetMajorVersion(v) == clientMajor);
    }
}
