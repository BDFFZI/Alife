using System.Reflection;
using Alife.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.ChatCompletion;

public class ChatActivity : IAsyncDisposable
{
    public static async Task<ChatActivity> Create(
        Character character,
        ConfigurationSystem configurationSystem,
        IProgress<(string, float)>? progress = null,
        object[]? appendServices = null)
    {
        //创建插件服务
        ServiceCollection extensionServiceBuilder = new();
        //添加系统服务
        if (appendServices != null)
        {
            foreach (var appendService in appendServices)
                extensionServiceBuilder.AddSingleton(appendService.GetType(), appendService);
        }
        foreach (Type pluginType in character.Plugins)
            extensionServiceBuilder.AddSingleton(pluginType);
        ServiceProvider extensionService = extensionServiceBuilder.BuildServiceProvider();

        //实例化所有插件
        List<IPlugin> allPlugins = new(extensionServiceBuilder.Count);
        foreach (ServiceDescriptor serviceDescriptor in extensionServiceBuilder)
        {
            object service = extensionService.GetRequiredService(serviceDescriptor.ServiceType);
            if (service is IPlugin plugin)
                allPlugins.Add(plugin);
        }

        //赋值插件配置数据
        foreach (IPlugin pluginInstance in allPlugins)
        {
            Type pluginType = pluginInstance.GetType();
            object? extensionData = configurationSystem.GetConfiguration(pluginType);
            if (extensionData != null)
            {
                MethodInfo? configureMethod = pluginType.GetMethod("Configure");
                if (configureMethod != null)
                    configureMethod.Invoke(pluginInstance, [extensionData]);
            }
        }

        //创建人工智能服务
        IKernelBuilder kernelBuilder = Kernel.CreateBuilder();
        // builder.Services.AddLogging(config => {
        //     config.AddConsole();
        //     config.SetMinimumLevel(LogLevel.Trace);
        //     config.AddFilter("Microsoft.SemanticKernel", LogLevel.Trace);
        //     config.AddFilter("System.Net.Http.HttpClient", LogLevel.Trace);
        // });

        //添加上下文
        ChatHistoryAgentThread agentThread = new();

        //插件初始化事件回调
        int index = 0;
        int count = allPlugins.Count;
        foreach (IPlugin pluginInstance in allPlugins)
        {
            progress?.Report(($"配置插件 {pluginInstance.GetType().Name}", (float)index++ / count));
            await pluginInstance.AwakeAsync(kernelBuilder, agentThread);
        }

        //正式开始 AI 代理
        Kernel kernelService = kernelBuilder.Build();
        ChatActivity chatActivity = new ChatActivity(character, agentThread, kernelService, extensionService, allPlugins);

        foreach (IPlugin pluginInstance in allPlugins)
            await pluginInstance.StartAsync(kernelService, chatActivity);

        return chatActivity;
    }

    public ServiceProvider PluginService => pluginService;
    public Kernel KernelService => kernelService;
    public Character Character => character;
    public ChatBot ChatBot => chatBot;
    public IReadOnlyList<IPlugin> Plugins => plugins;

    ChatActivity(Character character, ChatHistoryAgentThread context,
        Kernel kernelService, ServiceProvider pluginService, List<IPlugin> plugins)
    {
        this.pluginService = pluginService;
        this.kernelService = kernelService;
        this.plugins = plugins;

        //保存原始设定
        this.character = (Character)character.Clone();

        //创建最核心的大语言服务功能
        if (kernelService.Services.GetService<IChatCompletionService>() == null)
            throw new NotSupportedException("必须至少提供一个支持对话能力的模型！");
        ChatCompletionAgent llmAgent = new() {
            Name = character.Name,
            Instructions = character.Prompt,
            Kernel = kernelService,
            Arguments = new KernelArguments(
                new PromptExecutionSettings() { FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(), }
            ),
        };
        chatBot = new(llmAgent, context);
    }

    readonly Character character;
    readonly ChatBot chatBot;
    readonly Kernel kernelService;
    readonly ServiceProvider pluginService;
    readonly List<IPlugin> plugins;

    public async ValueTask DisposeAsync()
    {
        await Task.WhenAll(plugins.Select(plugin => plugin.DestroyAsync()));
        await chatBot.DisposeAsync();
        await pluginService.DisposeAsync();
        await Task.Delay(1000); //等待一秒让用户反应
    }
}
