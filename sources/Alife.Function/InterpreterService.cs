namespace Alife.OfficialPlugins;

using Abstractions;
using Interpreter;
using Microsoft.SemanticKernel;

[Plugin("口译员", "为AI增加一种基于Xml的流式函数执行功能，实现快速实时的交互能力。")]
public class InterpreterService : Plugin
{
    public void RegisterHandler(object handler)
    {
        handlerTable.Register(handler);
    }

    readonly XmlHandlerTable handlerTable = new();
    XmlStreamParser parser = null!;
    XmlStreamExecutor executor = null!;

    public override Task AwakeAsync(AwakeContext context)
    {
        //创建xml解析执行器等
        handlerTable.Register(this);
        parser = new XmlStreamParser();
        executor = new XmlStreamExecutor(
            parser,
            handlerTable,
            ["，", "。", "！", "？", "......", "~"],
            minBreakingLength: 7
        );

        //注入使用说明
        string prompt = @$"# {nameof(InterpreterService)}

你可以通过xml格式提供你的文本，xml格式与标准规范完全一致，一些特殊的xml标签还可以充当函数调用，使你的内容发挥特别的效果。

## 特殊标签

{handlerTable.Document()}

## 注意事项

1. 在一个标签中描述完整句子。
    - 正确写法：<speak>第一句。第二句。第三句</speak>
    - 错误写法：<speak>第一句</speak><speak>第二句</speak>
2. 如果要在内容中使用xml中的特殊符号，你必须要先进行转义，转义方式与标准xml一致。
    - 正确写法：我可以使用&lt;python&gt;标签来运行脚本。
    - 错误写法：我可以使用<python>标签来运行脚本。
";

        context.contextBuilder.ChatHistory.AddSystemMessage(prompt);
        return Task.CompletedTask;
    }
    public override Task StartAsync(Kernel kernel, ChatActivity chatActivity)
    {
        chatActivity.ChatBot.ChatReceived += OnChatReceived;
        chatActivity.ChatBot.ChatSent += OnChatSent;
        chatActivity.ChatBot.ChatOver += OnChatOver;
        return Task.CompletedTask;
    }

    void OnChatSent(string _)
    {
        executor.Reset();
    }
    void OnChatOver()
    {
        executor.Flush();
    }
    void OnChatReceived(string obj)
    {
        executor.Feed(obj);
    }
}
