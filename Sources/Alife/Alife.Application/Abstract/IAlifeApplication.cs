using System.Drawing;
using Alife.Client;

namespace Alife.Framework;

public interface IAlifeApplication
{
    #region AlifeMcp功能

    Uri? AlifeMcpServer { get; }

    Task StartAlifeMcpAsync(IServiceProvider provider);

    /// <summary>
    /// 停止 MCP 服务并释放监听端口。
    /// 应用退出前必须调用，否则进程看似退出、端口仍被后台占用，
    /// 下一次启动时插件/客户端会因端口冲突而表现为「关不干净」。
    /// </summary>
    Task StopAlifeMcpAsync();

    #endregion

    #region 文件对话框功能

    Task RevealFolderAsync(string path);
    Task RevealFileAsync(string path);
    Task<string?> SelectFolderAsync();
    Task<string?> SelectFileAsync();

    #endregion

    #region 自定义窗口功能

    /// <summary>
    /// 主显示器大小（物理像素）
    /// </summary>
    Size GetScreenSize();
    /// <summary>
    /// 光标屏幕坐标（物理像素）
    /// </summary>
    Point GetCursorPosition();
    float GetDpiScale();
    /// <summary>
    /// 创建额外的自定义窗口
    /// </summary>
    /// <param name="options"></param>
    /// <returns></returns>
    Task<IAlifeWindow> CreateWindowAsync(AlifeWindowOptions options);

    #endregion

    #region Python执行功能

    /// <summary>启动一个 Python 脚本进程。</summary>
    /// <param name="scriptPath">脚本路径。</param>
    /// <param name="pythonExe">可选的 Python 可执行文件；Android 仅支持设备内置 Python。</param>
    IAlifeProcess StartPython(string scriptPath, string? pythonExe = null);

    #endregion
}