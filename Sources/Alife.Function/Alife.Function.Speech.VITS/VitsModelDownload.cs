using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Alife.Function.Speech.VITS;

internal static class VitsModelDownload
{
    const string Url = "https://github.com/BDFFZI/Alife.OfficialPluginStorage/releases/download/VITS-ONNX/VITS-ONNX.zip";
    const string Sha256 = "1ABB8FC42B62A2B702E072BCF2625A12703AF1D79C2C089437DEA47C83C373C6";
    static readonly SemaphoreSlim Gate = new(1, 1);
    static readonly string[] Files = ["alife-paimon.onnx", "tokens.txt", "lexicon.txt", "speakers_list.txt"];

    public static async Task EnsureAsync(string directory, ILogger logger, CancellationToken cancellationToken)
    {
        await Gate.WaitAsync(cancellationToken);
        string staging = Path.Combine(directory, ".download-" + Guid.NewGuid().ToString("N"));
        try
        {
            if (VitsSpeechModel.IsModelReady(new() { ModelDirectory = directory })) return;
            Directory.CreateDirectory(staging);
            logger.LogInformation("正在下载 VITS ONNX 官方模型（约 108 MB），首次使用需等待下载完成。");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(30));
            using var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            string zipPath = Path.Combine(staging, "model.zip");
            using (var response = await client.GetAsync(Url, HttpCompletionOption.ResponseHeadersRead, timeout.Token))
            {
                response.EnsureSuccessStatusCode();
                await using var input = await response.Content.ReadAsStreamAsync(timeout.Token);
                await using var output = File.Create(zipPath);
                await input.CopyToAsync(output, timeout.Token);
            }
            await using (var input = File.OpenRead(zipPath))
            {
                string actual = Convert.ToHexString(await SHA256.HashDataAsync(input, timeout.Token));
                if (actual != Sha256) throw new InvalidDataException("VITS 模型包 SHA256 校验失败，请重试下载。");
            }
            using (var archive = ZipFile.OpenRead(zipPath))
            {
                foreach (string name in Files)
                {
                    var entry = archive.GetEntry("VITS/onnx/" + name)
                        ?? throw new InvalidDataException("VITS 模型包缺少 " + name);
                    await using var input = entry.Open();
                    await using var output = File.Create(Path.Combine(staging, name));
                    await input.CopyToAsync(output, timeout.Token);
                }
            }
            timeout.Token.ThrowIfCancellationRequested();
            // Publish only a fully downloaded and verified set; cancellation never leaves partial files.
            foreach (string name in Files) File.Move(Path.Combine(staging, name), Path.Combine(directory, name), overwrite: true);
            logger.LogInformation("VITS ONNX 模型已下载并校验完成。");
        }
        finally
        {
            try { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); }
            finally { Gate.Release(); }
        }
    }
}
