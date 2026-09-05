using System.Collections.Concurrent;
using Alife.Foundation;
using Alife.Framework;
using Alife.Function.FunctionCaller;
using Alife.Function.Language.OpenAI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

const string apiKeyEnvironmentVariable = "ALIFE_GLM_API_KEY";
string apiKey = Environment.GetEnvironmentVariable(apiKeyEnvironmentVariable)
    ?? throw new InvalidOperationException($"请设置 {apiKeyEnvironmentVariable} 后再运行此 Demo。");

string imagePath = Path.Combine(Path.GetTempPath(), "alife-multimodal-demo.png");
await File.WriteAllBytesAsync(imagePath, Convert.FromBase64String(
    "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQIHWP4z8DwHwAFgAI/ScLHiAAAAABJRU5ErkJggg=="));

Character character = new() {
    Name = "多模态验证助手",
    Modules = [
        typeof(XmlFunctionCaller).FullName!,
        typeof(OpenAILanguageModel).FullName!
    ]
};

ServiceCollection services = new();
services.AddAlife();
await using ServiceProvider provider = services.BuildServiceProvider();
await provider.InitAlife();

ConfigurationSystem configurationSystem = provider.GetRequiredService<ConfigurationSystem>();
configurationSystem.SetConfiguration(typeof(OpenAILanguageModel), new OpenAILanguageModelConfig {
    endpoint = "https://open.bigmodel.cn/api/paas/v4",
    apiKey = apiKey,
    modelId = "glm-5.3-flash",
    defaultThinking = false,
    extraBody = "{}",
    extraBodyNotThinking = "{}",
    enableImageInput = true,
    enableVideoInput = true,
    enableFileInput = true,
    useGlmMultimodalProtocol = true
}, character.StorageKey);

ChatActivitySystem activities = provider.GetRequiredService<ChatActivitySystem>();
ChatActivity activity = await activities.Activate(character)
    ?? throw new InvalidOperationException("无法激活多模态 Demo 对话。");

try
{
    await activity.Start();
    ChatBot chatBot = activity.ChatBot;
    ConcurrentQueue<ChatContext> results = new();
    TaskCompletionSource secondChatFinished = new(TaskCreationOptions.RunContinuationsAsynchronously);
    Exception? chatException = null;
    chatBot.ChatFinished += result => {
        results.Enqueue(result);
        if (results.Count >= 2)
            secondChatFinished.TrySetResult();
    };
    chatBot.ChatExceptionThrow += exception => chatException = exception;

    ChatResult firstResult = await chatBot.ChatAsync(
        $"请只输出 `<loadimage path=\"{imagePath}\"/>`，不要输出任何其他文字。",
        breakLast: false);
    if (firstResult.Exception != null)
        throw new InvalidOperationException("首次请求失败。", firstResult.Exception);

    await secondChatFinished.Task.WaitAsync(TimeSpan.FromSeconds(60));
    bool imageWasAdded = chatBot.ChatHistory
        .SelectMany(message => message.Items)
        .OfType<ImageContent>()
        .Any();
    if (imageWasAdded == false)
        throw new InvalidOperationException("loadimage 未将图片加入 ChatHistory。");

    ChatContext[] allResults = results.ToArray();
    if (chatException != null)
        throw new InvalidOperationException("Poke 后的续聊失败。", chatException);

    Console.WriteLine($"验证成功：已完成 {allResults.Length} 轮对话，图片已进入上下文。第二轮回复：{allResults[1].AIMessage}");
}
finally
{
    await activities.Deactivate(character);
    File.Delete(imagePath);
}
