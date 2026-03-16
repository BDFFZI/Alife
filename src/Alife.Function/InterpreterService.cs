using System.ComponentModel;

namespace Alife.OfficialPlugins;

using Abstractions;
using Interpreter;
using Microsoft.SemanticKernel;

[Plugin("框架-口译员", "为AI增加一种基于Xml的流式函数执行功能，实现快速实时的交互能力。")]
public class InterpreterService : Plugin
{
    public void RegisterHandler(object handler)
    {
        compiler.Register(handler);
    }

    [XmlHandler]
    [Description("当添加这个标签后，你就可以主动继续说话或操作了（但不要一直用，不要死循环！）。所以当遇到特别复杂的任务时，可以借此可以将其拆分成很多子任务来执行。")]
    public void Continue(XmlTagContext context)
    {
        if (context.Status == TagStatus.Closing || context.Status == TagStatus.OneShot)
            chatActivity.ChatBot.Poke("[来自continue标签的唤起：你可以机会继续说话啦！但不要一直使用噢，要让主人有说话的机会。]");
    }

    readonly XmlHandlerCompiler compiler = new();
    XmlStreamParser parser = null!;
    XmlStreamExecutor executor = null!;
    ChatActivity chatActivity = null!;

    public override Task AwakeAsync(AwakeContext context)
    {
        //创建xml解析执行器等
        compiler.Register(this);
        XmlHandlerTable handlerTable = compiler.Compile();
        parser = new XmlStreamParser { RootTagName = "Interpreter" };
        executor = new XmlStreamExecutor(
            parser,
            handlerTable,
            ["，", "。", "！", "？", "......", "~"],
            minResultLength: 7
        );

        //注入使用说明
        string prompt = @$"# 内容标记功能
你可以给你的回复套上一些可选的xml标签，来使内容发挥一些特定作用。
**目前可用的标签：**
{handlerTable.GenerateDocumentation()}
注意：如果你要使用上述功能，必须先用<Interpreter></Interpreter>包裹。
";

//         @"
        // **使用规则：**
        //     1. **直接嵌入**：在回复中直接插入标签。
        // 2. **流式兼容**：你可以放心连用多个标签。
        // 2. **可选参数**：如果标签支持可以携带属性。
// **标签语法**
// <p>(在这里填写主要内容)</p>
// <p size:int(这种标签可以携带参数)></p>
// <p><img>joy.png</img>开心！<p>(标签也可以嵌套)
// ";

        context.contextBuilder.ChatHistory.AddSystemMessage(prompt);
        Console.WriteLine(prompt);

        return Task.CompletedTask;
    }

    public override Task StartAsync(Kernel kernel, ChatActivity chatActivity)
    {
        this.chatActivity = chatActivity;
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
        executor.FlushAsync().Wait();
    }
    void OnChatReceived(string obj)
    {
        executor.Feed(obj);
    }
}
