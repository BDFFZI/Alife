using System.ComponentModel;
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
    MemoryStream memoryStream = null!;
    StreamWriter streamWriter = null!;
    XmlStreamParser parser = null!;
    XmlStreamExecutor executor = null!;
    PeriodicTimer periodicTimer = null!;

    public Task AwakeAsync(IKernelBuilder kernelBuilder, ChatHistoryAgentThread agentThread)
    {
        XmlHandlerTable handlerTable = compiler.Compile();
        parser = new XmlStreamParser();
        executor = new XmlStreamExecutor(parser, handlerTable, [',', '。']);
        memoryStream = new MemoryStream();
        streamWriter = new StreamWriter(memoryStream);
        periodicTimer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        Update();

        return Task.CompletedTask;
    }

    public Task StartAsync(Kernel kernel, ChatBot chatBot)
    {
        chatBot.ChatHandle += OnChatHandle;
        return Task.CompletedTask;
    }

    async void Update()
    {
        while (await periodicTimer.WaitForNextTickAsync()) { }
    }

    void OnChatHandle(string obj) { }
}
