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
}

/// <summary>
/// 一个特殊回调，其发生在系统初始化前，但UI进度条显示时，可借此实现一些用户友好的额外的初始化工作
/// </summary>
public interface IAlifeClientInitialize
{
    Task OnInitializeAsync(IProgress<string>? progress);
}

/// <summary>
/// 一个特殊回调，其发生在系统初始化后，但UI进度条消失前，可借此实现一些用户友好的额外的初始化后工作
/// </summary>
public interface IAlifeClientInitialized
{
    Task OnInitializedAsync(IProgress<string>? progress);
}