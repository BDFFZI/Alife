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
        AudioContent audioContent = new() {
            Data = File.ReadAllBytes(file),
            Format = GetFormat(file)
        };
        return audioContent;

        static string GetFormat(string path) => Path.GetExtension(path).ToLowerInvariant() switch {
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
}