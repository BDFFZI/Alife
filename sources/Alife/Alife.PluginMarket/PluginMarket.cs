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

    public async Task SyncOnlinePluginPackagesAsync()
    {
        allPluginPackages = (await pluginProvider.GetPluginsAsync()).ToDictionary(plugin => plugin.Id, plugin => plugin);
        SaveCache();
    }
    public async Task InstallPlugins(Dictionary<PluginPackage, string> plugins)
    {
        //校验插件安装输入
        foreach (var (pluginPackage, version) in plugins)
        {
            if (pluginPackage.Releases == null)
                throw new Exception($"插件 {pluginPackage.Id} 并没有发布版本，无法安装，请确保插件信息填写正确。");
            if (pluginPackage.Releases.ContainsKey(version) == false)
                throw new Exception($"插件 {pluginPackage} 不存在版本 {version}，请检查版本号填写是否正确。");
        }

        //收集完整的要装的插件
        Dictionary<string, (PluginPackage plugin, string version)> installPlan = new();
        DependencyResolver dependencyResolver = new();
        foreach (var (plugin, version) in plugins)
            CollectDependencies(plugin, version);

        void CollectDependencies(PluginPackage pluginPackage, string version)
        {
            if (installPlan.ContainsKey(pluginPackage.Id))
                return;//插件已被解算，不需要重复解算

            installPlan[pluginPackage.Id] = (pluginPackage, version);

            //解算依赖的插件
            var dependencies = pluginPackage.GetDependencies(version);
            if (dependencies != null)
            {
                //添加依赖需求
                dependencyResolver.AddDependencies(dependencies);

                //确认有满足依赖的插件
                foreach (var (dependentPluginId, _) in dependencies)
                {
                    if (pluginContext.AllPluginManifests.TryGetValue(dependentPluginId, out PluginManifest value) &&
                        dependencyResolver.IsSatisfiedVersion(dependentPluginId, value.Version))
                        continue;//插件已经安装，不需要重复安装

                    if (allPluginPackages.TryGetValue(dependentPluginId, out PluginPackage? dependentPluginPackage) == false)
                        throw new Exception($"{pluginPackage.Id} 依赖的插件 {dependentPluginId} 不存在，请确认依赖信息填写正确，或被依赖插件已上传市场并正确拉取。");

                    IEnumerable<string>? versionList = dependentPluginPackage.Releases?.Keys;
                    if (versionList == null)
                        throw new Exception($"插件 {dependentPluginId} 并没有发布版本，无法安装，请确保插件信息填写正确。");

                    string bestVersion = dependencyResolver.ResolveBestVersion(dependentPluginId, versionList);
                    CollectDependencies(dependentPluginPackage, bestVersion);
                }
            }
        }

        //安装插件
        foreach ((PluginPackage plugin, string version) in installPlan.Values)
            await pluginInstaller.InstallPlugin(plugin, version);
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
                File.WriteAllText(Path.Combine(pluginPackagesCacheDirectory, $"{plugin.Id}.json"), JsonConvert.SerializeObject(plugin, Formatting.Indented));
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
                    File.Delete(pluginFile);
                    Console.WriteLine(ex);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
        }
    }
}
