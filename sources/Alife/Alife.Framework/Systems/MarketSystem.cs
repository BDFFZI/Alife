using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Alife.PluginMarket;
using Alife.PluginSystem;

namespace Alife.Framework;

public class MarketSystem(
    PluginMarket.PluginMarket pluginMarket,
    PluginSystem.PluginSystem pluginSystem)
{
    /// <summary>
    /// 云端拉取插件
    /// </summary>
    public async Task SyncPluginMarket()
    {
        await pluginMarket.SyncOnlinePluginPackagesAsync();
    }
    /// <summary>
    /// 刷新本地插件
    /// </summary>
    public async Task SyncPluginEnvironment()
    {
        await pluginSystem.SyncPluginEnvironment();
    }

    public IReadOnlyDictionary<string, PluginPackage> GetAllOnlinePlugins()
    {
        return pluginMarket.AllPluginPackages;
    }
    public IReadOnlyDictionary<string, PluginManifest> GetAllLocalPlugins()
    {
        return pluginSystem.AllPluginManifests;
    }
    public List<PluginPackage> GetForceUpgradedPlugins()
    {
        return GetAllOnlinePlugins().Values.Where(NeedForceUpgrade).ToList();

        bool NeedForceUpgrade(PluginPackage pluginPackage)
        {
            string? installedVersion = GetInstalledVersion(pluginPackage.Id);
            if (installedVersion == null || pluginPackage.Releases == null)
                return false;

            int clientMajor = DependencyResolver.GetMajorVersion(currentClientVersion);
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
        return DependencyResolver.GetMajorVersion(pluginVersion) <= DependencyResolver.GetMajorVersion(currentClientVersion);
    }
    public bool HasUpdate(PluginPackage pluginPackage)
    {
        string? installedVersion = GetInstalledVersion(pluginPackage.Id);
        if (installedVersion == null || pluginPackage.Releases == null)
            return false;

        string? latestVersion = GetLatestVersion(pluginPackage);
        if (latestVersion == null || latestVersion == installedVersion)
            return false;

        return true;
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
    public List<string> GetBeDependentPlugins(string pluginId)
    {
        return pluginMarket.ResolveBeDependentPlugins(pluginId);
    }
    
    public async Task InstallPlugins(Dictionary<PluginPackage, string> plugins)
    {
        await pluginMarket.InstallPlugins(plugins);
        PluginSyncReport report = await pluginSystem.SyncPluginEnvironment();
        foreach (PluginPackage pluginPackage in plugins.Keys)
        {
            if (report.ReloadedPlugins.Contains(pluginPackage.Id))
                continue;
            await pluginSystem.ReloadPluginDll(pluginPackage.Id);
        }
    }
    public async Task UninstallPlugins(IEnumerable<PluginPackage> pluginPackages)
    {
        foreach (var plugin in pluginPackages)
            await pluginMarket.UninstallPlugins(plugin.Id);
        await pluginSystem.SyncPluginEnvironment();
    }
    
    readonly string currentClientVersion = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.0.0";
}
