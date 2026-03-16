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

    readonly XmlHandlerCompiler compiler = new();
    XmlStreamParser parser = null!;
    XmlStreamExecutor executor = null!;

    public override Task AwakeAsync(AwakeContext context)
    {
        //创建xml解析执行器等
        XmlHandlerTable handlerTable = compiler.Compile();
        parser = new XmlStreamParser();
        executor = new XmlStreamExecutor(
            parser,
            handlerTable,
            ["，", "。", "！", "？", "......", "~"],
            minResultLength: 7
        );

        //注入使用说明
        string doc = handlerTable.GenerateDocumentation();        //注入使用说明
        context.contextBuilder.ChatHistory.AddSystemMessage(
            @$"## 交互指令系统 (Interpreter)
你拥有一套实时的 XML 指令来控制桌宠展示情绪和动作。

**核心规则：**
1. **直接嵌入**：在回复中直接插入标签（例：`你好喵<pet_mtn>2</pet_mtn>`），严禁解释或复述标签。
2. **内容优先**：参数统一写在标签中间，不再使用复杂的属性名。
3. **流式兼容**：你可以放心连用多个标签。

**支持标签说明：**
{doc}

**常用组合建议：**
- 羞涩拒绝：`<pet_exp>06</pet_exp><pet_mtn>0</pet_mtn><pet_bubble>人家才没想你呢喵！</pet_bubble>`
- 兴奋欢迎：`<pet_mtn>3</pet_mtn><pet_exp>01</pet_exp><pet_bubble>欢迎回来喵！</pet_bubble>`
"
        );

        return Task.CompletedTask;
    }

    public override Task StartAsync(Kernel kernel, ChatActivity chatActivity)
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
