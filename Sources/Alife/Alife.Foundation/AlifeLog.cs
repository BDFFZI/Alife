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
    public static void Initialize(IAlifeFoundation alifeFoundation)
    {
        LogFilePath = Path.Combine(alifeFoundation.TempFolderPath, "alife.log");

        //用「共享读 + 追加」方式打开：就算上一次进程的日志句柄还没释放，
        //也能继续写而不抛 IOException；同时旧日志不会被覆盖丢失，便于排查上一轮崩溃。
        logFile?.Dispose();
        try
        {
            logFile = new StreamWriter(new FileStream(
                LogFilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite)) {
                AutoFlush = true
            };
        }
        catch
        {
            //极端情况下（目录被删/权限不足）日志打不开也不能让启动失败：
            //退化到 Null writer，控制台仍然可见
            logFile = new StreamWriter(Stream.Null);
        }

        //保存原始输出，供 Shutdown 还原；避免重复初始化时层层包裹
        originalOut ??= Console.Out;
        originalError ??= Console.Error;
        Console.SetOut(new AlifeConsoleWriter(originalOut, OnLogLineWrote));
        Console.SetError(new AlifeConsoleWriter(originalError, OnLogLineWrote));
    }

    /// <summary>
    /// 关闭日志：Flush 并释放文件句柄，还原 Console。
    /// 应用退出前必须调用，否则 alife.log 会被本进程占着，
    /// 下一次启动时删除 Temp 目录就会失败（典型 IOException）。
    /// </summary>
    public static void Shutdown()
    {
        try
        {
            if (originalOut != null) Console.SetOut(originalOut);
            if (originalError != null) Console.SetError(originalError);
        }
        catch { }
        finally
        {
            originalOut = null;
            originalError = null;
        }

        lock (Lock)
        {
            try
            {
                logFile?.Flush();
                logFile?.Dispose();
            }
            catch { }
            finally
            {
                logFile = null!;
            }
        }
    }

    public static string LogFilePath { get; private set; } = null!;
    public static int LogLineCount { get; private set; }
    public static event Action<string>? WarningLogged;
    public static event Action<string>? ErrorLogged;
    public static event Action<string>? Logging;

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
    static StreamWriter logFile = null!;
    static TextWriter? originalOut;
    static TextWriter? originalError;
    //Android 等平台没有控制台颜色，设置会抛 PlatformNotSupportedException。
    static readonly bool SupportsConsoleColor =
        OperatingSystem.IsAndroid() == false && OperatingSystem.IsIOS() == false;

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
            WriteConsole(logLevel, category, message, exception);

            string fullMessage = message + (exception == null ? string.Empty : Environment.NewLine + exception);
            if (logLevel == LogLevel.Warning)
                WarningLogged?.Invoke(fullMessage);
            else if (logLevel == LogLevel.Error)
                ErrorLogged?.Invoke(fullMessage);
        }

        static void WriteConsole(
            LogLevel logLevel,
            string category,
            string message,
            Exception? exception)
        {
            // 时间
            SetColor(ConsoleColor.DarkGray);
            Console.Write(
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ");

            // Level
            SetColor(GetColor(logLevel));
            Console.Write(
                $"[{logLevel}] ");

            // Category
            if (string.IsNullOrEmpty(category) == false)
            {
                SetColor(ConsoleColor.Cyan);
                Console.Write(
                    $"{category}: ");
            }

            // Message
            SetColor(ConsoleColor.White);
            Console.WriteLine(message);

            // Exception
            if (exception != null)
            {
                SetColor(ConsoleColor.Red);
                Console.WriteLine(exception);
            }

            if (SupportsConsoleColor)
                Console.ResetColor();
        }

        static void SetColor(ConsoleColor color)
        {
            if (SupportsConsoleColor)
                Console.ForegroundColor = color;
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
        //进程退出阶段 Shutdown 可能已释放 logFile，此时控制台仍可能有输出
        StreamWriter? writer = logFile;
        if (writer != null)
        {
            try
            {
                writer.WriteLine(line);
                writer.Flush();
            }
            catch { /*日志写失败不能影响业务，更不能抛到 Console.SetOut 里 */ }
        }
        LogLineCount++;
        Logging?.Invoke(line);
    }
}