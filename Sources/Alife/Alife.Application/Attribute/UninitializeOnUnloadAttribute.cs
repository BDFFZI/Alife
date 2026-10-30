namespace Alife.Framework;

/// <summary>
/// 标在公开静态函数上，插件卸载（含重载前的旧版本）时由 Application 扫描该插件的加载上下文并调用，
/// 与 <see cref="InitializeOnLoadAttribute"/> 成对：用来摘掉加载时挂上的全局订阅，避免已卸载的插件继续响应事件。
/// 参数按类型从容器解析；返回 Task 的会被等待。
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class UninitializeOnUnloadAttribute : Attribute { }
