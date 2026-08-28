using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Newtonsoft.Json.Linq;

namespace Alife.Function.GameCompanion.Collectors;

/// <summary>
/// 采样器注册器：以配置类型为键，按「配置实例」创建采样器、校验配置、提供类型清单。
/// 注册时仅需写 displayName，key 由 typeof(TConfig).Name 自动派生，不再手动维护字符串标识。
/// 新增采样器 = 新配置类 + 新采样器类 + 一行 Register，核心代码零改动。
/// </summary>
public static class CollectorRegistry
{
    /// <summary>采样器描述。</summary>
    public sealed record Descriptor(
        Type ConfigType,
        string DisplayName,
        Func<CollectConfigBase, CollectorBase?> Create,
        Func<CollectConfigBase, bool>? Validate,
        string Ui);

    static readonly Dictionary<string, Descriptor> ByTypeName = new();

    static CollectorRegistry()
    {
        // 自动发现本程序集内的采样器：触发各自静态构造使其自注册。
        foreach (Type type in typeof(CollectorBase).Assembly.GetTypes())
        {
            if (type.IsAbstract || !typeof(CollectorBase).IsAssignableFrom(type))
                continue;
            RuntimeHelpers.RunClassConstructor(type.TypeHandle);
        }
    }

    /// <summary>注册一种采样器（key 由 typeof(TConfig).Name 自动派生，无需手动指定）。</summary>
    public static void Register<TConfig>(
        string displayName,
        Func<TConfig, CollectorBase?> create,
        Func<TConfig, bool>? validate = null,
        string ui = "") where TConfig : CollectConfigBase
    {
        var descriptor = new Descriptor(
            typeof(TConfig),
            displayName,
            config => create((TConfig)config),
            validate == null ? null : config => validate((TConfig)config),
            ui);

        lock (ByTypeName)
        {
            ByTypeName[typeof(TConfig).Name] = descriptor;
        }
    }

    /// <summary>按配置实例创建采样器（框架统一入口）。</summary>
    public static CollectorBase? Create(CollectConfigBase? config)
    {
        if (!IsValid(config))
            return null;
        lock (ByTypeName)
        {
            return ByTypeName.TryGetValue(config.GetType().Name, out Descriptor? d)
                ? d.Create(config)
                : null;
        }
    }

    /// <summary>配置是否在其采样器下有效。</summary>
    public static bool IsValid(CollectConfigBase? config)
    {
        if (config is null || string.IsNullOrWhiteSpace(config.Name) || !config.IsEnable)
            return false;
        lock (ByTypeName)
        {
            if (!ByTypeName.TryGetValue(config.GetType().Name, out Descriptor? d))
                return false;
            return d.Validate?.Invoke(config) ?? true;
        }
    }

    /// <summary>由配置类型名 + 配置 JSON 还原配置实例（持久化反序列化用）。</summary>
    public static CollectConfigBase? CreateConfig(string configTypeName, JObject? data)
    {
        if (string.IsNullOrWhiteSpace(configTypeName) || data is null)
            return null;
        lock (ByTypeName)
        {
            if (!ByTypeName.TryGetValue(configTypeName, out Descriptor? d))
                return null;
            try { return (CollectConfigBase?)data.ToObject(d.ConfigType); }
            catch { return null; }
        }
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