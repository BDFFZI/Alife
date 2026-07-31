using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace Alife.Platform;

public struct CommandResult
{
    public int ExitCode { get; init; }
    public string StandardOutput { get; init; }
    public string StandardError { get; init; }
}

public static class AlifeUtility
{
    /// <summary>
    /// 通过 HttpClient 获取报文内容字符串
    /// </summary>
    /// <param name="url">请求地址</param>
    /// <param name="timeout">超时时间，默认 30 秒</param>
    /// <returns>响应内容字符串</returns>
    public static async Task<string> FetchStringAsync(string url, TimeSpan? timeout = null)
    {
        //应用镜像替换
        url = AlifeMirror.TransformUrl(url);

        //伪装用户
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

        //设置超时
        using var httpClient = timeout.HasValue ? new HttpClient { Timeout = timeout.Value } : new HttpClient();
        using var response = await httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsStringAsync();
    }
    /// <summary>
    /// 通过 HttpClient 下载文件到本地
    /// </summary>
    /// <param name="url">下载地址</param>
    /// <param name="savePath">保存路径</param>
    /// <param name="progress">进度回调，参数为已下载字节数和总字节数</param>
    /// <param name="timeout">超时时间，默认 100 秒</param>
    public static async Task DownloadFileAsync(string url, string savePath, Action<long, long>? progress = null, TimeSpan? timeout = null)
    {
        //应用镜像替换
        url = AlifeMirror.TransformUrl(url);

        //伪装用户
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        if (url.Contains("multimedia.nt.qq.com.cn") || url.Contains("qpic.cn"))
            request.Headers.Add("Referer", "https://q.qq.com/");

        //设置超时
        using var httpClient = timeout.HasValue ? new HttpClient { Timeout = timeout.Value } : new HttpClient();
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        //获取总大小
        long totalBytes = response.Content.Headers.ContentLength ?? -1;

        //创建目录
        string? dir = Path.GetDirectoryName(savePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        //下载文件并报告进度
        await using var contentStream = await response.Content.ReadAsStreamAsync();
        await using var fileStream = new FileStream(savePath, FileMode.Create, FileAccess.Write, FileShare.None);

        byte[] buffer = new byte[81920];
        long readSoFar = 0;
        int bytesRead;
        long nextReport = 0;

        while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
        {
            await fileStream.WriteAsync(buffer, 0, bytesRead);
            readSoFar += bytesRead;

            if (progress != null && readSoFar >= nextReport)
            {
                progress(readSoFar, totalBytes);
                nextReport = readSoFar + 10 * 1024 * 1024; // 每 10MB 报告一次
            }
        }

        //最终进度报告
        progress?.Invoke(readSoFar, totalBytes);
    }
    public static async Task DownloadZipFileAsync(string url, string rootPath)
    {
        if (Directory.Exists(rootPath) == false)
            Directory.CreateDirectory(rootPath);

        string zipPath = Path.Combine(rootPath, "temp.zip");
        await DownloadFileAsync(url, zipPath);

        await ZipFile.ExtractToDirectoryAsync(zipPath, rootPath, overwriteFiles: true);
        File.Delete(zipPath);
    }

    public static CommandResult Command(string fileName, string arguments)
    {
        ProcessStartInfo psi = new()
        {
            FileName = "cmd.exe",
            Arguments = $"/c chcp 65001 > nul && {fileName} {arguments}",
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        using Process? process = Process.Start(psi);
        if (process == null)
            throw new Exception("进程不存在，无法启动。");

        StringBuilder stdoutBuilder = new();
        StringBuilder stderrBuilder = new();
        process.OutputDataReceived += (_, e) =>
        {
            Console.WriteLine(e.Data);
            stdoutBuilder.AppendLine(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            Console.WriteLine(e.Data);
            stderrBuilder.AppendLine(e.Data);
        };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.WaitForExit();

        CommandResult commandResult = new()
        {
            ExitCode = process.ExitCode,
            StandardOutput = stdoutBuilder.ToString().Trim(),
            StandardError = stderrBuilder.ToString().Trim(),
        };

        return commandResult;
    }
}