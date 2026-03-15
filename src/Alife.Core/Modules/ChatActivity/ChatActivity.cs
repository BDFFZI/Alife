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
        //创建服务容器
        ServiceCollection extensionServiceBuilder = new();
        //添加系统服务
        if (appendServices != null)
        {
            foreach (var appendService in appendServices)
                extensionServiceBuilder.AddSingleton(appendService.GetType(), appendService);
        }
        //添加插件服务
        foreach (Type pluginType in character.Plugins.OrderBy(type => type.GetCustomAttribute<PluginAttribute>()?.LaunchOrder))
            extensionServiceBuilder.AddSingleton(pluginType);
        ServiceProvider extensionService = extensionServiceBuilder.BuildServiceProvider();

        //实例化所有插件
        List<Plugin> allPlugins = new(extensionServiceBuilder.Count);
        for (int index = 0; index < extensionServiceBuilder.Count; index++)
        {
            ServiceDescriptor serviceDescriptor = extensionServiceBuilder[index];
            progress?.Report(($"创建服务 {serviceDescriptor.ServiceType.Name}", (float)index / extensionServiceBuilder.Count));

            await Task.Delay(100);

            object service = extensionService.GetRequiredService(serviceDescriptor.ServiceType);
            if (service is Plugin plugin)
                allPlugins.Add(plugin);
        }

        //赋值插件配置数据
        foreach (Plugin pluginInstance in allPlugins)
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

        //创建人工智能构建器
        IKernelBuilder kernelBuilder = Kernel.CreateBuilder();
        //创建上下文构建器
        ChatHistoryAgentThread contentBuilder = new();
        //构建环境
        Plugin.AwakeContext awakeContext = new() {
            character = character,
            kernelBuilder = kernelBuilder,
            contextBuilder = contentBuilder
        };
        for (int index = 0; index < allPlugins.Count; index++)
        {
            Plugin pluginInstance = allPlugins[index];
            progress?.Report(($"唤醒服务 {pluginInstance.GetType().Name}", (float)index / allPlugins.Count));

            await pluginInstance.AwakeAsync(awakeContext);
        }

        //正式开始 AI 代理
        Kernel kernelService = kernelBuilder.Build();
        ChatActivity chatActivity = new ChatActivity(character, contentBuilder, kernelService, extensionService, allPlugins);

        for (int index = 0; index < allPlugins.Count; index++)
        {
            Plugin pluginInstance = allPlugins[index];
            progress?.Report(($"开始服务 {pluginInstance.GetType().Name}", (float)index / allPlugins.Count));

            await pluginInstance.StartAsync(kernelService, chatActivity);
        }

        return chatActivity;
    }

    public ServiceProvider PluginService => pluginService;
    public Kernel KernelService => kernelService;
    public Character Character => character;
    public ChatBot ChatBot => chatBot;
    public IReadOnlyList<Plugin> Plugins => plugins;

    ChatActivity(Character character, ChatHistoryAgentThread context,
        Kernel kernelService, ServiceProvider pluginService, List<Plugin> plugins)
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
            InstructionsRole = AuthorRole.System,
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
    readonly List<Plugin> plugins;

    public async ValueTask DisposeAsync()
    {
        try
        {
            await Task.WhenAll(plugins.Select(plugin => plugin.DestroyAsync()));
            await chatBot.DisposeAsync();
            await pluginService.DisposeAsync();
            await Task.Delay(1000); //等待一秒让用户反应
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }
}
