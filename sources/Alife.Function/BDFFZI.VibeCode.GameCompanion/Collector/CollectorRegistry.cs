using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Alife.PluginContext;
using Newtonsoft.Json.Linq;

namespace BDFFZI.VibeCode.GameCompanion;

/// <summary>
/// 采样器注册器：扫描程序集中的 <see cref="CollectorAttribute"/> 自动注册采样器。
/// 通过 <see cref="Initialize"/> 订阅插件加载/卸载事件，自动管理采样器生命周期。
/// </summary>
public static class CollectorRegistry
{
    /// <summary>采样器描述。</summary>
    public sealed record Descriptor(
        Type ConfigType,
        string DisplayName,
        Func<CollectConfigBase, CollectorBase?> Create,
        string Ui);

    static readonly Dictionary<string, Descriptor> ByTypeName = new();
    static readonly Dictionary<string, HashSet<string>> pluginTypeNames = new();
    static Alife.PluginContext.PluginContext? pluginContext;

    /// <summary>初始化采样器注册器：扫描已有插件并订阅加载/卸载事件。</summary>
    public static void Initialize(Alife.PluginContext.PluginContext context)
    {
        pluginContext = context;
        context.PluginLoadedAsync += OnPluginLoadedAsync;
        context.PluginUnloadedAsync += OnPluginUnloadedAsync;

        // 扫描已加载的所有插件
        foreach (var (pluginId, loadContext) in context.CurrentPluginLoadContexts)
            ScanAndRegister(pluginId, loadContext.Assemblies);
    }

    static Task OnPluginLoadedAsync(string pluginId, PluginLoadContext loadContext)
    {
        ScanAndRegister(pluginId, loadContext.Assemblies);
        return System.Threading.Tasks.Task.CompletedTask;
    }

    static Task OnPluginUnloadedAsync(string pluginId, PluginLoadContext loadContext)
    {
        if (pluginTypeNames.TryGetValue(pluginId, out HashSet<string>? typeNames))
        {
            lock (ByTypeName)
            {
                foreach (string typeName in typeNames)
                    ByTypeName.Remove(typeName);
            }
            pluginTypeNames.Remove(pluginId);
        }
        return System.Threading.Tasks.Task.CompletedTask;
    }

    static void ScanAndRegister(string pluginId, IEnumerable<Assembly> assemblies)
    {
        var discovered = new HashSet<string>(StringComparer.Ordinal);

        foreach (Assembly assembly in assemblies)
        {
            Type[] types;
            try { types = assembly.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).ToArray()!; }
            catch { continue; }

            foreach (Type type in types)
            {
                if (type.IsAbstract || !typeof(CollectorBase).IsAssignableFrom(type))
                    continue;

                CollectorAttribute? attr = type.GetCustomAttribute<CollectorAttribute>();
                if (attr == null)
                    continue;

                RegisterCollector(type, attr);
                discovered.Add(attr.ConfigType.Name);
            }
        }

        if (discovered.Count > 0)
        {
            lock (ByTypeName)
            {
                pluginTypeNames[pluginId] = discovered;
            }
        }
    }

    static void RegisterCollector(Type collectorType, CollectorAttribute attr)
    {
        // 通过构造函数参数推断 config 类型，创建工厂委托
        ConstructorInfo? ctor = collectorType.GetConstructor([attr.ConfigType]);
        if (ctor == null)
            return;

        Func<CollectConfigBase, CollectorBase?> create = config =>
        {
            try { return (CollectorBase?)ctor.Invoke([config]); }
            catch { return null; }
        };

        var descriptor = new Descriptor(attr.ConfigType, attr.DisplayName, create, attr.Ui);

        lock (ByTypeName)
        {
            ByTypeName[attr.ConfigType.Name] = descriptor;
        }
    }

    /// <summary>按配置实例创建采样器（框架统一入口）。</summary>
    public static CollectorBase? Create(CollectConfigBase? config)
    {
        if (config is null || string.IsNullOrWhiteSpace(config.Name) || !config.IsEnable)
            return null;

        // 占位配置 → 占位采样器
        if (config is PlaceholderCollectConfig placeholder)
            return new PlaceholderCollector(placeholder);

        lock (ByTypeName)
        {
            return ByTypeName.TryGetValue(config.GetType().Name, out Descriptor? d)
                ? d.Create(config)
                : null;
        }
    }

    /// <summary>配置是否有效（配置已注册、已启用、名称非空）。</summary>
    public static bool IsValid(CollectConfigBase? config)
    {
        if (config is null || string.IsNullOrWhiteSpace(config.Name) || !config.IsEnable)
            return false;

        // 占位配置始终有效（保留缺失采样器信息）
        if (config is PlaceholderCollectConfig)
            return true;

        lock (ByTypeName)
        {
            return ByTypeName.ContainsKey(config.GetType().Name);
        }
    }

    /// <summary>由配置类型名 + 配置 JSON 还原配置实例。
    /// 未识别的类型创建 <see cref="PlaceholderCollectConfig"/> 保留原始数据。</summary>
    public static CollectConfigBase? CreateConfig(string configTypeName, JObject? data)
    {
        if (string.IsNullOrWhiteSpace(configTypeName) || data is null)
            return null;

        lock (ByTypeName)
        {
            if (ByTypeName.TryGetValue(configTypeName, out Descriptor? d))
            {
                try { return (CollectConfigBase?)data.ToObject(d.ConfigType); }
                catch { return null; }
            }
        }

        // 未识别的采样器类型 → 占位配置，保留原始数据
        return new PlaceholderCollectConfig
        {
            SamplerName = configTypeName,
            RawConfig = data
        };
    }

    /// <summary>配置实例的类型名（持久化序列化用）。</summary>
    public static string TypeName(CollectConfigBase config)
    {
        return config?.GetType().Name ?? "";
    }

    /// <summary>类型名的显示名。</summary>
    public static string DisplayName(string typeName)
    {
        lock (ByTypeName)
        {
            return ByTypeName.TryGetValue(typeName, out Descriptor? d) ? d.DisplayName : typeName;
        }
    }

    /// <summary>全部已注册采样器（供编辑器下拉等使用）。</summary>
    public static IReadOnlyList<(string TypeName, string DisplayName)> All
    {
        get
        {
            lock (ByTypeName)
            {
                return ByTypeName.Values.Select(d => (d.ConfigType.Name, d.DisplayName)).ToList();
            }
        }
    }

    /// <summary>采样器自带的专属配置 UI 片段（HTML）。</summary>
    public static string GetUi(string typeName)
    {
        lock (ByTypeName)
        {
            return ByTypeName.TryGetValue(typeName, out Descriptor? d) ? d.Ui : "";
        }
    }
}
