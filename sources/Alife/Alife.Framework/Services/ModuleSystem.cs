using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading.Tasks;
using Alife.Foundation;

namespace Alife.Framework;

public partial class ModuleSystem
{
    public static ModuleAttribute? GetModuleAttribute(Type moduleType)
    {
        return moduleType.GetCustomAttribute<ModuleAttribute>();
    }
    public static string GetModuleId(Type moduleType)
    {
        return moduleType.FullName!;
    }
    public static bool IsModule(Type type)
    {
        return type is { IsAbstract: false, IsInterface: false } && GetModuleAttribute(type) != null;
    }
}

/// <summary>
/// 模块是 Alife 中的功能注入点。
/// 模块系统负责驱动插件系统、插件市场等，来创造一个适合 Alife 的模块框架环境。
/// </summary>
public partial class ModuleSystem
{
    public event Func<List<Type>, Task>? ModulesLoadedAsync;
    public event Func<List<Type>, Task>? ModulesUnloadedAsync;

    public IEnumerable<Type> GetAllModules()
    {
        return idToModules.Values;
    }
    public Type? GetModule(string moduleId)
    {
        return idToModules.GetValueOrDefault(moduleId);
    }
    public StringFolder GetModuleFolder()
    {
        return moduleFolder;
    }
    public void SaveModuleFolder()
    {
        storageSystem.SetSetting("ModuleCategory", moduleFolder);
    }

    public async Task LoadAssemblyModulesAsync(AssemblyLoadContext assemblyLoadContext)
    {
        LoadPluginModule(assemblyLoadContext);

        List<Type> moduleTypes = assemblyToModules[assemblyLoadContext];

        if (ModulesLoadedAsync != null)
        {
            try
            {
                await Task.WhenAll(ModulesLoadedAsync.GetInvocationList()
                    .Cast<Func<List<Type>, Task>>()
                    .Select(func => func(moduleTypes)));
            }
            catch (Exception e)
            {
                AlifeLog.LogError(e);
            }
        }
    }
    public async Task UnloadAssemblyModulesAsync(AssemblyLoadContext assemblyLoadContext)
    {
        List<Type> moduleTypes = assemblyToModules[assemblyLoadContext];

        if (ModulesUnloadedAsync != null)
        {
            try
            {
                await Task.WhenAll(ModulesUnloadedAsync.GetInvocationList()
                    .Cast<Func<List<Type>, Task>>()
                    .Select(func => func(moduleTypes)));
            }
            catch (Exception e)
            {
                AlifeLog.LogError(e);
            }
        }

        foreach (Type moduleType in moduleTypes)
            idToModules.Remove(GetModuleId(moduleType));
        assemblyToModules.Remove(assemblyLoadContext);

        SyncModuleFolder();
    }

    readonly Dictionary<AssemblyLoadContext, List<Type>> assemblyToModules = new();
    readonly Dictionary<string, Type> idToModules = new();
    readonly StorageSystem storageSystem;
    readonly StringFolder moduleFolder;

    public ModuleSystem(StorageSystem storageSystem)
    {
        this.storageSystem = storageSystem;
        moduleFolder = storageSystem.GetSetting("ModuleCategory", new StringFolder("全部模块"))!;

        //加载默认上下文
        LoadPluginModule(AssemblyLoadContext.Default);
    }

    void LoadPluginModule(AssemblyLoadContext assemblyLoadContext)
    {
        List<Type> moduleTypes = new();
        assemblyToModules.Add(assemblyLoadContext, moduleTypes);

        foreach (Assembly assembly in assemblyLoadContext.Assemblies)
        {
            Type[] types = assembly.GetTypes();
            foreach (Type type in types)
            {
                if (IsModule(type) == false)
                    continue;

                moduleTypes.Add(type);
                idToModules.Add(GetModuleId(type), type);
            }
        }

        SyncModuleFolder();
    }
    void SyncModuleFolder()
    {
        HashSet<string> currentModules = idToModules.Keys.ToHashSet();

        //与当前统计的模块对冲，不能对冲说明是多余的
        moduleFolder.RemoveAll(name => currentModules.Remove(name) == false);
        //对冲后剩下的就是还没有加入文件夹的模块
        foreach (var moduleId in currentModules)
        {
            ModuleAttribute moduleAttribute = GetModuleAttribute(GetModule(moduleId)!)!;

            StringFolder folder = moduleFolder;
            string[] path = moduleAttribute.DefaultCategory.Split("/", StringSplitOptions.RemoveEmptyEntries);
            foreach (string subFolderName in path)
            {
                string name = subFolderName;
                StringFolder? subFolder = folder.Folders.FirstOrDefault(subFolder => subFolder.Name == name);
                if (subFolder == null)
                {
                    subFolder = new StringFolder(subFolderName);
                    folder.Folders.Add(subFolder);
                }

                folder = subFolder;
            }

            folder.Strings.Add(moduleId);
        }
    }
}