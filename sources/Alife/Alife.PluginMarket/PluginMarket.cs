using Alife.Foundation;
using Alife.PluginContext;
using Newtonsoft.Json;

namespace Alife.PluginMarket;

public interface IPluginProvider
{
    /// <summary>
    /// 获取托管的所有插件信息
    /// </summary>
    /// <returns></returns>
    public Task<PluginPackage[]> GetPluginsAsync();
}

public interface IPluginInstaller
{
    /// <summary>
    /// 不需要考虑环境依赖，仅重新安装插件本体
    /// </summary>
    /// <param name="pluginPackage"></param>
    /// <param name="version"></param>
    public Task InstallPlugin(PluginPackage pluginPackage, string version);

    public Task UninstallPlugin(string pluginId);
}

/// <summary>
/// 一种在线的插件分发平台叫做插件市场，上传其中的插件都会提供一个包清单文件，里面包含对应插件的各种描述信息和发行源码等资源。
/// 插件市场类就是负责处理其中插件的安装工作，安装仅涉及文件系统上的变动，不包含环境同步、插件加载等。
/// </summary>
public class PluginMarket
{
    public IReadOnlyDictionary<string, PluginPackage> AllPluginPackages => allPluginPackages;
    public string PluginPackagesCacheDirectory => pluginPackagesCacheDirectory;
    public event Action<PluginPackage, string>? PluginInstalled;

    public async Task SyncOnlinePluginPackagesAsync()
    {
        allPluginPackages = (await pluginProvider.GetPluginsAsync()).ToDictionary(plugin => plugin.Id, plugin => plugin);
        SaveCache();
    }

    public async Task InstallPlugins(IEnumerable<KeyValuePair<PluginPackage, string>> plugins)
    {
        DependencyResolver dependencyResolver = new();
        List<KeyValuePair<PluginPackage, string>> pluginPlan = plugins.ToList();
        while (pluginPlan.Count != 0)
        {
            (PluginPackage plugin, string version) = pluginPlan[0];
            //安装插件文件
            await pluginInstaller.InstallPlugin(plugin, version);
            PluginInstalled?.Invoke(plugin, version);
            //检测依赖插件
            PluginManifest manifest = pluginContext.LoadPluginManifest(plugin.Id);
            if (manifest.Dependencies != null)
            {
                foreach ((string pluginId, string versionSpec) in manifest.Dependencies)
                {
                    dependencyResolver.AddDependency(pluginId, versionSpec);
                    if (pluginContext.AllPluginManifests.TryGetValue(pluginId, out PluginManifest pluginManifest) &&
                        dependencyResolver.IsSatisfiedVersion(pluginId, pluginManifest.Version))
                        continue; //依赖已安装

                    PluginPackage? pluginPackage = allPluginPackages.GetValueOrDefault(pluginId);
                    if (pluginPackage == null)
                        throw new Exception($"无法找到依赖插件 {pluginId} 的包信息，请确保已将其上传到没有发布版本，无法安装。");
                    var releases = allPluginPackages[pluginId].Releases;
                    if (releases == null)
                        throw new Exception($"依赖的插件 {pluginId} 没有发布版本，无法安装。");

                    string bestVersion = dependencyResolver.ResolveBestVersion(pluginId, releases.Select(pair => pair.Key));
                    pluginPlan.Add(new KeyValuePair<PluginPackage, string>(pluginPackage, bestVersion));
                }
            }

            pluginPlan.RemoveAt(0);
        }
    }

    public async Task UninstallPlugins(string pluginId)
    {
        List<string> dependencies = ResolveBeDependentPlugins(pluginId);
        if (dependencies.Count != 0)
            throw new Exception($"插件 {pluginId} 被 {string.Join(',', dependencies)} 插件所依赖，无法卸载，请先卸载这些依赖插件。");
        await pluginInstaller.UninstallPlugin(pluginId);
    }

    public List<string> ResolveBeDependentPlugins(string pluginId)
    {
        List<string> dependents = new();
        foreach ((string id, PluginManifest manifest) in pluginContext.AllPluginManifests)
        {
            if (id == pluginId)
                continue;

            Dictionary<string, string>? dependencies = manifest.Dependencies;
            if (dependencies != null && dependencies.ContainsKey(pluginId))
                dependents.Add(id);
        }

        return dependents;
    }

    readonly PluginContext.PluginContext pluginContext;
    readonly IPluginProvider pluginProvider;
    readonly IPluginInstaller pluginInstaller;
    readonly string pluginPackagesCacheDirectory;
    Dictionary<string, PluginPackage> allPluginPackages = new();

    public PluginMarket(
        PluginContext.PluginContext pluginContext,
        IPluginProvider pluginProvider,
        IPluginInstaller pluginInstaller,
        string pluginPackagesCacheDirectory)
    {
        this.pluginContext = pluginContext;
        this.pluginProvider = pluginProvider;
        this.pluginInstaller = pluginInstaller;
        this.pluginPackagesCacheDirectory = pluginPackagesCacheDirectory;

        LoadCache();
    }

    void SaveCache()
    {
        try
        {
            foreach (var plugin in Directory.GetFiles(pluginPackagesCacheDirectory))
                File.Delete(plugin);
            foreach ((string _, PluginPackage plugin) in allPluginPackages)
                File.WriteAllText(Path.Combine(pluginPackagesCacheDirectory, $"{plugin.Id}.json"),
                    JsonConvert.SerializeObject(plugin, Formatting.Indented));
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
        }
    }

    void LoadCache()
    {
        try
        {
            foreach (string pluginFile in Directory.GetFiles(pluginPackagesCacheDirectory, "*.json"))
            {
                try
                {
                    string json = File.ReadAllText(pluginFile);
                    PluginPackage? plugin = JsonConvert.DeserializeObject<PluginPackage>(json);
                    if (plugin != null)
                        allPluginPackages[plugin.Id] = plugin;
                }
                catch (Exception ex)
                {
                    AlifeLog.LogWarning($"加载插件缓存包信息失败:\n{pluginFile}\n{ex}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
        }
    }
}