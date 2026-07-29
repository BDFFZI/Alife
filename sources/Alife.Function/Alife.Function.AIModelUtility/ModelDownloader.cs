using System;
using System.IO;
using Alife.Platform;

namespace Alife.Function.AIModelUtility;

/// <summary>
/// 通用的模型与资源下载引导器。
/// 负责检测文件完整性并调用独立的 WPF 下载器窗口。
/// </summary>
public static class ModelDownloader
{
    public static string EnsureModelExisting(string modelId, string? targetFile = null)
    {
        string localPath = Path.Combine(ModelScopeModelPath, modelId.Replace(".", "___"));
        string checkFile = Path.Combine(localPath, targetFile ?? "README.md");

        AlifeUtility.Command("python", $"-c \"from modelscope import snapshot_download; snapshot_download('{modelId}')\"");

        if (!File.Exists(checkFile))
            throw new DirectoryNotFoundException($"模型下载失败，目录不完整（{localPath}），请尝试删除后重试");

        return targetFile != null ? checkFile : localPath;
    }

    public static string ModelScopeModelPath { get; }

    static ModelDownloader()
    {
        string modelScopeCachePath = Environment.GetEnvironmentVariable("MODELSCOPE_CACHE") ??
                                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "modelscope", "hub");

        try
        {
            string lockFile = Path.Combine(modelScopeCachePath, ".lock");
            if (Directory.Exists(lockFile))
                Directory.Delete(lockFile, true);
        }
        catch (Exception ex)
        {
            throw new Exception("检测到模型下载任务中断或冲突，请重启电脑后继续\n" + ex);
        }

        ModelScopeModelPath = Path.Combine(modelScopeCachePath, "models").Replace(Path.DirectorySeparatorChar, '/');
    }
}
