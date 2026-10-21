using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Alife.Foundation;
using Alife.Framework;

namespace Alife.Function.AIModelUtility;

/// <summary>Download ModelScope assets into a cache readable by both the app and its Python process.</summary>
public static class ModelDownloader
{
    static readonly SemaphoreSlim DownloadGate = new(1, 1);
    public static string EnsureModelExisting(IAlifeApplication alifeApplication, string modelId, string? targetFile = null)
        => Task.Run(() => EnsureModelExistingAsync(alifeApplication, modelId, targetFile)).GetAwaiter().GetResult();

    public static async Task<string> EnsureModelExistingAsync(IAlifeApplication alifeApplication, string modelId,
        string? targetFile = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(modelId) || modelId.Split('/').Length != 2 ||
            modelId.Contains("..") || modelId.IndexOfAny(['\\', ':']) >= 0)
            throw new ArgumentException("模型 ID 必须为 owner/name。", nameof(modelId));
        string localPath = Path.Combine(ModelScopeModelPath, modelId.Replace(".", "___"));
        string checkFile = Path.GetFullPath(Path.Combine(localPath, targetFile ?? "README.md"));
        if (!checkFile.StartsWith(Path.GetFullPath(localPath) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new ArgumentException("模型文件必须位于模型目录内。", nameof(targetFile));
        await DownloadGate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(checkFile))
            {
                Directory.CreateDirectory(ModelScopeModelPath);
                string script = Path.Combine(ModelScopeModelPath, $"download_{Guid.NewGuid():N}.py");
                try
                {
                    // local_dir makes the returned layout independent of ModelScope cache-version changes.
                    string patterns = targetFile == null ? "None" : JsonSerializer.Serialize(new[] { targetFile });
                    string code =
                        $"from modelscope import snapshot_download\nsnapshot_download({JsonSerializer.Serialize(modelId)}, local_dir={JsonSerializer.Serialize(localPath)}, allow_patterns={patterns})\n";
                    await File.WriteAllTextAsync(script, code, cancellationToken);
                    await alifeApplication.StartPython(script).WaitCompletion(cancellationToken);
                }
                finally
                {
                    File.Delete(script);
                }
            }
            if (!File.Exists(checkFile))
                throw new FileNotFoundException("模型下载不完整，缺少所需文件。", checkFile);
            return targetFile != null ? checkFile : localPath;
        }
        finally
        {
            DownloadGate.Release();
        }
    }

    public static string ModelScopeModelPath { get; } = Path.Combine(AlifePath.RuntimeFolderPath, "Models");
}