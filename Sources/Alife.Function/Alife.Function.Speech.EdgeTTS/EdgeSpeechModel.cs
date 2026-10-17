using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Alife.Framework;
using Alife.Foundation;
using Alife.Function.AIModelUtility;

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
        //计算输出位置
        string fileSafeText = string.Concat(text.Where(ch => invalidChars.Contains(ch) == false));
        if (string.IsNullOrWhiteSpace(fileSafeText))
            return null;
        string outputPath = Path.Combine(AlifePath.TempFolderPath, fileSafeText + ".mp3");
        if (File.Exists(outputPath))
            return outputPath;

        // 使用 Python 脚本调用 edge_tts，避免 aiohttp 的 DNS 问题
        string scriptPath = Path.Combine(AlifePath.TempFolderPath, "edge_tts_script.py");
        string escapedText = text.Replace("\"", "\\\"");
        string escapedOutput = outputPath.Replace("\\", "\\\\");
        string scriptContent = $@"
import asyncio
import aiohttp
from aiohttp.resolver import ThreadedResolver
from edge_tts import Communicate

async def main():
    connector = aiohttp.TCPConnector(resolver=ThreadedResolver())
    communicate = Communicate('{escapedText}', '{Configuration.VoiceTone}', connector=connector)
    await communicate.save('{escapedOutput}')

asyncio.run(main())
";
        await File.WriteAllTextAsync(scriptPath, scriptContent, cancellationToken);

        ProcessStartInfo psi = new() {
            FileName = "python",
            Arguments = $"\"{scriptPath}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

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
    }

    readonly char[] invalidChars = Path.GetInvalidFileNameChars();
}
