using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using Alife.Foundation;

namespace Alife.Function.Auditory;

/// <summary>
/// 音频解码工具：将任意音频格式（wav/mp3/amr/m4a 等）以及微信/QQ语音（SILK v3）解码为 16kHz 单声道 float 采样序列。
/// 普通格式借助 ffmpeg（优先 python imageio-ffmpeg 捆绑二进制，其次系统 PATH）；SILK 借助 silk_v3_decoder（运行时下载）。
/// </summary>
public static class AudioDecoder
{
    /// <summary>silk_v3_decoder.exe 固定下载源（kn007/silk-v3-decoder，MIT，支持微信/QQ语音）</summary>
    const string SilkDecoderUrl = "https://raw.githubusercontent.com/kn007/silk-v3-decoder/master/windows/silk_v3_decoder.exe";
    /// <summary>silk_v3_decoder.exe 的 SHA256（179,037 字节）</summary>
    const string SilkDecoderSha256 = "AFE908FDF8BB5DDC3566CAEF224A365159A6216E517D8A915DB50CE5ECF86D1B";
    static readonly Lock SilkDecoderLock = new();

    static string SilkDecoderPath => Path.Combine(AlifePath.RuntimeFolderPath, "Tools", "silk_v3_decoder.exe");

    static readonly byte[] SilkMagic = "#!SILK_V3"u8.ToArray();

    public static float[] DecodeFileTo16kMonoFloat(string filePath)
    {
        if (File.Exists(filePath) == false)
            throw new FileNotFoundException($"音频文件不存在: {filePath}");

        //SILK（微信/QQ 语音）走专用解码器
        if (IsSilkFile(filePath))
            return DecodeSilkTo16kMonoFloat(filePath);

        //其余格式走 ffmpeg
        return DecodeByFfmpeg(filePath);
    }

    /// <summary>
    /// 判断是否为 SILK v3 文件。支持标准头（#!SILK_V3$）与 QQ/Tencent 变体头（0x02 #!SILK_V3$）。
    /// </summary>
    static bool IsSilkFile(string filePath)
    {
        try
        {
            Span<byte> head = stackalloc byte[16];
            int read;
            using (FileStream fs = File.OpenRead(filePath))
                read = fs.Read(head);

            //标准：开头直接是 #!SILK_V3
            if (read >= 9 && head[..9].SequenceEqual(SilkMagic))
                return true;
            //QQ/Tencent 变体：0x02 + #!SILK_V3
            if (read >= 10 && head[0] == 0x02 && head[1..10].SequenceEqual(SilkMagic))
                return true;
            return false;
        }
        catch
        {
            return false;
        }
    }

    static float[] DecodeSilkTo16kMonoFloat(string filePath)
    {
        string decoder = EnsureSilkDecoder();
        string pcmPath = Path.Combine(Path.GetTempPath(), $"alife_silk_{Guid.NewGuid():N}.pcm");

        string args = $"\"{filePath}\" \"{pcmPath}\" -Fs_API 16000 -quiet";
        ProcessStartInfo psi = new(decoder, args) {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using Process process = Process.Start(psi)!;
        string output = process.StandardOutput.ReadToEnd();
        string err = process.StandardError.ReadToEnd();
        process.WaitForExit();

        try
        {
            if (File.Exists(pcmPath) == false || new FileInfo(pcmPath).Length == 0)
                throw new Exception($"SILK 解码失败（这是微信/QQ语音(SILK)格式，需用专用解码器）:\n{output}\n{err}");

            byte[] bytes = File.ReadAllBytes(pcmPath);
            if (bytes.Length % 2 != 0)
                throw new Exception($"SILK 解码结果异常，长度 {bytes.Length}");

            float[] samples = new float[bytes.Length / 2];
            for (int i = 0; i < samples.Length; i++)
                samples[i] = BitConverter.ToInt16(bytes, i * 2) / 32768f;
            return samples;
        }
        finally
        {
            try { File.Delete(pcmPath); } catch { }
        }
    }

    /// <summary>
    /// 确保 silk_v3_decoder.exe 已下载到 Runtime\Tools 目录（懒下载 + SHA256 校验 + 缓存）。
    /// </summary>
    static string EnsureSilkDecoder()
    {
        string path = SilkDecoderPath;
        if (File.Exists(path))
            return path;

        lock (SilkDecoderLock)
        {
            if (File.Exists(path))
                return path;

            string tmp = path + ".tmp";
            try
            {
                AlifeUtility.DownloadFileAsync(SilkDecoderUrl, tmp).GetAwaiter().GetResult();
                byte[] hashBytes = SHA256.HashData(File.ReadAllBytes(tmp));
                string hash = Convert.ToHexString(hashBytes);
                if (string.Equals(hash, SilkDecoderSha256, StringComparison.OrdinalIgnoreCase) == false)
                    throw new Exception($"SILK 解码器校验失败（SHA256 不符: {hash}），为安全起见已中止");

                File.Move(tmp, path);
            }
            catch (Exception e)
            {
                try { File.Delete(tmp); } catch { }
                throw new Exception($"SILK 解码器下载失败: {e.Message}", e);
            }

            return path;
        }
    }

    static float[] DecodeByFfmpeg(string filePath)
    {
        string ffmpeg = FindFfmpeg();
        string output = Path.Combine(Path.GetTempPath(), $"alife_decode_{Guid.NewGuid():N}.f32");

        string args = $"-y -i \"{filePath}\" -ar 16000 -ac 1 -f f32le \"{output}\"";
        ProcessStartInfo psi = new(ffmpeg, args) {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using Process process = Process.Start(psi)!;
        string err = process.StandardError.ReadToEnd();
        process.WaitForExit();

        try
        {
            if (process.ExitCode != 0)
            {
                FileInfo info = new(filePath);
                throw new Exception(
                    $"ffmpeg 解码失败: {filePath}（大小 {info.Length} 字节）\n" +
                    $"ffmpeg: {ffmpeg}\n{err}");
            }

            byte[] bytes = File.ReadAllBytes(output);
            if (bytes.Length == 0 || bytes.Length % 4 != 0)
                throw new Exception($"ffmpeg 解码结果异常，长度 {bytes.Length}");

            float[] samples = new float[bytes.Length / 4];
            Buffer.BlockCopy(bytes, 0, samples, 0, bytes.Length);
            return samples;
        }
        finally
        {
            try { File.Delete(output); } catch { }
        }
    }

    public static string FindFfmpeg()
    {
        //1. 优先使用 python imageio-ffmpeg 捆绑的 ffmpeg
        try
        {
            ProcessStartInfo psi = new("python", "-c \"import imageio_ffmpeg; print(imageio_ffmpeg.get_ffmpeg_exe())\"") {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using Process process = Process.Start(psi)!;
            string? output = process.StandardOutput.ReadLine()?.Trim();
            process.WaitForExit(15000);
            if (process.ExitCode == 0 && !string.IsNullOrEmpty(output) && File.Exists(output))
                return output;
        }
        catch { }

        //2. 搜索 PATH
        string? pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(pathEnv))
        {
            foreach (string dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    string candidate = Path.Combine(dir.Trim(), "ffmpeg.exe");
                    if (File.Exists(candidate))
                        return candidate;
                }
                catch { }
            }
        }

        //3. 常见安装位置兜底
        string[] roots = [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WinGet", "Packages"),
            @"C:\ffmpeg\bin",
            @"C:\Program Files\ffmpeg\bin",
            @"C:\tools\ffmpeg\bin",
        ];
        foreach (string root in roots)
        {
            try
            {
                if (Directory.Exists(root) == false)
                    continue;
                string? hit = Directory.EnumerateFiles(root, "ffmpeg.exe", SearchOption.AllDirectories).FirstOrDefault();
                if (hit != null)
                    return hit;
            }
            catch { }
        }

        throw new Exception("未找到 ffmpeg。请安装 python 的 imageio-ffmpeg 包（插件会自动安装），或在系统安装 ffmpeg。");
    }
}
