using System.Reflection;
using Alife.Foundation;

namespace Alife.Framework;

/// <summary>
/// 可插拔客户端界面的标识：不带业务参数，只表示「这个组件类可以被宿主界面装载」，具体数据由派生特性自己定义。
/// 被标记的组件类本身就是宿主界面用 DynamicComponent 渲染的内容组件。
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public abstract class UIAttribute : Attribute
{
    /// <summary>
    /// 是否允许在「界面管理」里被关闭。关掉就再也进不去的页面（界面管理、插件市场、插件环境）声明为 false。
    /// </summary>
    public bool Closable { get; set; } = true;
}

/// <summary>
/// 一条插拔式 UI 登记。
/// </summary>
/// <param name="Attribute">声明这条 UI 的特性，字段由宿主界面自己解读。</param>
/// <param name="ComponentType">宿主界面交给 DynamicComponent 渲染的内容组件。</param>
/// <param name="PluginId">UI 来源插件；不属于任何插件（客户端自带）时为 null。</param>
/// <param name="IsEnabled">当前是否启用。</param>
public sealed record UIRegistration(
    UIAttribute Attribute,
    Type ComponentType,
    string? PluginId,
    bool IsEnabled);

/// <summary>
/// 按 <see cref="UIAttribute"/> 标识统计所有已加载程序集里声明的插拔式 UI，并为每条登记提供开关，从而实现自定义 UI 的替换。
/// 本系统只认 UIAttribute 这个标识：键名、文字、图标、排序这些字段由宿主界面从自己的特性上读，不在这里统一。
/// </summary>
public class UIAttributeSystem
{
    /// <summary>登记集合或开关状态发生变化，已打开的宿主界面据此刷新。</summary>
    public event Action? RegistrationsChanged;

    /// <summary>取全部登记（含已关闭），供界面管理页面展示。</summary>
    public IReadOnlyList<UIRegistration> GetAllRegistrations()
    {
        return Scan();
    }

    /// <summary>取某个宿主界面声明的登记，已过滤为启用项。</summary>
    public IReadOnlyList<UIRegistration> GetEnabledRegistrations<TAttribute>()
        where TAttribute : UIAttribute
    {
        return Scan()
            .Where(registration => registration.Attribute is TAttribute && registration.IsEnabled)
            .ToList();
    }

    /// <summary>写入某条登记的启用状态。</summary>
    public void SetEnabled(UIRegistration registration, bool enabled)
    {
        AlifeConfig.SetBool(SettingKey(registration.Attribute.GetType(), registration.ComponentType), enabled);
        RegistrationsChanged?.Invoke();
    }

    readonly PluginSystem pluginSystem;
    List<DeclaredUI>? declared;

    public UIAttributeSystem(PluginSystem pluginSystem)
    {
        this.pluginSystem = pluginSystem;
        //插件增删重载会改变登记集合，下一次取用时重新扫描
        pluginSystem.PluginSynced += () => declared = null;
    }

    /// <summary>扫描到的原始登记，不含开关状态：开关状态每次读取时按存档换算。</summary>
    readonly record struct DeclaredUI(UIAttribute Attribute, Type ComponentType, string? PluginId);

    /// <summary>
    /// 取当前登记：声明部分有缓存，启用状态每次按存档换算。
    /// </summary>
    List<UIRegistration> Scan()
    {
        declared ??= ScanDeclarations();
        return declared.Select(entry => new UIRegistration(
            entry.Attribute,
            entry.ComponentType,
            entry.PluginId,
            AlifeConfig.GetBool(SettingKey(entry.Attribute.GetType(), entry.ComponentType), true))).ToList();
    }

    List<DeclaredUI> ScanDeclarations()
    {
        List<DeclaredUI> result = new();

        foreach (Type type in TypeUtility.GetAllLoadedTypes())
        {
            UIAttribute[] attributes;
            try
            {
                //单个类型的特性读取可能因签名不匹配而失败（如插件按旧版特性构造签名编译），只跳过该类型
                attributes = type.GetCustomAttributes<UIAttribute>().ToArray();
            }
            catch (Exception e)
            {
                AlifeLog.LogError(new Exception($"读取类型 {type.FullName} 的 UI 特性失败，已跳过该类型。", e));
                continue;
            }

            foreach (UIAttribute attribute in attributes)
                result.Add(new DeclaredUI(
                    attribute,
                    type,
                    pluginSystem.PluginContext.GetTypeAttachedPlugin(type)));
        }

        return result;
    }

    /// <summary>一条登记的身份 = 声明特性的类型 + 内容组件的类型，两者都是稳定名字，插件重载后开关状态仍然对得上。</summary>
    static string SettingKey(Type declarationType, Type componentType)
        => $"UIAttribute:{declarationType.FullName}:{componentType.FullName}";
}