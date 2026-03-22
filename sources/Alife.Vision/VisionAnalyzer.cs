using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Alife.Vision;

/// <summary>
/// 使用 InternVL2.5-1B 进行图像理解。
/// 内部维护一个长驻 Python 子进程，模型只加载一次。
/// </summary>
public class VisionAnalyzer : IDisposable
{
    private Process? _process;
    private StreamWriter? _stdin;
    private StreamReader? _stdout;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private bool _ready = false;
    private bool _disposed = false;

    /// <summary>
    /// 模型生成的最大字长限制（Token 数量）。
    /// </summary>
    public int MaxResponseTokens { get; set; } = 100;

    /// <summary>
    /// 启动 Python Bridge 并等待模型加载完成。
    /// 首次运行会自动从 HuggingFace 下载模型（约 6GB），之后离线可用。
    /// </summary>
    /// <param name="scriptPath">qwen_vision_bridge.py 的路径，默认为当前程序目录</param>
    /// <param name="timeoutSeconds">等待模型加载的超时时间（秒），默认 120s</param>
    public async Task InitAsync(string? scriptPath = null, string? modelPath = null, int timeoutSeconds = 120, Action<string>? onLog = null)
    {
        if (_ready) return;

        string script = scriptPath
                        ?? Path.Combine(AppContext.BaseDirectory, "qwen_vision_bridge.py");

        if (!File.Exists(script))
            throw new FileNotFoundException($"Vision bridge script not found: {script}");

        string arguments = $"\"{script}\"";
        if (!string.IsNullOrEmpty(modelPath))
        {
            arguments += $" --model_path \"{modelPath}\"";
        }

        var psi = new ProcessStartInfo {
            FileName = PathEnvironment.PythonExecutablePath,
            Arguments = arguments,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = new UTF8Encoding(false),
        };
        psi.Environment["PYTHONIOENCODING"] = "utf-8";

        _process = Process.Start(psi)
                   ?? throw new InvalidOperationException("Failed to start Python vision bridge.");

        _stdin = _process.StandardInput;
        _stdout = _process.StandardOutput;

        if (onLog != null)
        {
            _ = Task.Run(async () => {
                var stderr = _process.StandardError;
                char[] buffer = new char[256];
                int read;
                try
                {
                    while ((read = await stderr.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        onLog(new string(buffer, 0, read));
                    }
                }
                catch { }
            });
        }

        // 等待 "READY" 信号
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        try
        {
            while (!cts.Token.IsCancellationRequested)
            {
                string? line = await _stdout.ReadLineAsync(cts.Token);
                if (line == null) throw new InvalidOperationException("Python bridge process exited unexpectedly during startup.");
                if (line == "READY")
                {
                    _ready = true;
                    return;
                }
                // 可能是错误信息
                if (line.StartsWith("{"))
                {
                    var err = JsonNode.Parse(line);
                    throw new InvalidOperationException($"Vision bridge startup error: {err?["message"]}");
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException($"Vision bridge did not become ready within {timeoutSeconds}s.");
        }
    }

    /// <summary>
    /// 视觉问答：用中文提问，获得中文回答。
    /// </summary>
    public Task<string> QueryAsync(string imagePath, string question, int? maxResponseTokens = null, CancellationToken ct = default)
    {
        return SendRequestAsync(new { action = "query", image_path = imagePath, question, max_new_tokens = maxResponseTokens }, ct);
    }

    // ─────────────────────────────────────────────────

    private async Task<string> SendRequestAsync(object request, CancellationToken ct)
    {
        if (!_ready)
            throw new InvalidOperationException("VisionAnalyzer is not initialized. Call InitAsync() first.");

        await _lock.WaitAsync(ct);
        try
        {
            // 如果请求对象中没有指定 max_new_tokens，则使用默认配置
            var requestNode = JsonNode.Parse(JsonSerializer.Serialize(request));
            if (requestNode != null && requestNode["max_new_tokens"] == null)
            {
                requestNode["max_new_tokens"] = MaxResponseTokens;
            }

            string json = requestNode?.ToJsonString() ?? JsonSerializer.Serialize(request);
            await _stdin!.WriteLineAsync(json.AsMemory(), ct);
            await _stdin.FlushAsync(ct);

            string? response = await _stdout!.ReadLineAsync(ct);
            if (response == null)
                throw new InvalidOperationException("Python bridge closed unexpectedly.");

            var node = JsonNode.Parse(response);
            string? status = node?["status"]?.GetValue<string>();

            if (status == "ok")
                return node!["result"]?.GetValue<string>() ?? string.Empty;

            string message = node?["message"]?.GetValue<string>() ?? "Unknown error";
            throw new InvalidOperationException($"Vision bridge error: {message}");
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try { _stdin?.Close(); }
        catch { }
        try { _process?.Kill(); }
        catch { }
        try { _process?.Dispose(); }
        catch { }
        _lock.Dispose();

        GC.SuppressFinalize(this);
    }
}
