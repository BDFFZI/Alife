namespace Alife.OfficialPlugins;

using Abstractions;
using Interpreter;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;

[Plugin("框架-口译员", "为AI增加一种基于Xml的流式函数执行功能，实现快速实时的交互能力。")]
public class InterpreterService : IPlugin
{
    public void RegisterHandler(object handler)
    {
        compiler.Register(handler);
    }

    readonly XmlHandlerCompiler compiler = new();
    XmlStreamParser parser = null!;
    XmlStreamExecutor executor = null!;

    public Task AwakeAsync(IKernelBuilder kernelBuilder, ChatHistoryAgentThread context)
    {
        XmlHandlerTable handlerTable = compiler.Compile();
        parser = new XmlStreamParser();
        executor = new XmlStreamExecutor(
            parser,
            handlerTable,
            ["，", "。", "！", "？", "......"],
            minResultLength: 13
        );

        string doc = handlerTable.GenerateDocumentation();
        context.ChatHistory.AddSystemMessage(
            @$"InterpreterService
功能说明：
1. 你拥有通过某些xml标签修饰对话内容的能力，这些被标记的文字将会传递给外部系统进行处理，从而让你具有多模态输出的能力。
2. 标签可能会支持参数，其解读方式为：参数名后面表示类型；如果是enum，则还会用花括号列出选项；如果参数用[]包裹，表示可选。
3. 不要向用户解释标签功能：在回复中直接使用即可，这是你的默认输出方式，优先保持对话的自然性。

使用案例：
- 输入：今天天气怎么样？
- 回复：<chat>我看看 <img src=""weather.jpg"" /><speak>天气非常好呢！</speak></chat>
- 细节：chat中的文字“我看看 天气非常好呢！”被发送到聊天窗口，并穿插了一张图片，同时用户能听到语音“天气非常好呢！”。

目前支持的标签列表：
{doc}
（注意：不要自创此列表中未列出的标签！）
"
        );
        return Task.CompletedTask;
    }

    public Task StartAsync(Kernel kernel, ChatActivity chatActivity)
    {
        chatActivity.ChatBot.ChatHandle += OnChatHandle;
        chatActivity.ChatBot.ChatStart += OnChatStart;
        return Task.CompletedTask;
    }
    void OnChatStart()
    {
        executor.Reset();
    }
    void OnChatHandle(string obj)
    {
        executor.Feed(obj);
    }
}
