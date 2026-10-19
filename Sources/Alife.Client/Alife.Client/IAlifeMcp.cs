namespace Alife.Framework;

/// <summary>
/// 实现了 AlifeMcpTools 的 Mcp 服务器
/// </summary>
public interface IAlifeMcp
{
    Uri? Server { get; }

    Task StartAsync(string address = "http://127.0.0.1:18765", string path = "/mcp");
}