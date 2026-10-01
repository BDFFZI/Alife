using Alife.Foundation;

namespace Alife.Framework;

public class PluginSystem
{
    public event Action? PluginSynced;
    public PluginMarket PluginMarket => pluginMarket;
    public PluginContext PluginContext => pluginContext;

    /// <summary>
    /// 云端拉取插件
    /// </summary>
    public async Task SyncOnlinePluginPackages()
    {
        await pluginMarket.SyncOnlinePluginPackagesAsync();
    }

    /// <summary>
    /// 刷新本地插件。
    /// 安装卸载插件时会自动同步，因此默认情况下无需调用，只有手动修改插件清单等数据后，才会用到此函数来主动同步。
    /// </summary>
    public async Task SyncLocalPluginEnvironment(bool reloadEnvironment = true)
    {
        await pluginContext.SyncPluginEnvironment(reloadEnvironment);
    }

    public IReadOnlyDictionary<string, PluginPackage> GetAllOnlinePlugins()
    {
        return pluginMarket.AllPluginPackages;
    }

    public IReadOnlyDictionary<string, PluginManifest> GetAllLocalPlugins()
    {
        return pluginContext.AllPluginManifests;
    }

    public List<PluginPackage> GetForceUpgradedPlugins()
    {
        return GetAllOnlinePlugins().Values.Where(NeedForceUpgrade).ToList();

        bool NeedForceUpgrade(PluginPackage pluginPackage)
        {
            string? installedVersion = GetInstalledVersion(pluginPackage.Id);
            if (installedVersion == null || pluginPackage.Releases == null)
                return false;

            int clientMajor = DependencyResolver.GetMajorVersion(AlifeContext.AppVersion);
            int installedMajor = DependencyResolver.GetMajorVersion(installedVersion);

            if (installedMajor >= clientMajor)
                return false;

            return pluginPackage.Releases.Keys.Any(v => DependencyResolver.GetMajorVersion(v) == clientMajor);
        }
    }

    public bool IsInstalled(string pluginId)
    {
        return GetAllLocalPlugins().ContainsKey(pluginId);
    }

    public bool IsClientCompatible(string pluginVersion)
    {
        return DependencyResolver.GetMajorVersion(pluginVersion) <= DependencyResolver.GetMajorVersion(AlifeContext.AppVersion);
    }

    public bool HasUpdate(PluginPackage pluginPackage)
    {
        string? installedVersion = GetInstalledVersion(pluginPackage.Id);
        if (installedVersion == null || pluginPackage.Releases == null)
            return false;

        string? latestVersion = GetLatestVersion(pluginPackage);
        if (latestVersion == null || latestVersion == installedVersion)
            return false;

        return DependencyResolver.CompareVersions(installedVersion, latestVersion) < 0;
    }

    public string? GetInstalledVersion(string pluginId)
    {
        if (GetAllLocalPlugins().TryGetValue(pluginId, out PluginManifest pluginManifest))
            return pluginManifest.Version;
        return null;
    }

    public string? GetLatestVersion(PluginPackage pluginPackage)
    {
        return pluginPackage.Releases?.Keys
            .Where(IsClientCompatible)
            .OrderByDescending(v => v, Comparer<string>.Create(DependencyResolver.CompareVersions))
            .FirstOrDefault();
    }

    public async Task InstallPlugins(List<KeyValuePair<PluginPackage, string>> plugins)
    {
        //卸载插件并删除其dll，以便在后同步插件环境时重新编译加载
        foreach ((PluginPackage pluginPackage, _) in plugins)
            await pluginContext.ClearPluginDll(pluginPackage.Id);
        //下载新的插件文件
        await pluginMarket.InstallPlugins(plugins);
        //重新加载
        await pluginContext.SyncPluginEnvironment();
    }

    public async Task UninstallPlugins(IEnumerable<PluginPackage> pluginPackages)
    {
        foreach (var plugin in pluginPackages)
            await pluginMarket.UninstallPlugins(plugin.Id);
        await pluginContext.SyncPluginEnvironment();
    }

    public async Task ReloadPlugin(string pluginId)
    {
        await pluginContext.ReloadPluginDll(pluginId, true);
        await pluginContext.SyncPluginEnvironment(false);
    }

    readonly PluginMarket pluginMarket;
    readonly PluginContext pluginContext;

    public PluginSystem(PluginMarket pluginMarket,
        PluginContext pluginContext)
    {
        this.pluginMarket = pluginMarket;
        this.pluginContext = pluginContext;
        pluginMarket.PluginPackageSynced += () => PluginSynced?.Invoke();
        pluginContext.PluginEnvironmentSynced += _ => PluginSynced?.Invoke();
    }
}