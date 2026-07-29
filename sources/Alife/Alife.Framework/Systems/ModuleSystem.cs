using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading.Tasks;
using Alife.Platform;
using Alife.PluginSystem;

namespace Alife.Framework;

/// <summary>
/// 模块是 Alife 中的功能注入点。
/// 模块系统负责驱动插件系统、插件市场等，来创造一个适合 Alife 的模块框架环境。
/// </summary>
public class ModuleSystem
{
    public static ModuleAttribute? GetModuleAttribute(Type moduleType)
    {
        return moduleType.GetCustomAttribute<ModuleAttribute>();
    }
    public static string GetModuleID(Type moduleType)
    {
        return moduleType.FullName!;
    }
    public static bool IsModule(Type type)
    {
        return type is { IsAbstract: false, IsInterface: false } && GetModuleAttribute(type) != null;
    }

    public ModuleSystem(PluginSystem.PluginSystem pluginSystem)
    {
        //插件重载时触发模块重载
        pluginSystem.PluginLoaded += OnPluginLoaded;
        pluginSystem.PluginUnloaded += OnPluginUnloaded;
        //加载现有插件的模块
        foreach ((string pluginId, PluginLoadContext pluginLoadContext) in pluginSystem.CurrentPluginLoadContexts)
            LoadPluginModule(pluginId, pluginLoadContext);
        //加载默认上下文
        LoadPluginModule(AssemblyLoadContext.Default.Name!, AssemblyLoadContext.Default);
    }

    public event Func<List<Type>, Task>? ModulesLoaded;
    public event Func<List<Type>, Task>? ModulesUnloaded;

    public IEnumerable<Type> GetAllModules()
    {
        return idToModules.Values;
    }
    public Type? GetModule(string moduleID)
    {
        return idToModules.GetValueOrDefault(moduleID);
    }

    readonly Dictionary<string, List<Type>> pluginToModules = new();
    readonly Dictionary<string, Type> idToModules = new();

    async Task OnPluginLoaded(string pluginId, PluginLoadContext pluginLoadContext)
    {
        LoadPluginModule(pluginId, pluginLoadContext);

        List<Type> moduleTypes = pluginToModules[pluginId];

        if (ModulesLoaded != null)
        {
            try
            {
                await Task.WhenAll(ModulesLoaded.GetInvocationList()
                    .Cast<Func<List<Type>, Task>>()
                    .Select(func => func(moduleTypes)));
            }
            catch (Exception e)
            {
                AlifeLog.LogError(e);
            }
        }
    }
    async Task OnPluginUnloaded(string pluginId, PluginLoadContext pluginLoadContext)
    {
        List<Type> moduleTypes = pluginToModules[pluginId];

        if (ModulesUnloaded != null)
        {
            try
            {
                await Task.WhenAll(ModulesUnloaded.GetInvocationList()
                    .Cast<Func<List<Type>, Task>>()
                    .Select(func => func(moduleTypes)));
            }
            catch (Exception e)
            {
                AlifeLog.LogError(e);
            }
        }

        foreach (Type moduleType in moduleTypes)
            idToModules.Remove(GetModuleID(moduleType));
        pluginToModules.Remove(pluginId);
    }

    void LoadPluginModule(string pluginId, AssemblyLoadContext assemblyLoadContext)
    {
        List<Type> moduleTypes = new();
        pluginToModules.Add(pluginId, moduleTypes);

        foreach (Assembly assembly in assemblyLoadContext.Assemblies)
        {
            Type[] types = assembly.GetTypes();
            foreach (Type type in types)
            {
                if (IsModule(type) == false)
                    continue;

                moduleTypes.Add(type);
                idToModules.Add(GetModuleID(type), type);
            }
        }
    }
}
