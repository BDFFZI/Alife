using System.Drawing;

namespace Alife.Client;

public sealed record EnvironmentItem(string Id, string Name, bool IsReady, bool IsRequired);

public interface IAlifeClient
{
    /// <summary>
    /// 下载并安装指定版本的客户端更新，完成后重启客户端。
    /// </summary>
    /// <param name="version">目标版本号（不含前导 v）。</param>
    /// <param name="onProgress">下载进度回调，参数为 0-100。</param>
    Task UpgradeClient(string version, Action<int>? onProgress = null);

    bool GetAutoStart();
    void SetAutoStart(bool value);

    /// <summary>
    /// 客户端可能会提供自己的运行环境，而此函数将在 Alife 运行前执行，以便调整
    /// </summary>
    /// <returns></returns>
    Task SetupEnvironment();
    IReadOnlyList<EnvironmentItem> GetEnvironmentItems();
    Task InstallEnvironmentAsync(string id, IProgress<string>? progress = null);

    Task RevealFolderAsync(string path);
    Task RevealFileAsync(string path);
    Task<string?> SelectFolderAsync();

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
}