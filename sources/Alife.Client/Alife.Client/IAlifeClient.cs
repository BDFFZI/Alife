namespace Alife;

public interface IAlifeClient
{
    void Quit();
    void Exit();
    Task<string?> ShowSelectDirectoryDialog();

    /// <summary>
    /// 下载并安装指定版本的客户端更新，完成后重启客户端。
    /// 更新包从哪里取、是什么格式、如何安装，全部由各平台自行处理
    /// （Windows 为 Electron 打包出的 ZIP，其他平台可能是 APK 等）。
    /// </summary>
    /// <param name="version">目标版本号（不含前导 v）。</param>
    /// <param name="onProgress">下载进度回调，参数为 0-100。</param>
    Task UpgradeClient(string version, Action<int>? onProgress = null);
}
