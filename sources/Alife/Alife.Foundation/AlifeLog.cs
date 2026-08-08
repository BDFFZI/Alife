using System;
using System.IO;
using System.Text;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace Alife.Foundation;

public class AlifeLogger(string categoryName) : ILogger
{
    public IDisposable BeginScope<TState>(TState state) where TState : notnull
    {
        return NullScope.Instance;
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return true;
    }

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
            return;

        AlifeLog.Handle(
            categoryName,
            logLevel,
            eventId,
            state,
            exception,
            formatter);
    }

    public sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose() { }
    }
}

public class AlifeLogProvider : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName)
    {
        return new AlifeLogger(categoryName);
    }

    public void Dispose() { }
}

class AlifeConsoleWriter(TextWriter inner, Action<string> lineWrote) : TextWriter
{
    public override Encoding Encoding => inner.Encoding;

    public override void Write(char value)
    {
        inner.Write(value);
        if (value == '\n') FlushLine();
        else lineBuffer.Append(value);
    }
    public override void Write(string? value)
    {
        if (value == null) return;
        inner.Write(value);
        foreach (char c in value)
        {
            if (c == '\n') FlushLine();
            else lineBuffer.Append(c);
        }
    }
    public override void WriteLine(string? value)
    {
        inner.WriteLine(value);
        if (value != null) lineBuffer.Append(value);
        FlushLine();
    }
    public override void WriteLine() => FlushLine();

    readonly StringBuilder lineBuffer = new();

    void FlushLine()
    {
        if (lineBuffer.Length == 0)
            return;
        string line = lineBuffer.ToString();
        lineBuffer.Clear();
        if (string.IsNullOrEmpty(line))
            return;

        lineWrote(line);
    }
}

public static class AlifeLog
{
    public static string LogFilePath { get; } = Path.Combine(AlifePath.TempFolderPath, "alife.log");
    public static int LogLineCount { get; private set; }
    public static event Action<string>? WarningLogged;
    public static event Action<string>? ErrorLogged;
    public static event Action<string>? Logging;

    public static void SetupConsoleCapture()
    {
        Console.SetOut(new AlifeConsoleWriter(Console.Out, OnLogLineWrote));
        Console.SetError(new AlifeConsoleWriter(Console.Error, OnLogLineWrote));
    }

    public static void LogInformation(object message)
    {
        DefaultLogger.LogInformation(message.ToString());
    }

    public static void LogWarning(object message)
    {
        DefaultLogger.LogWarning(message.ToString());
    }

    public static void LogError(object message)
    {
        DefaultLogger.LogError(message.ToString());
    }

    static readonly AlifeLogger DefaultLogger = new("");
    static readonly Lock Lock = new();
    static readonly StreamWriter LogFile = new(LogFilePath, false);

    internal static void Handle<TState>(
        string category,
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        lock (Lock)
        {
            string message = formatter(state, exception);

            if (logLevel == LogLevel.Warning)
                WarningLogged?.Invoke(message);
            else if (logLevel == LogLevel.Error)
                ErrorLogged?.Invoke(message);

            WriteConsole(logLevel, category, message, exception);
        }

        static void WriteConsole(
            LogLevel logLevel,
            string category,
            string message,
            Exception? exception)
        {
            // 时间
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write(
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ");

            // Level
            Console.ForegroundColor = GetColor(logLevel);
            Console.Write(
                $"[{logLevel}] ");

            // Category
            if (string.IsNullOrEmpty(category) == false)
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write(
                    $"{category}: ");
            }

            // Message
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine(message);

            // Exception
            if (exception != null)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(exception);
            }

            Console.ResetColor();
        }

        static ConsoleColor GetColor(
            LogLevel level)
        {
            return level switch {
                LogLevel.Trace =>
                    ConsoleColor.DarkGray,

                LogLevel.Debug =>
                    ConsoleColor.Gray,

                LogLevel.Information =>
                    ConsoleColor.Green,

                LogLevel.Warning =>
                    ConsoleColor.Yellow,

                LogLevel.Error =>
                    ConsoleColor.Red,

                LogLevel.Critical =>
                    ConsoleColor.White,

                _ => throw new ArgumentOutOfRangeException(nameof(level), level, null)
            };
        }
    }
    static void OnLogLineWrote(string line)
    {
        LogFile.WriteLine(line);
        LogFile.Flush();
        LogLineCount++;
        Logging?.Invoke(line);
    }
}