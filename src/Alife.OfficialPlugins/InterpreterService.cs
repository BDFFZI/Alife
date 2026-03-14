namespace Alife.OfficialPlugins;

using System.Text;
using Alife.Abstractions;
using Alife.Interpreter;
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

    public async Task AwakeAsync(IKernelBuilder kernelBuilder, ChatHistoryAgentThread context)
    {
        XmlHandlerTable handlerTable = compiler.Compile();
        parser = new XmlStreamParser();
        executor = new XmlStreamExecutor(parser, handlerTable, [',', '。']);

        string doc = handlerTable.GenerateDocumentation();
        context.ChatHistory.AddSystemMessage(
            @$"功能说明：
1. XML流式能力：这是你默认且最主要的交互方式！你现在拥有通过嵌套XML标签来驱动外部系统的能力！这可以让你边说话边做动作，或者用语音、画面来辅助你的表达。请优先使用并贯穿你的整个对话逻辑！
2. 嵌套与层级：标签完全支持嵌套。例如，你可以用一个 `<emotion>` 标签包裹整个回复，然后在其中嵌套 `<img />` 和 `<speak>`。系统会按照流式解析的顺序即时执行。
3. 参数规范：标签参数严格遵循 `name=""value""`。在下方的说明中，`[]` 表示可选，参数后跟有类型注解（如 `speed:float`），请严格按规范填写参数！
4. 不要解释标签：在回复中直接使用标签即可，不要向用户解释你在使用什么标签，保持对话的自然性。

使用案例：
- 案例一（作为默认交互方式）：
    输入：今天天气怎么样？
    回复：<chat>今天天气真不错！<img src=""http://api.weather.com/today.png"" /> 看到天空这么蓝，心情都变好了。</chat>
    细节：chat中的文字将会被发送到聊天窗口，同时其中穿插了一张天气图片的展示。
- 案例二（多能力协同）：
    输入：小声地打个招呼。
    回复：<chat type=""Joy"">好的，<speak volume=""0.5"">你好啊，见到你真高兴！</speak></chat>
    细节：chat中的文字被发送到聊天窗口，同时用户能听到语音，并且语音的音量被控制。

目前可用的标签（不要自创未列出的标签）：
{doc}
（注：标签默认用法为`<tag>内容</tag>`，但也可能支持`<tag />`，具体见功能描述而定。）
"
        );
    }

    public Task StartAsync(Kernel kernel, ChatBot chatBot)
    {
        chatBot.ChatHandle += OnChatHandle;
        chatBot.ChatStart += OnChatStart;
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
