using Microsoft.SemanticKernel;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public static class KernelPrinter
{
    public static object ToLogObject(this FunctionCallContent functionCallContent)
    {
        object data = new {
            Id = functionCallContent.Id,
            PluginName = functionCallContent.PluginName,
            FunctionName = functionCallContent.FunctionName,
            Arguments = functionCallContent.Arguments
        };

        return data;
    }
    public static object ToLogObject(this FunctionResultContent functionResetContent)
    {
        object data = new {
            CallId = functionResetContent.CallId,
            PluginName = functionResetContent.PluginName,
            FunctionName = functionResetContent.FunctionName,
            Result = functionResetContent.Result
        };

        return data;
    }
    public static object ToLogObject(this TextContent textContent)
    {
        object data = new {
            Text = textContent.Text,
        };
        return data;
    }

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
            default:
                return "不支持打印的核心内容";
        }
    }

    public static object ToLogObject(this ChatMessageContent chatMessageContent)
    {
        object data = new {
            AuthorName = chatMessageContent.AuthorName,
            Role = chatMessageContent.Role,
            Content = chatMessageContent.Content,
            Items = chatMessageContent.Items.Select(kernelContent => kernelContent.ToLogObject())
        };

        return data;
    }
}
