using Microsoft.SemanticKernel;

namespace Alife.Framework;

public static class KernelPrinter
{
    public static object ToLogObject(this KernelContent kernelContent)
    {
        switch (kernelContent)
        {
            case FunctionCallContent functionCallContent:
                return functionCallContent.ToLogObject();
            case TextContent textContent:
                return textContent.ToLogObject();
            case FunctionResultContent functionResultContent:
                return functionResultContent.ToLogObject();
            case ChatMessageContent chatMessageContent:
                return chatMessageContent.ToLogObject();
            default:
                return "不支持打印的核心内容";
        }
    }

    static object ToLogObject(this ChatMessageContent chatMessageContent)
    {
        object data = new {
            chatMessageContent.AuthorName,
            chatMessageContent.Role,
            chatMessageContent.Content,
            Items = chatMessageContent.Items.Select(kernelContent => kernelContent.ToLogObject())
        };

        return data;
    }
    static object ToLogObject(this FunctionCallContent functionCallContent)
    {
        object data = new {
            functionCallContent.Id,
            functionCallContent.PluginName,
            functionCallContent.FunctionName,
            functionCallContent.Arguments
        };

        return data;
    }
    static object ToLogObject(this FunctionResultContent functionResetContent)
    {
        object data = new {
            functionResetContent.CallId,
            functionResetContent.PluginName,
            functionResetContent.FunctionName,
            functionResetContent.Result
        };

        return data;
    }
    static object ToLogObject(this TextContent textContent)
    {
        object data = new {
            textContent.Text,
        };
        return data;
    }
}
