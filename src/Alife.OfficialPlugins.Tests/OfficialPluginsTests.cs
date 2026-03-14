using Alife.OfficialPlugins;
using Alife.Interpreter;
using Alife.Abstractions;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Agents;
using Xunit;
using Moq;

namespace Alife.OfficialPlugins.Tests;

public class OfficialPluginsTests
{
    [Fact]
    public async Task TestInterpreterServiceCapabilitySummary()
    {
        var interpreterService = new InterpreterService();
        var chatWindow = new ChatWindow();
        var speechService = new SpeechService(interpreterService, chatWindow);
        
        var kernelBuilder = new Mock<IKernelBuilder>();
        var agentThread = new ChatHistoryAgentThread(new ChatHistory());
        
        await interpreterService.AwakeAsync(kernelBuilder.Object, agentThread);
        
        var summaryMessage = agentThread.ChatHistory.Last().Content;
        Assert.Contains("<speak>", summaryMessage);
        Assert.Contains("让AI说出指定的内容给用户听。", summaryMessage);
    }
}
