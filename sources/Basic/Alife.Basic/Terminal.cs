namespace Alife.Basic;

public static class Terminal
{
    public static readonly Lock ConsoleLock = new();
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
    public static void LogHint(string message) => Log($"[Success] {message}", ConsoleColor.Green);
    public static void LogWarning(string message) => Log($"[Warning] {message}", ConsoleColor.Yellow);
    public static void LogError(string message) => Log($"[Error] {message}", ConsoleColor.Red);
}
