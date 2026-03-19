using System.ComponentModel;

namespace Alife.OfficialPlugins;

using Alife.Abstractions;
using Alife.Interpreter;
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
注意事项：
1. 如果你要使用上述功能，必须先用<Interpreter></Interpreter>包裹。
2. 先执行消息类指令（如speak、pet_bubble），然后再执行动作类指令（如pet_move、python）。
3. 指令可以嵌套使用，例如将消息类指令嵌套使用，从而实现发文字消息的同时语音。
";

        context.contextBuilder.ChatHistory.AddSystemMessage(prompt);
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
        executor.Flush();
    }
    void OnChatReceived(string obj)
    {
        executor.Feed(obj);
    }
}
