using System;
using System.Linq;
using Alife.Basic;
using Alife.Framework;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel.ChatCompletion;

public class DemoSuite : IAsyncDisposable
{
    public ConfigurationSystem Configuration => chatActivity.PluginService.GetRequiredService<ConfigurationSystem>();
    public ChatBot ChatBot => chatActivity.ChatBot;

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

        LogSystem($"[角色系统提示词]:\n{character.Prompt}");
        Terminal.LogHint("环境构建完成喵！✨");

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

            await ChatBot.ChatAsync(input);
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

        ChatBot.ChatSent += (msg) => {
            LogSent("USER", msg);
            isReceivingChat = true;
        };
        ChatBot.ChatReceived += (msg) => {
            if (isReceivingChat)
            {
                isReceivingChat = false;
                LogReceivedStart("AI");
            }
            LogReceivedContent(msg);
        };
        ChatBot.ChatOver += () => Console.WriteLine();

        ChatBot.ChatHistoryAdd += (msg) => {
            if (msg.Role == AuthorRole.User || msg.Role == AuthorRole.Assistant) return;

            string content = msg.Content ?? "(无内容)";

            if (msg.Role == AuthorRole.System)
                LogSystem($"[SYSTEM] {content}");
            else if (msg.Role == AuthorRole.Tool)
                LogSystem($"[TOOL_USED] {content}");
            else
                Terminal.Log($"[{msg.Role.ToString().ToUpper()}] {content}", ConsoleColor.DarkGray);
        };
    }
    public async ValueTask DisposeAsync()
    {
        isRunning = false;
        await chatActivity.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    static void LogSystem(string message) => Terminal.Log($"[System] {message}", ConsoleColor.DarkYellow);
    static void LogSent(string sender, string message)
    {
        lock (Terminal.ConsoleLock)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write($"[{DateTime.Now:HH:mm:ss}] ");
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write($"{sender} SENT > ");
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine(message);
            Console.ResetColor();
        }
    }
    static void LogReceivedStart(string receiver)
    {
        lock (Terminal.ConsoleLock)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write($"[{DateTime.Now:HH:mm:ss}] ");
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write($"RECV {receiver} < ");
            Console.ForegroundColor = ConsoleColor.White;
        }
    }
    static void LogReceivedContent(string content)
    {
        lock (Terminal.ConsoleLock)
        {
            Console.Write(content);
        }
    }
}
