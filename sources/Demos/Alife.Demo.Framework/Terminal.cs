namespace Alife.Test;

public static class Terminal
{
    public static void Log(string message, ConsoleColor color = ConsoleColor.Gray)
    {
        lock (ConsoleLock)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write($"[{DateTime.Now:HH:mm:ss}] ");
            Console.ForegroundColor = color;
            Console.WriteLine(message);
            Console.ResetColor();
        }
    }

    public static void LogInfo(string message) => Log($"[Info] {message}", ConsoleColor.White);
    public static void LogWarning(string message) => Log($"[Warning] {message}", ConsoleColor.Yellow);
    public static void LogError(string message) => Log($"[Error] {message}", ConsoleColor.Red);
    public static void LogSuccess(string message) => Log($"[Success] {message}", ConsoleColor.Green);
    public static void LogSystem(string message) => Log($"[System] {message}", ConsoleColor.DarkYellow);

    public static void LogSent(string sender, string message)
    {
        lock (ConsoleLock)
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

    public static void LogReceivedStart(string receiver)
    {
        lock (ConsoleLock)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write($"[{DateTime.Now:HH:mm:ss}] ");
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write($"RECV {receiver} < ");
            Console.ForegroundColor = ConsoleColor.White;
        }
    }

    public static void LogReceivedContent(string content)
    {
        lock (ConsoleLock)
        {
            Console.Write(content);
        }
    }

    static readonly Lock ConsoleLock = new();
}
