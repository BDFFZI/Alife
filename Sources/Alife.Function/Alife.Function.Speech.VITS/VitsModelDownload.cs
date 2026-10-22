using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Alife.Foundation;
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
            using (var response = await client.GetAsync(AlifeMirror.TransformUrl(Url), HttpCompletionOption.ResponseHeadersRead, timeout.Token))
            {
                response.EnsureSuccessStatusCode();
                long totalBytes = response.Content.Headers.ContentLength ?? -1;
                await using var input = await response.Content.ReadAsStreamAsync(timeout.Token);
                await using var output = File.Create(zipPath);
                byte[] buffer = new byte[81920];
                long downloaded = 0;
                long lastDownloaded = 0;
                long nextReport = 0;
                int lastPercent = -1;
                var stopwatch = Stopwatch.StartNew();
                TimeSpan lastReportTime = TimeSpan.Zero;
                int bytesRead;
                while ((bytesRead = await input.ReadAsync(buffer, 0, buffer.Length, timeout.Token)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, bytesRead), timeout.Token);
                    downloaded += bytesRead;
                    TimeSpan elapsed = stopwatch.Elapsed;
                    if (downloaded < nextReport && elapsed - lastReportTime < TimeSpan.FromSeconds(1) &&
                        (totalBytes <= 0 || downloaded < totalBytes))
                        continue;

                    TimeSpan interval = elapsed - lastReportTime;
                    LogProgress(logger, downloaded, lastDownloaded, totalBytes, interval, ref lastPercent);
                    lastDownloaded = downloaded;
                    lastReportTime = elapsed;
                    nextReport = downloaded + 1024 * 1024;
                }
                if (lastPercent < 100)
                    LogProgress(logger, downloaded, lastDownloaded, totalBytes, stopwatch.Elapsed - lastReportTime, ref lastPercent);
            }
            logger.LogInformation("VITS 模型下载完成，正在校验 SHA256...");
            await using (var input = File.OpenRead(zipPath))
            {
                string actual = Convert.ToHexString(await SHA256.HashDataAsync(input, timeout.Token));
                if (actual != Sha256) throw new InvalidDataException("VITS 模型包 SHA256 校验失败，请重试下载。");
            }
            logger.LogInformation("VITS 模型校验通过，正在解压...");
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

    static void LogProgress(ILogger logger, long downloaded, long lastDownloaded, long totalBytes, TimeSpan interval, ref int lastPercent)
    {
        double downloadedMb = ToMegabytes(downloaded);
        double speedMb = interval.TotalSeconds > 0 ? ToMegabytes(downloaded - lastDownloaded) / interval.TotalSeconds : 0;
        if (totalBytes <= 0)
        {
            logger.LogInformation("VITS 模型已下载 {Downloaded:F1} MB（{Speed:F1} MB/s）...", downloadedMb, speedMb);
            return;
        }

        int percent = (int)Math.Min(100, downloaded * 100.0 / totalBytes);
        if (percent == lastPercent && downloaded < totalBytes) return;
        lastPercent = percent;
        logger.LogInformation("VITS 模型下载 {Percent}%（{Downloaded:F1}/{Total:F1} MB，{Speed:F1} MB/s）",
            percent, downloadedMb, ToMegabytes(totalBytes), speedMb);
    }

    static double ToMegabytes(long bytes) => bytes / (1024d * 1024d);
}
