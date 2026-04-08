using Alife.Basic;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Alife.Function.QChat;

public class OneBotClient : IAsyncDisposable
{
    private ClientWebSocket _ws = new();
    private readonly OneBotConfig _config;
    private long _botId = 0;

    public event Action<OneBotEvent>? OnMessageReceived;
    public event Action<bool>? OnConnectionStatusChanged;

    public long BotId => _botId;

    public OneBotClient(OneBotConfig config)
    {
        _config = config;
    }

    public async Task ConnectAsync()
    {
        try
        {
            if (_ws.State == WebSocketState.Open) return;
            _ws = new ClientWebSocket();
            await _ws.ConnectAsync(new Uri(_config.Url), CancellationToken.None);
            OnConnectionStatusChanged?.Invoke(true);

            // 主动请求登录信息以获取 Bot ID
            await SendActionAsync("get_login_info", new { }, "init_bot_id");

            _ = Task.Run(ReceiveLoop);

            //等待收到qq后，证明连接完成
            await Task.Run(() => {
                while (_botId == 0) { Thread.Sleep(500); }
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[OneBotClient] 连接失败: {ex.Message}");
            OnConnectionStatusChanged?.Invoke(false);
        }
    }

    public async Task SendActionAsync(string action, object? @params = null, string? echo = null)
    {
        if (_ws.State != WebSocketState.Open) return;

        var payload = new OneBotAction {
            Action = action,
            Params = @params,
            Echo = echo
        };

        var json = JsonConvert.SerializeObject(payload);
        await _ws.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(json)),
            WebSocketMessageType.Text, true, CancellationToken.None);
    }

    private async Task ReceiveLoop()
    {
        var buffer = new byte[1024 * 64];
        try
        {
            while (_ws.State == WebSocketState.Open)
            {
                var result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                if (result.MessageType == WebSocketMessageType.Close) break;

                var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                await HandleMessage(json);
            }
        }
        catch (Exception ex)
        {
            if (_ws.State != WebSocketState.Aborted)
                Console.WriteLine($"[OneBotClient] 链路异常: {ex.Message}");
            OnConnectionStatusChanged?.Invoke(false);
            await Task.Delay(5000);
            _ = ConnectAsync();
        }
    }

    private async Task HandleMessage(string json)
    {
        try
        {
            var data = JsonConvert.DeserializeObject<OneBotEvent>(json);
            if (data == null) return;

            // 处理 API 响应（如 get_login_info）
            if (data.Echo == "init_bot_id")
            {
                var userIdNode = data.Data?["user_id"];
                if (userIdNode != null)
                {
                    _botId = userIdNode.Value<long>();
                }
                return;
            }

            // 同步 Bot ID
            if (data.SelfId != 0 && _botId != data.SelfId)
            {
                _botId = data.SelfId;
            }

            if (data.PostType == "message")
            {
                OnMessageReceived?.Invoke(data);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[OneBotClient] 处理消息异常: {ex.Message}");
        }
    }

    public string StringifyMessage(JToken? token)
    {
        if (token == null) return string.Empty;
        if (token.Type == JTokenType.String) return token.ToString();
        if (token.Type == JTokenType.Array)
        {
            var sb = new StringBuilder();
            foreach (var item in token)
            {
                string segmentType = item["type"]?.ToString() ?? "text";
                var dataObj = item["data"] as JObject;
                if (segmentType == "text")
                {
                    sb.Append(dataObj?["text"]?.ToString());
                }
                else if (segmentType == "at")
                {
                    sb.Append($"[CQ:at,qq={dataObj?["qq"]}]");
                }
                else if (segmentType == "face")
                {
                    sb.Append($"[CQ:face,id={dataObj?["id"]}]");
                }
                else if (segmentType == "image")
                {
                    sb.Append($"[CQ:image,file={dataObj?["url"]}]");
                }
                else
                {
                    var props = dataObj?.Properties();
                    var args = props != null ? string.Join(",", props.Select(p => $"{p.Name}={p.Value}")) : "";
                    sb.Append($"[CQ:{segmentType}{(string.IsNullOrEmpty(args) ? "" : "," + args)}]");
                }
            }
            return sb.ToString();
        }
        return token.ToString();
    }

    public async ValueTask DisposeAsync()
    {
        if (_ws != null)
        {
            if (_ws.State == WebSocketState.Open)
                await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Disposing", CancellationToken.None);
            _ws.Dispose();
        }
    }
}
