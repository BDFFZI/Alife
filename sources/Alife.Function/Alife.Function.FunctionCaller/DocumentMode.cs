namespace Alife.Function.FunctionCaller;

/// <summary>
/// 函数的文档注入模式，XmlFunctionCaller 与 McpFunctionCaller 共用：
/// <list type="bullet">
/// <item><see cref="None"/>：不注入文档。</item>
/// <item><see cref="Implicit"/>：渐进式加载，AI 调用入口时自动注入对应文档。</item>
/// <item><see cref="Explicit"/>：文档常驻注入，AI 始终可见。</item>
/// </list>
/// </summary>
public enum DocumentMode
{
    None,
    Implicit,
    Explicit,
}
