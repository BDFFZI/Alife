using System;
using System.IO;
using System.Text.Json.Nodes;
using Alife.Framework;
using Alife.Function.FunctionCaller;
using Microsoft.SemanticKernel;

namespace Alife.Function.Language.OpenAI;

public sealed class AudioContent : KernelContent
{
    public required byte[] Data { get; init; }
    public required string Format { get; init; }
}

public sealed class AudioContentRegistrar : AlifeContentRegistrar
{
    public override string DisplayName => "音频输入";
    public override string ProtocolTypeName => "input_audio";
    public override Type ContentType => typeof(AudioContent);


    public override JsonObject SerializeContent(KernelContent content)
    {
        AudioContent audio = (AudioContent)content;
        return new JsonObject {
            ["type"] = "input_audio",
            ["input_audio"] = new JsonObject {
                ["data"] = Convert.ToBase64String(audio.Data),
                ["format"] = audio.Format
            }
        };
    }

    public override XmlFunction[] AttachedFunction(ChatBot chatBot, bool allowPersistent)
    {
        return [AlifeContentUtility.BuildXmlFunction("LoadAudio", AudioFileToContentAsync, chatBot, true)];
    }

    static KernelContent AudioFileToContentAsync(string file)
    {
        byte[] bytes = File.ReadAllBytes(file);
        AudioContent audioContent = new() {
            Data = bytes,
            Format = GetFormat(file, bytes)
        };
        return audioContent;

        static string GetFormat(string path, byte[] data)
        {
            string? sniffed = SniffAudioFormat(data);
            if (sniffed != null)
                return sniffed;

            return Path.GetExtension(path).ToLowerInvariant() switch {
                ".mp3" => "mp3",
                ".wav" => "wav",
                ".m4a" => "m4a",
                ".flac" => "flac",
                ".ogg" or ".opus" => "ogg",
                ".aac" => "aac",
                ".webm" => "webm",
                _ => "mp3"
            };
        }

        static string? SniffAudioFormat(byte[] data)
        {
            if (data.Length >= 12 && data[0] == 0x52 && data[1] == 0x49 && data[2] == 0x46 && data[3] == 0x46
                && data[8] == 0x57 && data[9] == 0x41 && data[10] == 0x56 && data[11] == 0x45)
                return "wav";
            if (data.Length >= 4 && data[0] == 0x66 && data[1] == 0x4C && data[2] == 0x61 && data[3] == 0x43)
                return "flac";
            if (data.Length >= 4 && data[0] == 0x4F && data[1] == 0x67 && data[2] == 0x67 && data[3] == 0x53)
                return "ogg";
            return null;
        }
    }
}