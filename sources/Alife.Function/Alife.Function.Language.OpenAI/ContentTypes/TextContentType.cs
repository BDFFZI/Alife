using System;
using System.Text.Json.Nodes;
using Alife.Framework;
using Alife.Function.FunctionCaller;
using Microsoft.SemanticKernel;

namespace Alife.Function.Language.OpenAI;

/// <summary>文本内容：协议序列化为 <c>text</c>。文本由对话本身承载，无需暴露 AI 上传函数。</summary>
public sealed class TextContentType : AlifeContentHandlerBase
{
    public override Type ContentType => typeof(TextContent);
    public override string? RegistrationKey => null;
    public override string DisplayName => "文本";
    public override string ProtocolTypeName => "text";

    public override JsonObject SerializeContent(KernelContent content)
    {
        TextContent text = (TextContent)content;
        return new JsonObject { ["type"] = "text", ["text"] = text.Text };
    }

    public override XmlFunction? CreateXmlFunction(ChatBot chatBot, OpenAILanguageModelConfig config)
    {
        return null;
    }
}