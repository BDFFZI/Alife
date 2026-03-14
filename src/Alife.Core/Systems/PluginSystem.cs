using System.Reflection;
using System.Runtime.Loader;
using Alife.Abstractions;

namespace Alife;

public class PluginSystem : IDisposable
{
    public event Action? PreReloadPlugins;
    public event Action? PostReloadPlugins;

    public StringFolder GetPluginFolder()
    {
        return pluginFolder;
    }

    public IEnumerable<Type> GetAllPlugins()
    {
        return pluginTypes.Values;
    }
    public Type? GetPlugin(string pluginID)
    {
        return pluginTypes.GetValueOrDefault(pluginID);
    }
    public string GetPluginID(Type pluginType)
    {
        return pluginType.AssemblyQualifiedName!;
    }

    public void ReloadPlugins()
    {
        PreReloadPlugins?.Invoke();

        //卸载插件
        if (pluginContext != null)
            pluginContext.Unload();
        pluginContext = new AssemblyLoadContext("AllPluginsContext", isCollectible: true);

        //获取插件
        string pluginRoot = Path.Combine(AppContext.BaseDirectory, "Plugins");
        if (!Directory.Exists(pluginRoot))
            Directory.CreateDirectory(pluginRoot);
        string[] pluginPaths = Directory.GetFiles(pluginRoot, "*.dll", SearchOption.AllDirectories);

        //加载插件
        foreach (string pluginPath in pluginPaths)
        {
            try { pluginContext.LoadFromAssemblyPath(pluginPath); }
            catch (Exception)
            {
                // ignored 可能包含一些非C#的dll
            }
        }

        // 重新扫描所有已加载程序集中的 IPlugin
        pluginTypes.Clear();
        Assembly[] allAssemblies = AppDomain.CurrentDomain.GetAssemblies();
        foreach (Assembly assembly in allAssemblies)
        {
            Console.WriteLine(assembly.FullName);
            try
            {
                foreach (Type type in assembly.GetTypes())
                {
                    if (type.IsAssignableTo(typeof(IPlugin)) == false)
                        continue;
                    if (type.IsAbstract)
                        continue;
                    if (type.IsInterface)
                        continue;

                    pluginTypes.Add(GetPluginID(type), type);
                }
            }
            catch
            {
                /* 忽略无法获取类型的程序集 */
            }
        }

        SyncFolder();
        PostReloadPlugins?.Invoke();
    }

    public void SaveData()
    {
        storageSystem.SetObject("PluginSystem/PluginFolder", pluginFolder);
    }


    readonly StorageSystem storageSystem;
    readonly Dictionary<string, Type> pluginTypes;
    readonly StringFolder pluginFolder;
    AssemblyLoadContext? pluginContext;

    public PluginSystem(StorageSystem storageSystem)
    {
        this.storageSystem = storageSystem;
        pluginTypes = new Dictionary<string, Type>();
        pluginFolder = storageSystem.GetObject("PluginSystem/PluginFolder", new StringFolder("全部插件"))!;

        ReloadPlugins();
    }

    public void Dispose()
    {
        SaveData();
    }

    void SyncFolder()
    {
        HashSet<string> currentPlugins = pluginTypes.Keys.ToHashSet();

        //移除无效插件，同时如果有效则剔除
        pluginFolder.RemoveAll(name => currentPlugins.Remove(name) == false);
        //剩下的就是还没有的插件，添加到根目录
        foreach (var typeName in currentPlugins)
            pluginFolder.Strings.Add(typeName);
    }
}
