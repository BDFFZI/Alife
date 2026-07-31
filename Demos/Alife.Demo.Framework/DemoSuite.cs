using Alife.Platform;
using Alife.Framework;
using Alife.Function.FunctionCaller;
using Alife.Function.Language.OpenAI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

public static class DemoSuite
{
    public static async Task Run(
        Character character,
        Action<ServiceProvider>? systemCreated = null,
        Action<ChatActivity>? activityCreated = null)
    {
        Console.WriteLine("导入:" + typeof(OpenAILanguageModel));
        Console.WriteLine("导入:" + typeof(XmlFunctionCaller));

        ServiceCollection serviceCollection = new();
        serviceCollection.AddAlife();
        ServiceProvider provider = serviceCollection.BuildServiceProvider();
        provider.InitAlife();

        systemCreated?.Invoke(provider);

        ChatActivitySystem chatActivitySystem = provider.GetRequiredService<ChatActivitySystem>();
        chatActivitySystem.ActivatingProcess += (_, progress) => { DemeLog.LogInfo(progress.Step); };
        chatActivitySystem.ActivationFailed += (_, exception) => { DemeLog.LogError(exception.ToString()); };

        //进行活动
        {
            DemeLog.LogInfo("激活对话中...");
            ChatActivity? chatActivity = await chatActivitySystem.Activate(character);
            if (chatActivity == null)
            {
                DemeLog.LogError("对话活动启动失败");
                return;
            }

            AddChatLog(chatActivity.ChatBot);
            activityCreated?.Invoke(chatActivity);

            DemeLog.LogInfo("对话开始。直接在下方输入文字与 AI 交流。输入 'exit' 退出。");
            StartChat(chatActivity.ChatBot);

            DemeLog.LogInfo("关闭对话中...");
            await chatActivitySystem.Deactivate(character);

            DemeLog.LogInfo("对话结束");
        }
    }

    static void AddChatLog(ChatBot chatBot)
    {
        bool isFirstMessage = false;
        bool isFirstReasoning = false;

        chatBot.ChatSent += msg =>
        {
            LogSent("USER", msg);
            isFirstMessage = true;
            isFirstReasoning = true;
        };
        chatBot.ReasoningReceived += msg =>
        {
            if (isFirstReasoning)
            {
                isFirstReasoning = false;
                LogReceivedStart("Reasoning");
            }

            LogReceivedContent(msg, ConsoleColor.Gray);
        };
        chatBot.ChatReceived += msg =>
        {
            if (isFirstMessage)
            {
                isFirstMessage = false;
                Console.WriteLine();
                LogReceivedStart("Message");
            }

            LogReceivedContent(msg, ConsoleColor.White);
        };
        chatBot.ChatOver += Console.WriteLine;
        chatBot.ChatHistoryAdd += OnChatHistoryAdd;
        chatBot.ChatExceptionThrow += AlifeLog.LogError;

        foreach (ChatMessageContent chatMessageContent in chatBot.ChatHistory)
            OnChatHistoryAdd(chatMessageContent);

        void OnChatHistoryAdd(ChatMessageContent msg)
        {
            if (msg.Role == AuthorRole.User || msg.Role == AuthorRole.Assistant) return;

            string content = msg.Content ?? "(无内容)";

            if (msg.Role == AuthorRole.System)
                LogSystem($"[SYSTEM] {content}");
            else if (msg.Role == AuthorRole.Tool)
                LogSystem($"[TOOL_USED] {content}");
            else
                DemeLog.Log($"[{msg.Role.ToString().ToUpper()}] {content}", ConsoleColor.DarkGray);
        }
    }
    static void StartChat(ChatBot chatBot)
    {
        while (true)
        {
            Console.Write("\n> ");
            string? input = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(input))
                continue;
            if (input.Equals("exit", StringComparison.CurrentCultureIgnoreCase))
                break;

            chatBot.Chat(input);
            Console.WriteLine();
        }
    }

    static void LogSystem(string message) => DemeLog.Log($"[System] {message}", ConsoleColor.DarkYellow);
    static void LogSent(string sender, string message)
    {
        lock (DemeLog.ConsoleLock)
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
        lock (DemeLog.ConsoleLock)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write($"[{DateTime.Now:HH:mm:ss}] ");
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write($"RECV {receiver} < ");
            Console.ForegroundColor = ConsoleColor.White;
        }
    }
    static void LogReceivedContent(string content, ConsoleColor consoleColor)
    {
        lock (DemeLog.ConsoleLock)
        {
            Console.ForegroundColor = consoleColor;
            Console.Write(content);
        }
    }
}