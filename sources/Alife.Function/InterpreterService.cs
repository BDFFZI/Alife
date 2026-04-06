namespace Alife.OfficialPlugins;

using Alife.Abstractions;
using Alife.Interpreter;
using Microsoft.SemanticKernel;

[Plugin("口译员", "为AI增加一种基于Xml的流式函数执行功能，实现快速实时的交互能力。")]
public class InterpreterService : Plugin
{
    public void RegisterHandler(object handler)
    {
        compiler.Register(handler);
    }

    readonly XmlHandlerCompiler compiler = new();
    OldXmlStreamParser parser = null!;
    XmlStreamExecutor executor = null!;

    public override Task AwakeAsync(AwakeContext context)
    {
        //创建xml解析执行器等
        compiler.Register(this);
        XmlHandlerTable handlerTable = compiler.Compile();
        parser = new OldXmlStreamParser() {
            RootTagName = "parse"
        };
        executor = new XmlStreamExecutor(
            parser,
            handlerTable,
            ["，", "。", "！", "？", "......", "~"],
            minResultLength: 7
        );

        //注入使用说明
        string prompt = @$"# {nameof(InterpreterService)}
你拥有给文字套上部分xml标签来执行特殊功能的能力。
**目前可用的标签：**
{handlerTable.GenerateDocumentation()}
注意事项：
1. 你要先执行消息类指令（如speak、pet_bubble），然后再执行动作类指令（如pet_move、python）。
2. 不要分行使用消息类指令，每次必须将完整段落放在一组消息类指令中，如<pet_bubble>开始...过程...结束</pet_bubble>。
3. 你要嵌套使用消息类指令，如<pet_bubble><speak></speak></pet_bubble>，从而实现同时发送语音和气泡消息。
4. 以上功能必须放在<parse></parse>中才能生效（换句话说，如果你想把大段的标签内容比如html输出成内容，就把它放在parse外面即可）。
5. 如果你要在<parse></parse>中将'<','>','\'等符号作为普通字符使用，那么你必须得用'\'转义，如'<','>','\'。
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
