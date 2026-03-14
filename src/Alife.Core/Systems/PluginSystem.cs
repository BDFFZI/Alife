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
        return pluginTypes;
    }

    public void ReloadPlugins()
    {
        PreReloadPlugins?.Invoke();

        // 卸载由该系统加载的程序集
        foreach (Assembly pluginAssembly in loadedAssemblies.Values)
        {
            var context = AssemblyLoadContext.GetLoadContext(pluginAssembly);
            if (context != null && context.IsCollectible)
            {
                context.Unload();
            }
        }
        loadedAssemblies.Clear();

        // 加载 Plugins 文件夹下的所有程序集
        string pluginRoot = Path.Combine(AppContext.BaseDirectory, "Plugins");
        if (!Directory.Exists(pluginRoot))
            Directory.CreateDirectory(pluginRoot);

        string[] pluginPaths = Directory.GetFiles(pluginRoot, "*.dll", SearchOption.AllDirectories);
        foreach (string pluginPath in pluginPaths)
        {
            try
            {
                string dllName = Path.GetFileName(pluginPath);
                if (!loadedAssemblies.ContainsKey(dllName))
                {
                    // 使用可收集的上下文以便后续卸载（可选，这里保持原有逻辑）
                    var context = new AssemblyLoadContext(dllName, true);
                    Assembly assembly = context.LoadFromAssemblyPath(pluginPath);
                    loadedAssemblies.Add(dllName, assembly);
                }
            }
            catch { /* 忽略加载失败的程序集 */ }
        }

        // 重新扫描所有已加载程序集中的 IPlugin
        pluginTypes.Clear();
        Assembly[] allAssemblies = AppDomain.CurrentDomain.GetAssemblies();
        foreach (Assembly assembly in allAssemblies)
        {
            try
            {
                foreach (Type type in assembly.GetTypes())
                {
                    if (type.IsAssignableTo(typeof(IPlugin)) && !type.IsAbstract && !type.IsInterface)
                    {
                        pluginTypes.Add(type);
                    }
                }
            }
            catch { /* 忽略无法获取类型的程序集 */ }
        }

        SyncFolder();
        PostReloadPlugins?.Invoke();
    }

    public void SaveData()
    {
        storageSystem.SetObject("PluginSystem/PluginFolder", pluginFolder);
    }

    public PluginSystem(StorageSystem storageSystem)
    {
        this.storageSystem = storageSystem;
        this.pluginTypes = new List<Type>();
        this.loadedAssemblies = new Dictionary<string, Assembly>();

        ReloadPlugins();
        LoadPluginFolder();
    }

    public void Dispose()
    {
        SaveData();
    }

    void LoadPluginFolder()
    {
        pluginFolder = storageSystem.GetObject("PluginSystem/PluginFolder", new StringFolder("全部插件"))!;
        SyncFolder();
    }

    void SyncFolder()
    {
        if (pluginFolder == null) return;

        var currentTypeNames = pluginTypes.Select(t => t.FullName!).ToHashSet();
        
        // 移除不存在的插件
        pluginFolder.RemoveAll(name => !currentTypeNames.Contains(name));
        
        // 添加新发现的插件
        foreach (var typeName in currentTypeNames)
        {
            if (!pluginFolder.Contains(typeName))
            {
                pluginFolder.Strings.Add(typeName);
            }
        }
    }

    readonly StorageSystem storageSystem;
    readonly List<Type> pluginTypes;
    readonly Dictionary<string, Assembly> loadedAssemblies;
    StringFolder pluginFolder = null!;
}
