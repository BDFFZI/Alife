using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Alife.Test;

public class DemoSuite : IAsyncDisposable
{
    public ChatActivity Activity => chatActivity;
    public ChatBot Bot => chatActivity.ChatBot;
    public ConfigurationSystem ConfigSystem => chatActivity.PluginService.GetRequiredService<ConfigurationSystem>();
    public StorageSystem StorageSystem => chatActivity.PluginService.GetRequiredService<StorageSystem>();

    public static async Task<DemoSuite> InitializeAsync(Character character)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Terminal.Log("========================================", ConsoleColor.Magenta);
        Terminal.Log($"   Alife Demo 套件: {character.Name}", ConsoleColor.Magenta);
        Terminal.Log("========================================", ConsoleColor.Magenta);

        Terminal.LogInfo("正在初始化系统环境 (Storage, Config)...");
        StorageSystem storage = new();
        ConfigurationSystem config = new(storage);

        Terminal.LogInfo("正在创建 ChatActivity 并注入插件...");
        ChatActivity activity = await ChatActivity.Create(character, config, null, [config, storage]);

        Terminal.LogInfo($"[插件加载完毕]: {string.Join(", ", activity.Plugins.Select(p => p.GetType().Name))}");

        DemoSuite suite = new(activity);

        Terminal.LogSystem($"[角色系统提示词]:\n{character.Prompt}");
        Terminal.LogSuccess("环境构建完成喵！✨");

        return suite;
    }

    public async Task RunAsync()
    {
        Terminal.LogInfo("文字输入已就绪，可直接在下方输入文字与 AI 交流。输入 'exit' 退出。");

        while (isRunning)
        {
            Console.Write("\n> ");
            string? input = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(input))
                continue;
            if (input.Equals("exit", StringComparison.CurrentCultureIgnoreCase))
                break;

            await Bot.ChatAsync(input);
            Console.WriteLine();
        }
        Terminal.LogInfo("正在退出套件...");
    }

    readonly ChatActivity chatActivity;
    bool isRunning = true;
    bool isReceivingChat;

    DemoSuite(ChatActivity activity)
    {
        chatActivity = activity;

        Bot.ChatSent += (msg) => {
            Terminal.LogSent("USER", msg);
            isReceivingChat = true;
        };
        Bot.ChatReceived += (msg) => {
            if (isReceivingChat)
            {
                isReceivingChat = false;
                Terminal.LogReceivedStart("AI");
            }
            Terminal.LogReceivedContent(msg);
        };
        Bot.ChatOver += () => Console.WriteLine();

        Bot.ChatHistoryAdd += (msg) => {
            if (msg.Role == AuthorRole.User || msg.Role == AuthorRole.Assistant) return;

            string content = msg.Content ?? "(无内容)";

            if (msg.Role == AuthorRole.System)
                Terminal.LogSystem($"[SYSTEM] {content}");
            else if (msg.Role == AuthorRole.Tool)
                Terminal.LogSystem($"[TOOL_USED] {content}");
            else
                Terminal.Log($"[{msg.Role.ToString().ToUpper()}] {content}", ConsoleColor.DarkGray);
        };
    }
    public async ValueTask DisposeAsync()
    {
        isRunning = false;
        await chatActivity.DisposeAsync();
    }
}
