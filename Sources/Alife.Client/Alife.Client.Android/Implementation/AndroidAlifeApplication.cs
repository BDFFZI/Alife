using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Alife.Foundation;
using Alife.Framework;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Alife.Client.Android;

/// <summary>
/// Android 平台前端应用：文件对话框（不支持）、自定义窗口，以及基于 TcpListener 的 MCP 服务器。
/// MCP 使用极小的 HTTP 层，协议处理交给 ModelContextProtocol.Core 的 StreamableHttpServerTransport。
/// </summary>
sealed class AndroidAlifeApplication(
    AlifeClientConfig config,
    IServiceProvider provider,
    AndroidWindowManager windowManager) : IAlifeApplication
{
    #region 文件对话框功能

    public Task RevealFolderAsync(string path)
    {
        AndroidFileDialogs.RevealFolder(path);
        return Task.CompletedTask;
    }
    public Task RevealFileAsync(string path)
    {
        AndroidFileDialogs.RevealFile(path);
        return Task.CompletedTask;
    }
    public Task<string?> SelectFolderAsync()
    {
        return AndroidFileDialogs.SelectFolderAsync(CancellationToken.None);
    }

    #endregion

    #region 自定义窗口功能

    public System.Drawing.Size GetScreenSize() => AndroidDisplay.GetSize();
    public System.Drawing.Point GetCursorPosition()
    {
        AndroidPointerMonitor.EnsureStarted();
        if (Interlocked.Read(ref AndroidAlifeClient.LastCursor) < 0)
        {
            System.Drawing.Size size = GetScreenSize();
            return new(size.Width / 2, size.Height / 2);
        }
        return AndroidAlifeClient.LastCursorPosition;
    }
    public float GetDpiScale() => AndroidDisplay.Density;

    public async Task<IAlifeWindow> CreateWindowAsync(AlifeWindowOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return await AndroidWindowManager.RunOnUi(() => windowManager.Create(options));
    }

    #endregion

    #region Python

    public IAlifeProcess StartPython(string scriptPath, string? pythonExe = null)
    {
        if (pythonExe is not null && pythonExe != "python")
            throw new PlatformNotSupportedException("Android 使用 Shizuku 安装的 Python。");
        return AndroidAlifePlatform.StartPython(scriptPath);
    }

    #endregion

    #region AlifeMcp功能

    public Uri? AlifeMcpServer { get; private set; }

    public Task StartAlifeMcpAsync(IServiceProvider provider)
    {
        Uri uri = new(config.McpServerAddress);
        string route = string.IsNullOrWhiteSpace(config.McpServerPath) ? "/mcp" : config.McpServerPath;
        AlifeMcpTools alifeMcpTools = ActivatorUtilities.CreateInstance<AlifeMcpTools>(provider);
        McpServerOptions options = CreateOptions(provider, alifeMcpTools);
        ILoggerFactory loggerFactory = provider.GetService<ILoggerFactory>() ?? NullLoggerFactory.Instance;

        TcpListener listener = new(IPAddress.Loopback, uri.Port);
        listener.Start();
        _ = AcceptLoopAsync(listener, provider, options, loggerFactory, route);
        AlifeMcpServer = new Uri($"http://127.0.0.1:{uri.Port}{route}");
        AlifeLog.LogInformation($"Android MCP 服务器已启动: {AlifeMcpServer}");
        return Task.CompletedTask;
    }

    static McpServerOptions CreateOptions(IServiceProvider provider, AlifeMcpTools alifeMcpTools)
    {
        McpServerOptions options = new() {
            ServerInfo = new Implementation {
                Name = "Alife MCP Server",
                Version = AlifeContext.AppVersion,
                Title = "Alife MCP Server",
                Description = "Alife 是一个 AIAgent 智能体，除了 MCP 控制规范图形界面，通过 AI 友好的方式对话或聆听，多端在线可看可开发可设置",
                WebsiteUrl = "https://github.com/BDFFZI/Alife"
            }
        };

        options.Filters.Request.CallToolFilters.Add(next => async (request, cancellationToken) => {
            try
            {
                return await next(request, cancellationToken);
            }
            catch (Exception e) when (e is not OperationCanceledException && e is not McpProtocolException)
            {
                return new CallToolResult {
                    IsError = true,
                    Content = [new TextContentBlock { Text = e.ToString() }],
                };
            }
        });

        options.ToolCollection ??= new McpServerPrimitiveCollection<McpServerTool>();

        foreach (MethodInfo method in typeof(AlifeMcpTools).GetMethods(BindingFlags.Public | BindingFlags.Instance))
        {
            if (method.GetCustomAttribute<McpServerToolAttribute>() == null)
                continue;
            options.ToolCollection.Add(McpServerTool.Create(method, alifeMcpTools, new McpServerToolCreateOptions {
                Services = provider
            }));
        }

        return options;
    }

    static async Task AcceptLoopAsync(
        TcpListener listener,
        IServiceProvider provider,
        McpServerOptions options,
        ILoggerFactory loggerFactory,
        string route)
    {
        ConcurrentDictionary<string, Session> sessions = new();
        try
        {
            while (true)
            {
                TcpClient client = await listener.AcceptTcpClientAsync();
                _ = HandleClientAsync(client, provider, options, loggerFactory, route, sessions);
            }
        }
        catch (Exception e)
        {
            AlifeLog.LogError(e);
        }
    }

    static async Task HandleClientAsync(
        TcpClient client,
        IServiceProvider provider,
        McpServerOptions options,
        ILoggerFactory loggerFactory,
        string route,
        ConcurrentDictionary<string, Session> sessions)
    {
        using (client)
        {
            try
            {
                await using NetworkStream stream = client.GetStream();
                HttpRequest? request = await ReadRequestAsync(stream, CancellationToken.None);
                if (request == null)
                    return;
                if (string.Equals(request.Path, route, StringComparison.Ordinal) == false)
                {
                    await WriteResponseAsync(stream, 404, "Not Found", [], null, null);
                    return;
                }

                switch (request.Method)
                {
                    case "POST":
                        await HandlePostAsync(stream, request, provider, options, loggerFactory, sessions);
                        break;
                    case "GET":
                        await HandleGetAsync(stream, request, sessions);
                        break;
                    case "DELETE":
                        await HandleDeleteAsync(stream, request, sessions);
                        break;
                    default:
                        await WriteResponseAsync(stream, 405, "Method Not Allowed", [], null, null);
                        break;
                }
            }
            catch (Exception e)
            {
                AlifeLog.LogError(e);
            }
        }
    }

    static async Task HandlePostAsync(
        NetworkStream stream,
        HttpRequest request,
        IServiceProvider provider,
        McpServerOptions options,
        ILoggerFactory loggerFactory,
        ConcurrentDictionary<string, Session> sessions)
    {
        JsonRpcMessage? message = JsonSerializer.Deserialize<JsonRpcMessage>(request.Body, McpJsonUtilities.DefaultOptions);
        if (message == null)
        {
            await WriteResponseAsync(stream, 400, "Bad Request", [], null, null);
            return;
        }

        string? sessionId = request.GetHeader("Mcp-Session-Id");
        Session? session;
        if (string.IsNullOrEmpty(sessionId))
        {
            if (message is not JsonRpcRequest { Method: "initialize" })
            {
                await WriteResponseAsync(stream, 400, "Bad Request", [], null, null);
                return;
            }

            sessionId = Guid.NewGuid().ToString("N");
            StreamableHttpServerTransport transport = new(loggerFactory) {
                SessionId = sessionId
            };
            McpServer server = McpServer.Create(transport, options, loggerFactory, provider);
            CancellationTokenSource cts = new();
            Task runTask = server.RunAsync(cts.Token);
            session = new Session(transport, cts, runTask);
            sessions[sessionId] = session;
        }
        else if (sessions.TryGetValue(sessionId, out Session? existing) == false)
        {
            await WriteResponseAsync(stream, 404, "Not Found", [], null, null);
            return;
        }
        else
        {
            session = existing;
        }

        using MemoryStream response = new();
        bool wrote = await session.Transport.HandlePostRequestAsync(message, response, CancellationToken.None);
        byte[] body = response.ToArray();
        if (wrote)
            await WriteResponseAsync(stream, 200, "OK", body, "text/event-stream", sessionId);
        else
            await WriteResponseAsync(stream, 202, "Accepted", [], null, sessionId);
    }

    static async Task HandleGetAsync(NetworkStream stream, HttpRequest request, ConcurrentDictionary<string, Session> sessions)
    {
        string? sessionId = request.GetHeader("Mcp-Session-Id");
        if (string.IsNullOrEmpty(sessionId) || sessions.TryGetValue(sessionId, out Session? session) == false)
        {
            await WriteResponseAsync(stream, 404, "Not Found", [], null, null);
            return;
        }

        string headers =
            $"HTTP/1.1 200 OK\r\nContent-Type: text/event-stream\r\nCache-Control: no-cache\r\nMcp-Session-Id: {sessionId}\r\nConnection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(headers));
        await stream.FlushAsync();
        await session.Transport.HandleGetRequestAsync(stream, CancellationToken.None);
    }

    static async Task HandleDeleteAsync(NetworkStream stream, HttpRequest request, ConcurrentDictionary<string, Session> sessions)
    {
        string? sessionId = request.GetHeader("Mcp-Session-Id");
        if (string.IsNullOrEmpty(sessionId) || sessions.TryRemove(sessionId, out Session? session) == false)
        {
            await WriteResponseAsync(stream, 404, "Not Found", [], null, null);
            return;
        }

        await session.DisposeAsync();
        await WriteResponseAsync(stream, 200, "OK", [], null, null);
    }

    static async Task WriteResponseAsync(
        NetworkStream stream,
        int status,
        string reason,
        byte[] body,
        string? contentType,
        string? sessionId)
    {
        StringBuilder headers = new();
        headers.Append($"HTTP/1.1 {status} {reason}\r\n");
        if (contentType != null)
            headers.Append($"Content-Type: {contentType}\r\n");
        if (sessionId != null)
            headers.Append($"Mcp-Session-Id: {sessionId}\r\n");
        headers.Append($"Content-Length: {body.Length}\r\n");
        headers.Append("Connection: close\r\n\r\n");

        await stream.WriteAsync(Encoding.ASCII.GetBytes(headers.ToString()));
        if (body.Length > 0)
            await stream.WriteAsync(body);
        await stream.FlushAsync();
    }

    static async Task<HttpRequest?> ReadRequestAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        string? requestLine = await ReadLineAsync(stream, cancellationToken);
        if (string.IsNullOrWhiteSpace(requestLine))
            return null;

        string[] parts = requestLine.Split(' ', 3);
        if (parts.Length < 3)
            return null;

        Dictionary<string, string> headers = new(StringComparer.OrdinalIgnoreCase);
        while (true)
        {
            string? line = await ReadLineAsync(stream, cancellationToken);
            if (string.IsNullOrEmpty(line))
                break;
            int index = line.IndexOf(':');
            if (index <= 0)
                continue;
            headers[line[..index].Trim()] = line[(index + 1)..].Trim();
        }

        if (headers.TryGetValue("Expect", out string? expect) && expect.Contains("100-continue", StringComparison.OrdinalIgnoreCase))
            await stream.WriteAsync("HTTP/1.1 100 Continue\r\n\r\n"u8.ToArray(), cancellationToken);

        byte[] body;
        // The MCP .NET client streams JSON using HTTP/1.1 chunked encoding.
        if (headers.TryGetValue("Transfer-Encoding", out string? encoding) &&
            encoding.Equals("chunked", StringComparison.OrdinalIgnoreCase))
        {
            using MemoryStream decoded = new();
            while (true)
            {
                string line = await ReadLineAsync(stream, cancellationToken)
                              ?? throw new EndOfStreamException();
                int size = int.Parse(line.Split(';', 2)[0], System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture);
                if (size == 0)
                {
                    while (!string.IsNullOrEmpty(await ReadLineAsync(stream, cancellationToken))) { }
                    break;
                }
                byte[] chunk = new byte[size];
                await stream.ReadExactlyAsync(chunk, cancellationToken);
                await decoded.WriteAsync(chunk, cancellationToken);
                if (await ReadLineAsync(stream, cancellationToken) != "")
                    throw new InvalidDataException("Invalid HTTP chunk terminator.");
            }
            body = decoded.ToArray();
        }
        else
        {
            int contentLength = headers.TryGetValue("Content-Length", out string? lengthText) && int.TryParse(lengthText, out int parsed)
                ? parsed
                : 0;
            body = new byte[contentLength];
            await stream.ReadExactlyAsync(body, cancellationToken);
        }

        return new HttpRequest(parts[0], parts[1], headers, body);
    }

    static async Task<string?> ReadLineAsync(Stream stream, CancellationToken cancellationToken)
    {
        StringBuilder line = new();
        byte[] one = new byte[1];
        while (true)
        {
            int read = await stream.ReadAsync(one, cancellationToken);
            if (read == 0)
                return line.Length == 0 ? null : line.ToString();
            if (one[0] == '\n')
            {
                if (line.Length > 0 && line[^1] == '\r')
                    line.Length--;
                return line.ToString();
            }
            line.Append((char)one[0]);
            if (line.Length > 8192)
                throw new InvalidDataException("HTTP header line too long.");
        }
    }

    sealed record HttpRequest(string Method, string Path, Dictionary<string, string> Headers, byte[] Body)
    {
        public string? GetHeader(string name) => Headers.GetValueOrDefault(name);
    }

    sealed record Session(
        StreamableHttpServerTransport Transport,
        CancellationTokenSource Cancellation,
        Task RunTask) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await Cancellation.CancelAsync();
            await Transport.DisposeAsync();
            try
            {
                await RunTask;
            }
            catch (OperationCanceledException) { }
            Cancellation.Dispose();
        }
    }

    #endregion
}