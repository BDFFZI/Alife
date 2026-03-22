using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Alife.Test;

/// <summary>
/// 标准化的 Demo 测试套件，封装了环境配置、日志追踪、消息收发等共有逻辑。
/// </summary>
public class DemoSuite : IAsyncDisposable
{
    public ChatActivity Activity => chatActivity;
    public ChatBot Bot => chatActivity.ChatBot;
    public ConfigurationSystem ConfigSystem => chatActivity.PluginService.GetRequiredService<ConfigurationSystem>();
    public StorageSystem StorageSystem => chatActivity.PluginService.GetRequiredService<StorageSystem>();

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

        Terminal.LogInfo("正在创建 ChatActivity 并注入插件...");
        var activity = await ChatActivity.Create(character, config, null, [config, storage]);

        Terminal.LogInfo($"[插件加载完毕]: {string.Join(", ", activity.Plugins.Select(p => p.GetType().Name))}");

        var suite = new DemoSuite(activity);
        suite.SetupStandardLogging();

        Terminal.LogSystem($"[角色系统提示词]:\n{character.Prompt}");
        Terminal.LogSuccess("环境构建完成喵！✨");

        return suite;
    }

    /// <summary>
    /// 运行标准化的交互循环。
    /// </summary>
    public async Task RunAsync()
    {
        Terminal.LogInfo("文字输入已就绪，可直接在下方输入文字与 AI 交流。输入 'exit' 退出。");
        while (isRunning)
        {
            Console.Write("\n> ");
            string? input = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(input)) continue;

            if (input.ToLower() == "exit") break;

            await foreach (var chunk in Bot.ChatStreamingAsync(input))
            {
                continue;
            }

            Console.WriteLine(); // 换行结束流式输出
        }
        Terminal.LogInfo("正在退出套件...");
    }

    private readonly ChatActivity chatActivity;
    private bool isRunning = true;
    private bool isChatFront;

    private DemoSuite(ChatActivity activity)
    {
        chatActivity = activity;
    }

    private void SetupStandardLogging()
    {
        Bot.ChatSent += (msg) =>
        {
            Terminal.LogSent("USER", msg);
            isChatFront = true;
        };
        Bot.ChatReceived += (msg) =>
        {
            if (isChatFront)
            {
                isChatFront = false;
                Terminal.LogReceivedStart("AI");
            }
            Terminal.LogReceivedChunk(msg);
        };
        Bot.ChatOver += () =>
        {
            Console.WriteLine();
        };

        // 监听历史记录添加，捕获插件注入的消息、AI 回复等
        Bot.ChatHistoryAdd += (msg) =>
        {
            if (msg.Role == AuthorRole.User)
            {
                return;
            }
            if (msg.Role == AuthorRole.Assistant)
            {
                return;
            }

            string roleLabel = msg.Role.ToString().ToUpper();
            string content = msg.Content ?? "(无内容)";

            if (msg.Role == AuthorRole.System)
            {
                Terminal.LogSystem($"[{roleLabel}] {content}");
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

    public async ValueTask DisposeAsync()
    {
        isRunning = false;
        await chatActivity.DisposeAsync();
    }
}
