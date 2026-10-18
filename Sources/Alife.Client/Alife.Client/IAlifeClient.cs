using System.Drawing;

namespace Alife.Client;

public sealed record EnvironmentItem(string Id, string Name, bool IsReady, bool IsRequired);

public interface IAlifeClient
{
    #region 客户端更新功能

    /// <summary>
    /// 下载并安装指定版本的客户端更新，完成后重启客户端。
    /// </summary>
    /// <param name="version">目标版本号，不含前缀 v</param>
    /// <param name="onProgress">下载进度回调，参数为 0-100</param>
    Task UpgradeClient(string version, Action<int>? onProgress = null);

    #endregion

    #region 自定义路径功能

    void SetStorageFolderPath(string path);
    void SetRuntimeFolderPath(string path);

    #endregion

    #region 开机自启功能

    bool GetAutoStart();
    void SetAutoStart(bool value);

    #endregion

    #region 环境安装功能

    IReadOnlyList<EnvironmentItem> GetEnvironmentItems();
    Task InstallEnvironmentAsync(string id, IProgress<string>? progress = null);

    #endregion

    #region 文件对话框功能

    Task RevealFolderAsync(string path);
    Task RevealFileAsync(string path);
    Task<string?> SelectFolderAsync();

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
}