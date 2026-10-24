using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Alife.Client;
using Alife.Framework;
using Alife.Foundation;

namespace Alife.Function.Speech.EdgeTTS;

[Module("Edge语音合成",
    "基于Edge-TTS的在线语音合成引擎",
    defaultCategory: "Alife 官方/模型接入/语音模型",
    EditorUI = typeof(EdgeSpeechModelUI))]
public class EdgeSpeechModel(IAlifeApplication alifeApplication) :
    ISpeechModel,
    IConfigurable<EdgeSpeechModelConfig>
{
    public EdgeSpeechModelConfig Configuration { get; set; } = null!;

    public async Task<string?> GenerateSpeechFileAsync(string text, CancellationToken cancellationToken = default)
    {
        string hash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(text + "\n" + Configuration.VoiceTone)))[..16];
        string outputPath = Path.Combine(AlifePath.TempFolderPath, $"tts_{hash}.mp3");
        if (File.Exists(outputPath))
            return outputPath;

        // 使用 Python 脚本调用 edge_tts，避免 aiohttp 的 DNS 问题
        string scriptPath = Path.Combine(AlifePath.TempFolderPath, $"tts_{hash}.py");
        await File.WriteAllTextAsync(scriptPath, BuildScript(text, outputPath), cancellationToken);

        try
        {
            using IAlifeProcess process = alifeApplication.StartPython(scriptPath);
            AlifeProcessResult result = await process.WaitCompletion(cancellationToken);
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
}