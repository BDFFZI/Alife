namespace Alife.Framework;

/// <summary>
/// 标在公开静态函数上，由 Application 扫描并调用：内置代码在系统初始化完成后调用，插件在其加载或重载完成后立刻调用。
/// 这是插件侧唯一的进程级入口：需要常驻挂接（订阅全局事件、维护跨界面状态）时用，而不是要求别人改客户端。
/// 参数按类型从容器解析；返回 Task 的会被等待。
/// 插件卸载（含重载前的旧版本）时对应的清理函数标 <see cref="UninitializeOnUnloadAttribute"/>。
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class InitializeOnLoadAttribute : Attribute { }
