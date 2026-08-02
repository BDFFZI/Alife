using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Alife.Foundation;
using Newtonsoft.Json;

namespace Alife.PluginContext;

public struct PluginSyncReport
{
    public string[] UnloadedPlugins { get; set; }
    public string[] ReloadedPlugins { get; set; }
}
/// <summary>
/// 一个插件代表一个包含 cs、dll、版本、依赖信息 的文件夹。
/// 插件系统负责安装这些插件依赖的环境，编译 cs 为 dll，以及将他们加载到程序中，从而可以被实际使用。
/// 插件是从软件启动时就被载入的内容，并且将一直常驻，只能重载或加载新插件。
/// </summary>
/// <param name="pluginRootDirectory"></param>
/// <param name="dllOutputDirectory"></param>
/// <param name="environmentInstallers"></param>
/// <param name="codeCompiler"></param>
public class PluginContext(
    string pluginRootDirectory,
    string dllOutputDirectory,
    Dictionary<string, IEnvironmentInstaller> environmentInstallers,
    CSharpCompiler codeCompiler)
{
    public event Func<string, PluginLoadContext, Task>? PluginLoaded;
    public event Func<string, PluginLoadContext, Task>? PluginUnloaded;

    public string PluginRootDirectory => pluginRootDirectory;
    public IReadOnlyDictionary<string, PluginLoadContext> CurrentPluginLoadContexts => currentPluginLoadContexts;
    public IReadOnlyDictionary<string, PluginManifest> AllPluginManifests => allPluginManifests;
    public Func<string, PluginManifest> PluginManifestFallback { get; set; } = _ => new PluginManifest() { Version = "0.0.0" };

    /// <summary>
    /// 当插件增加或插件依赖变更时应调用该函数来同步环境。
    /// 调用后会重新解算环境，卸载多余的插件，并加载新增的插件。
    /// </summary>
    public async Task<PluginSyncReport> SyncPluginEnvironment()
    {
        PluginSyncReport report = new PluginSyncReport();

        SyncPluginManifests();
        await SyncEnvironment();
        await SyncPlugin();

        void SyncPluginManifests()
        {
            allPluginManifests.Clear();
            foreach (var pluginDirectory in Directory.GetDirectories(pluginRootDirectory))
            {
                string pluginId = Path.GetFileName(pluginDirectory);
                string pluginDependencyFile = GetPluginManifestPath(pluginId);
                PluginManifest pluginManifest = File.Exists(pluginDependencyFile)
                    ? JsonConvert.DeserializeObject<PluginManifest>(File.ReadAllText(pluginDependencyFile))
                    : PluginManifestFallback(pluginId);
                allPluginManifests[pluginId] = pluginManifest;
            }
        }

        async Task SyncEnvironment()
        {
            //汇总所有环境依赖
            Dictionary<string, List<KeyValuePair<string, string>>> allEnvironments = new();
            foreach (PluginManifest dependency in allPluginManifests.Values)
            {
                if (dependency.Environments == null)
                    continue;
                foreach (KeyValuePair<string, Dictionary<string, string>> dependencyEnvironment in dependency.Environments)
                {
                    if (allEnvironments.TryGetValue(dependencyEnvironment.Key, out List<KeyValuePair<string, string>>? environment) == false)
                        allEnvironments.Add(dependencyEnvironment.Key, environment = new List<KeyValuePair<string, string>>());
                    environment.AddRange(dependencyEnvironment.Value);
                }
            }

            //确认环境支持
            foreach (string environmentType in allEnvironments.Keys)
            {
                if (environmentInstallers.ContainsKey(environmentType) == false)
                    throw new NotSupportedException($"不支持的环境类型 {environmentType}，请检查环境字段是否填写错误。");
            }

            //安装环境
            foreach ((string environmentType, List<KeyValuePair<string, string>> environment) in allEnvironments)
                await environmentInstallers[environmentType].InstallEnvironment(environment);
        }

        async Task SyncPlugin()
        {
            report.UnloadedPlugins = currentPluginLoadContexts.Keys
                .Where(pluginId => !allPluginManifests.ContainsKey(pluginId))
                .ToArray();

            //清除已不在的插件
            foreach (string pluginId in report.UnloadedPlugins)
            {
                if (currentPluginLoadContexts.TryGetValue(pluginId, out PluginLoadContext? context))
                    await context.DisposeAsync();
            }

            report.ReloadedPlugins = allPluginManifests.Keys
                .Where(pluginId => !currentPluginLoadContexts.ContainsKey(pluginId))
                .ToArray();

            //加载新增的插件
            foreach (string pluginId in report.ReloadedPlugins)
            {
                try
                {
                    if (currentPluginLoadContexts.ContainsKey(pluginId) == false)
                        await ReloadPluginDll(pluginId);
                }
                catch (Exception e)
                {
                    AlifeLog.LogError(e);
                }
            }
        }

        return report;
    }
    public async Task ReloadPluginDll(string pluginId, bool recompile = false)
    {
        if (!allPluginManifests.TryGetValue(pluginId, out PluginManifest pluginEnvironment))
            throw new Exception("未找到插件信息，请确认插件存在并已同步环境。");

        //卸载旧dll
        if (currentPluginLoadContexts.TryGetValue(pluginId, out PluginLoadContext? pluginLoadContext))
            await pluginLoadContext.DisposeAsync();
        if (recompile)
            File.Delete(GetPluginCompiledDllPath(pluginId));

        //确保依赖插件环境存在
        if (pluginEnvironment.Dependencies != null)
        {
            foreach ((string dependentPluginId, string _) in pluginEnvironment.Dependencies)
            {
                if (currentPluginLoadContexts.ContainsKey(dependentPluginId) == false)
                    await ReloadPluginDll(dependentPluginId);
            }
        }

        //加载新插件dll
        pluginLoadContext = new(pluginId, [GetPluginDirectoryPath(pluginId)]);
        foreach (string dll in RequirePluginDll(pluginId))
            pluginLoadContext.LoadDll(dll);
        currentPluginLoadContexts.Add(pluginId, pluginLoadContext);
        pluginLoadContext.Disposed += async () =>
        {
            currentPluginLoadContexts.Remove(pluginId);
            if (PluginUnloaded != null)
            {
                try
                {
                    await Task.WhenAll(PluginUnloaded.GetInvocationList()
                        .Cast<Func<string, PluginLoadContext, Task>>()
                        .Select(func => func(pluginId, pluginLoadContext)));
                }
                catch (Exception e)
                {
                    AlifeLog.LogError(e);
                }
            }
        };

        //触发插件重载事件
        if (PluginLoaded != null)
        {
            try
            {
                await Task.WhenAll(PluginLoaded.GetInvocationList()
                    .Cast<Func<string, PluginLoadContext, Task>>()
                    .Select(func => func(pluginId, pluginLoadContext)));
            }
            catch (Exception e)
            {
                AlifeLog.LogError(e);
            }
        }
    }
    public string GetPluginManifestPath(string pluginId) => Path.Combine(pluginRootDirectory, pluginId, "manifest.json");
    public string GetPluginDirectoryPath(string pluginId) => Path.Combine(pluginRootDirectory, pluginId);


    readonly Dictionary<string, PluginManifest> allPluginManifests = new();
    readonly Dictionary<string, PluginLoadContext> currentPluginLoadContexts = new();

    string GetPluginCompiledDllPath(string pluginId) => Path.Combine(dllOutputDirectory, pluginId + ".dll");


    void CompilePluginCode(string pluginId)
    {
        string pluginDirectory = GetPluginDirectoryPath(pluginId);
        if (!Directory.Exists(pluginDirectory))
            throw new Exception($"未找到名为 {pluginId} 的插件目录，每个插件需在插件根目录建立同名子目录作为插件目录");

        if (!allPluginManifests.TryGetValue(pluginId, out PluginManifest pluginManifest))
            throw new Exception($"未找到插件环境信息，请先使用 {nameof(SyncPluginEnvironment)} 同步环境。");

        string[] codeFiles = Directory.GetFiles(pluginDirectory, "*.cs", SearchOption.AllDirectories);
        if (codeFiles.Length == 0)
            throw new Exception("插件目录中不存在 cs 文件，请确认插件是否真的需要编译代码，且代码文件放在了插件目录中。");

        List<string> dllFiles = Directory.GetFiles(pluginDirectory, "*.dll", SearchOption.AllDirectories).ToList();
        if (pluginManifest.Dependencies != null)
        {
            DependencyResolver pluginDependencyResolver = new();
            pluginDependencyResolver.AddDependencies(pluginManifest.Dependencies);

            foreach (string dependencyPlugin in pluginManifest.Dependencies.Keys)
            {
                //验证依赖插件存在
                if (allPluginManifests.TryGetValue(dependencyPlugin, out var manifest) == false)
                    throw new Exception($"未找到依赖的插件 {dependencyPlugin}，请检查依赖的插件 id 填写是否正确，是否存在，或是否已同步插件环境。");

                //验证依赖插件当前版本可用
                if (!pluginDependencyResolver.IsSatisfiedVersion(dependencyPlugin, manifest.Version))
                    throw new Exception($"环境中的 {dependencyPlugin} 插件版本不满足于 {pluginId} 的要求，请更换插件版本，或调整版本号要求。");

                dllFiles.AddRange(RequirePluginDll(dependencyPlugin));
            }
        }

        codeCompiler.Compile(GetPluginCompiledDllPath(pluginId), codeFiles, dllFiles.ToArray());
    }
    List<string> RequirePluginDll(string pluginId)
    {
        List<string> result = new List<string>();

        string pluginDirectory = GetPluginDirectoryPath(pluginId);
        //添加需要编译的dll
        if (Directory.GetFiles(pluginDirectory, "*.cs", SearchOption.AllDirectories).Length > 0)
        {
            string pluginCompiledDllPath = GetPluginCompiledDllPath(pluginId);
            if (AlifeUtility.IsValidDll(pluginCompiledDllPath, out _) == false)
                CompilePluginCode(pluginId);
            result.Add(pluginCompiledDllPath);
        }

        //添加插件自带的dll
        result.AddRange(Directory.GetFiles(pluginDirectory, "*.dll", SearchOption.AllDirectories));

        return result;
    }
}