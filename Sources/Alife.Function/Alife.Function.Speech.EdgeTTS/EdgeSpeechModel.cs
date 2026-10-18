using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Alife.Framework;
using Alife.Foundation;
using Alife.Function.AIModelUtility;
#if ANDROID
using Alife.Client.Android;
#endif

namespace Alife.Function.Speech.EdgeTTS;

[Module("Edge语音合成",
    "基于Edge-TTS的在线语音合成引擎",
    defaultCategory: "Alife 官方/模型接入/语音模型",
    EditorUI = typeof(EdgeSpeechModelUI))]
public class EdgeSpeechModel :
    ISpeechModel,
    IConfigurable<EdgeSpeechModelConfig>
{
    public EdgeSpeechModelConfig Configuration { get; set; } = null!;

    public async Task<string?> GenerateSpeechFileAsync(string text, CancellationToken cancellationToken = default)
    {
        string tempFolder = GetTempFolder();
        Directory.CreateDirectory(tempFolder);

        string hash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(text + "\n" + Configuration.VoiceTone)))[..16];
        string outputPath = Path.Combine(tempFolder, $"tts_{hash}.mp3");
        if (File.Exists(outputPath))
            return outputPath;

        // 使用 Python 脚本调用 edge_tts，避免 aiohttp 的 DNS 问题
        string scriptPath = Path.Combine(tempFolder, $"tts_{hash}.py");
        await File.WriteAllTextAsync(scriptPath, BuildScript(text, outputPath), cancellationToken);

#if ANDROID
        try
        {
            string command = $"""
                              export HOME={ShizukuBridge.PythonRoot}
                              export TMPDIR={ShizukuBridge.PythonRoot}/tmp
                              export PYTHONHOME={ShizukuBridge.PythonUsr}
                              export LD_LIBRARY_PATH={ShizukuBridge.PythonUsr}/lib
                              export PATH={ShizukuBridge.PythonUsr}/bin:$PATH
                              export SSL_CERT_FILE={ShizukuBridge.PythonUsr}/etc/tls/cert.pem
                              mkdir -p {ShizukuBridge.PythonRoot}/tmp
                              cd {ShizukuBridge.PythonRoot}
                              ./usr/bin/python "{scriptPath}" 2>&1
                              """;
            ShizukuShellResult result = await ShizukuBridge.ExecuteAsync(command, cancellationToken);
            if (result.ExitCode != 0)
                throw new Exception($"EdgeTTS 执行失败：{result.Output}");
            if (File.Exists(outputPath) == false)
                throw new Exception($"语音文件未生成：{outputPath}");
            return outputPath;
        }
        finally
        {
            if (File.Exists(scriptPath))
                File.Delete(scriptPath);
        }
#else
        ProcessStartInfo psi = new("python") {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add(scriptPath);

        using Process? process = Process.Start(psi);
        if (process == null)
            return null;

        try
        {
            await Task.WhenAny(
                process.WaitForExitAsync(cancellationToken),
                Task.Delay(5000, cancellationToken)
            );
            if (process.HasExited == false)
                throw new TimeoutException();
            if (process.ExitCode != 0)
                throw new Exception(
                    $"{outputPath}\n{await process.StandardOutput.ReadToEndAsync(cancellationToken)}\n{await process.StandardError.ReadToEndAsync(cancellationToken)}"
                );
            if (File.Exists(outputPath) == false)
                throw new Exception($"语音文件未生成：{outputPath}");

            return outputPath;
        }
        catch (TimeoutException)
        {
            return null;
        }
        finally
        {
            if (process.HasExited == false)
                process.Kill();
            if (File.Exists(scriptPath))
                File.Delete(scriptPath);
        }
#endif
    }

    string BuildScript(string text, string outputPath)
    {
        string textJson = JsonSerializer.Serialize(text);
        string voiceJson = JsonSerializer.Serialize(Configuration.VoiceTone);
        string outputJson = JsonSerializer.Serialize(outputPath);
        return $"""
                import asyncio
                import aiohttp
                from aiohttp.resolver import ThreadedResolver
                from edge_tts import Communicate

                async def main():
                    connector = aiohttp.TCPConnector(resolver=ThreadedResolver())
                    communicate = Communicate({textJson}, {voiceJson}, connector=connector)
                    await communicate.save({outputJson})

                asyncio.run(main())
                """;
    }

    static string GetTempFolder()
    {
#if ANDROID
        return Path.Combine(AlifePath.StorageFolderPath, "Temp");
#else
        return AlifePath.TempFolderPath;
#endif
    }
}
