namespace Alife;

public interface IAlifeClient
{
    void Quit();
    void Exit();
    Task<string?> ShowSelectDirectoryDialog();

    bool GetAutoStart();
    void SetAutoStart(bool value);

    /// <summary>
    /// 下载并安装指定版本的客户端更新，完成后重启客户端。
    /// </summary>
    /// <param name="version">目标版本号（不含前导 v）。</param>
    /// <param name="onProgress">下载进度回调，参数为 0-100。</param>
    Task UpgradeClient(string version, Action<int>? onProgress = null);
}