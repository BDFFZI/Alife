using System;
using System.Linq;
using System.Text;
using Alife.Abstractions;
using DialogContext = global::Alife.Modules.Context.DialogContext;
using DialogItem = global::Alife.Modules.Context.DialogItem;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Alife.Test;

/// <summary>
/// 标准化的 Demo 测试套件，封装了环境配置、日志追踪、消息收发等共有逻辑。
/// </summary>
public class DemoSuite : IAsyncDisposable
{
    private readonly ChatActivity _activity;
    private bool _isRunning = true;

    public ChatActivity Activity => _activity;
    public ChatBot Bot => _activity.ChatBot;
    public DialogContext? Context => _activity.Plugins.OfType<DialogContext>().FirstOrDefault();
    public ConfigurationSystem ConfigSystem => _activity.PluginService.GetRequiredService<ConfigurationSystem>();
    public StorageSystem StorageSystem => _activity.PluginService.GetRequiredService<StorageSystem>();

    private bool _isStreamingResponse = false;

    private DemoSuite(ChatActivity activity)
    {
        _activity = activity;
    }

    /// <summary>
    /// 初始化 Demo 环境并返回套件实例。
    /// </summary>
    public static async Task<DemoSuite> InitializeAsync(Character character)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Terminal.Log("========================================", ConsoleColor.Magenta);
        Terminal.Log($"   Alife Demo 套件: {character.Name}", ConsoleColor.Magenta);
        Terminal.Log("========================================", ConsoleColor.Magenta);

        Terminal.LogInfo("正在初始化系统环境 (Storage, Config)...");
        var storage = new StorageSystem();
        var config = new ConfigurationSystem(storage);

        // 自动注入 DialogContext (如果缺失)
        if (!character.Plugins.Contains(typeof(DialogContext)))
        {
            Terminal.LogSystem("检测到缺失 DialogContext，已自动注入。");
            character.Plugins.Add(typeof(DialogContext));
        }

        Terminal.LogInfo("正在创建 ChatActivity 并注入插件...");
        var activity = await ChatActivity.Create(character, config, null, [config, storage]);

        Terminal.LogInfo($"[插件加载完毕]: {string.Join(", ", activity.Plugins.Select(p => p.GetType().Name))}");

        var suite = new DemoSuite(activity);
        suite.SetupStandardLogging();

        Terminal.LogSystem($"[角色系统提示词]:\n{character.Prompt}");
        Terminal.LogSuccess("环境构建完成喵！✨");
        
        return suite;
    }

    private void SetupStandardLogging()
    {
        // 监听历史记录添加，捕获插件注入的消息、AI 回复等
        Bot.ChatHistoryAdd += (msg) =>
        {
            // 如果正在流式响应中，跳过 Assistant 消息的重复打印
            if (_isStreamingResponse && msg.Role == AuthorRole.Assistant) return;

            string roleLabel = msg.Role.ToString().ToUpper();
            string content = msg.Content ?? "(无内容)";

            if (msg.Role == AuthorRole.System)
            {
                Terminal.LogSystem($"[{roleLabel}] {content}");
            }
            else if (msg.Role == AuthorRole.User)
            {
                Terminal.LogReceived("USER", content);
            }
            else if (msg.Role == AuthorRole.Assistant)
            {
                Terminal.LogSent("AI", content);
            }
            else if (msg.Role == AuthorRole.Tool)
            {
                Terminal.LogSystem($"[TOOL_USED] {content}");
            }
            else
            {
                Terminal.Log($"[{roleLabel}] {content}", ConsoleColor.DarkGray);
            }
        };
    }

    /// <summary>
    /// 运行标准化的交互循环。
    /// </summary>
    public async Task RunAsync()
    {
        Terminal.LogInfo("文字输入已就绪，可直接在下方输入文字与 AI 交流。输入 'exit' 退出。");
        while (_isRunning)
        {
            Console.Write("\n> ");
            string? input = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(input)) continue;
            
            if (input.ToLower() == "exit") break;

            // 使用流式输出展示 AI 回复
            _isStreamingResponse = true;
            Terminal.LogStreamStart("AI");
            
            await foreach (var chunk in Bot.ChatStreamingAsync(input))
            {
                Terminal.LogStreamChunk(chunk);
            }
            
            Console.WriteLine(); // 换行结束流式输出
            _isStreamingResponse = false;
        }
        Terminal.LogInfo("正在退出套件...");
    }

    public async ValueTask DisposeAsync()
    {
        _isRunning = false;
        await _activity.DisposeAsync();
    }
}
