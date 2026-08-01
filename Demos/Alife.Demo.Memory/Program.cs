using System.Text.RegularExpressions;
using Alife.Framework;
using Alife.Function.Memory;
using Alife.Function.MessageFilter;
using Alife.Foundation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel.ChatCompletion;

Console.WriteLine("导入:" + typeof(MessageFilterService));

var character = new Character {
    Name = "开发测试助手",
    Modules = [
        typeof(MemoryService).FullName!
    ]
};

await DemoSuite.Run(character,
    provider => {
        ConfigurationSystem configurationSystem = provider.GetRequiredService<ConfigurationSystem>();
        configurationSystem.SetConfiguration(typeof(MemoryService), new MemoryConfig {
            Threshold = 8,
            BatchSize = 6,
            MaxCompressionLevel = 3,
            Probability = 1,
        });
    },
    activity => {
        activity.ChatBot.ChatOver += () => {
            PrintHistoryStructure(activity.ChatBot.ChatHistory);
        };
    }
);

static void PrintHistoryStructure(ChatHistory history)
{
    lock (DemeLog.ConsoleLock)
    {
        Console.WriteLine("\n[探测器] 当前上下文物理结构监控:");
        Console.WriteLine("----------------------------------------------------------------------");
        for (int i = 0; i < history.Count; i++)
        {
            var msg = history[i];
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write($"{i:00} | ");

            if (msg.Role == AuthorRole.System)
            {
                Console.ForegroundColor = ConsoleColor.DarkYellow;
                Console.Write("[SYSTEM     ] ");
            }
            else if (msg.Content != null && msg.Content.StartsWith("[记忆存档"))
            {
                Console.ForegroundColor = ConsoleColor.Blue;
                Console.Write("[ARCHIVED   ] ");
                Console.WriteLine(Regex.Match(msg.Content, @"\[记忆存档(.*)\]").Value);
                continue;
            }
            else
            {
                Console.ForegroundColor = msg.Role == AuthorRole.User ? ConsoleColor.White : ConsoleColor.Green;
                Console.Write($"[{msg.Role.ToString().ToUpper(),-12}] ");
            }

            string preview = msg.Content?.Length > 60 ? msg.Content.Substring(0, 57).Replace("\n", " ") + "..." : msg.Content?.Replace("\n", " ") ?? "";
            Console.WriteLine(preview);
        }
        Console.WriteLine("----------------------------------------------------------------------\n");
        Console.ResetColor();
    }
}
