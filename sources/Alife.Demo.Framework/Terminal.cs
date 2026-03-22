using System;

namespace Alife.Test;

/// <summary>
/// 标准化的终端日志工具，支持颜色区分。
/// </summary>
public static class Terminal
{
    private static readonly object _lock = new object();

    public static void Log(string message, ConsoleColor color = ConsoleColor.Gray)
    {
        lock (_lock)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write($"[{DateTime.Now:HH:mm:ss}] ");
            Console.ForegroundColor = color;
            Console.WriteLine(message);
            Console.ResetColor();
        }
    }

    public static void LogInfo(string message) => Log($"[INFO] {message}", ConsoleColor.Yellow);
    public static void LogSuccess(string message) => Log($"[OK] {message}", ConsoleColor.Green);
    public static void LogError(string message) => Log($"[ERROR] {message}", ConsoleColor.Red);
    public static void LogSystem(string message) => Log($"[SYS] {message}", ConsoleColor.DarkYellow);

    public static void LogSent(string sender, string message)
    {
        lock (_lock)
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
        lock (_lock)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write($"[{DateTime.Now:HH:mm:ss}] ");
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write($"RECV {receiver} < ");
            Console.ForegroundColor = ConsoleColor.White;
        }
    }

    public static void LogReceivedChunk(string chunk)
    {
        lock (_lock)
        {
            Console.Write(chunk);
        }
    }
}
