using System.Drawing;

namespace Alife.Client;

public class AlifeWindowOptions
{
    public required string Title { get; init; }
    public required string Url { get; init; }
    public Rectangle? Bounds { get; init; }
    public bool TopMost { get; init; } //是否置于其他窗口顶部？如果需要再任务栏之上还需要每次都设置一下
    public bool Resizable { get; init; } = true;
    public bool Transparent { get; init; } //影响窗口绘制模式，边框样式等
}

/// <summary>
/// 窗口采用 dpi 坐标，可以通过 <see cref="Alife.Framework.IAlifeApplication.GetDpiScale" /> 来与物理坐标转换
/// </summary>
public interface IAlifeWindow : IAsyncDisposable
{
    /// <summary>实际内容区域变化（DPI 逻辑坐标），包括平台限制、旋转导致的位置/尺寸修正。</summary>
    event Action<Rectangle>? BoundsChanged { add { } remove { } }

    Task Launch();

    Task ShowAsync();
    Task HideAsync();

    Task SetBoundsAsync(Rectangle bounds) => throw new NotSupportedException();
    Task SetTopMostAsync(bool topMost) => throw new NotSupportedException();
    /// <summary>
    /// 设置接收鼠标/触摸输入的区域，采用客户区内的 DPI 逻辑坐标。
    /// null 表示整窗接收输入，空数组表示整窗穿透，否则仅指定区域接收输入。
    /// </summary>
    Task SetMouseInputRegionsAsync(Rectangle[]? regions) => throw new NotSupportedException();

    /// <summary>
    /// 接受前端消息（需让前端支持用 window.alifeWindow.postMessage(obj | string) 发消息）
    /// </summary>
    event Action<string>? MessageReceived;
    /// <summary>
    /// 向前端发送消息（需让前端支持用 window.alifeWindow.onMessage 注册消息处理器 handler(obj)）
    /// </summary>
    /// <param name="messageJson"></param>
    /// <returns></returns>
    /// <exception cref="NotSupportedException"></exception>
    Task PostMessageAsync(string messageJson) => throw new NotSupportedException();
    Task<string?> ExecuteScriptAsync(string script) => throw new NotSupportedException();
}
