using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Alife.Foundation;

namespace Alife.Function.AIModelUtility;

public class PythonException(string message) : Exception(message);

public sealed class PythonPipeProcess(string scriptName, string pythonCode, string? pythonExe = null) : IAsyncDisposable
{
    public async Task StartAsync(CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(scriptPath)!);
        await File.WriteAllTextAsync(scriptPath, BoilerplateHeader + "\n" + pythonCode + "\n" + BoilerplateFooter, ct);

        ProcessStartInfo psi = new() {
            FileName = pythonExe,
            Arguments = $"\"{scriptPath}\"",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            Environment = { { "PYTHONIOENCODING", "utf-8" }, { "PYTHONUTF8", "1" } },
        };

        process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        process.ErrorDataReceived += (_, e) => {
            if (string.IsNullOrEmpty(e.Data) == false)
                AlifeLog.LogInformation(e.Data); //python用错误流输出普通log，也是醉了
        };
        process.OutputDataReceived += (_, e) => {
            if (string.IsNullOrEmpty(e.Data) == false)
                AlifeLog.LogInformation(e.Data);
        };

        process.Start();

        process.BeginErrorReadLine();
        stdin = process.StandardInput;
        stdout = process.StandardOutput;
        callLock = new SemaphoreSlim(1, 1);
    }
    public async Task<T> InvokeAsync<T>(string funcName, params object[] args)
    {
        JsonElement result = await InvokeAsync(funcName, args);
        return result.Deserialize<T>(jsonOptions)!;
    }
    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;

        if (process is not null && !process.HasExited)
        {
            try
            {
                await stdin!.WriteLineAsync("""{"func":"__shutdown__","args":[]}""");
                await stdin.FlushAsync();

                if (process.WaitForExit(3000) == false)
                    process.Kill();
            }
            catch
            {
                try
                {
                    process.Kill();
                }
                catch (Exception e)
                {
                    AlifeLog.LogError(e);
                }
            }
        }

        if (stdin != null)
            await stdin.DisposeAsync();

        stdout?.Dispose();
        process?.Dispose();
        callLock?.Dispose();

        try
        {
            File.Delete(scriptPath);
        }
        catch (Exception e)
        {
            AlifeLog.LogError(e);
        }
    }

    const string BoilerplateHeader = """
                                     import sys, json

                                     def __alife_run():
                                         for line in sys.stdin:
                                             line = line.strip()
                                             if not line:
                                                 continue
                                             try:
                                                 msg = json.loads(line)
                                             except:
                                                 continue
                                             func_name = msg.get('func', '')
                                             if func_name == '__shutdown__':
                                                 break
                                             args = msg.get('args', [])
                                             kwargs = msg.get('kwargs', {})
                                             try:
                                                 func = globals()[func_name]
                                                 if isinstance(args, list) and len(args) == 1 and isinstance(args[0], dict) and not kwargs:
                                                     result = func(**args[0])
                                                 else:
                                                     result = func(*args, **kwargs)
                                                 print(json.dumps({"ok": True, "result": result}, ensure_ascii=False), flush=True)
                                             except Exception as e:
                                                 print(json.dumps({"ok": False, "error": f"{type(e).__name__}: {e}"}, ensure_ascii=False), flush=True)
                                     """;
    const string BoilerplateFooter = """

                                     if __name__ == '__main__':
                                         __alife_run()
                                     """;

    readonly string pythonExe = pythonExe ?? "python";
    readonly string scriptPath = Path.Combine(AlifePath.TempFolderPath, "python_pipe", $"{scriptName}.py");

    readonly JsonSerializerOptions jsonOptions = new() {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    Process? process;
    StreamWriter? stdin;
    StreamReader? stdout;
    SemaphoreSlim? callLock;
    bool disposed;

    async Task<JsonElement> InvokeAsync(string funcName, params object[] args)
    {
        EnsureStarted();
        await callLock!.WaitAsync();
        try
        {
            await WriteRequestAsync(funcName, args);
            return await ReadResponseAsync();
        }
        finally
        {
            callLock.Release();
        }
    }
    async Task WriteRequestAsync(string funcName, object[] args)
    {
        object payload = new { func = funcName, args };
        string json = JsonSerializer.Serialize(payload, jsonOptions);
        await stdin!.WriteLineAsync(json.AsMemory());
        await stdin.FlushAsync();
    }
    async Task<JsonElement> ReadResponseAsync()
    {
        while (true)
        {
            string? line = await stdout!.ReadLineAsync();

            if (line is null)
                throw new PythonException("Python 进程已退出，未收到响应");

            if (string.IsNullOrWhiteSpace(line))
                continue;

            if (line.AsSpan().TrimStart()[0] != '{')
                continue;

            JsonDocument doc = JsonDocument.Parse(line);
            JsonElement root = doc.RootElement;

            if (root.TryGetProperty("ok", out JsonElement ok))
            {
                if (ok.GetBoolean() == false)
                {
                    string error = root.TryGetProperty("error", out JsonElement err) ? err.GetString() ?? "" : "";
                    throw new PythonException(error);
                }

                if (root.TryGetProperty("result", out JsonElement result))
                    return result.Clone();

                return default;
            }
        }
    }
    void EnsureStarted()
    {
        if (process is null || process.HasExited)
            throw new InvalidOperationException($"Python 进程 '{scriptName}' 未启动或已退出，请先调用 StartAsync()");
    }
}