using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Alife.Framework;
using Newtonsoft.Json.Linq;

namespace Alife.Plugin.LanguageModelRouter;

    /// <summary>
    /// 多路容灾切换 Handler：检测 HTTP 429/402/5xx 或响应体中的错误关键字，
    /// 自动切换到下一组 endpoint/apiKey 重试。
    /// 同时处理 SSE 流中 reasoning_content/thinking 等字段到 content 的兼容转换。
    /// </summary>
public class FallbackHandler : DelegatingHandler
{
    readonly Func<List<FallbackGroup>> getGroups;
    readonly Func<List<string>> getErrorKeywords;
    readonly Func<int> getRetryDelayMs;
    readonly Func<int> getRequestTimeoutMs;
    readonly Func<int> getForcedGroupIndex;
    readonly Func<bool> getAutoFailoverEnabled;
    readonly Action<int>? onFailover;
    readonly Func<bool>? consumeTestFlag;
    readonly Func<bool> getShowThinkingChain;
    readonly Func<bool> getThinkingMode;
    readonly Action<int>? onServed;

    static readonly string[] ReasoningKeys = {
        "reasoning_content",
        "thought",
        "thinking",
        "thought_content",
        "reasoning"
    };

    /// <summary>
    /// 内置内容安全检查类错误标记：部分渠道会对输入内容做安全审查（如阿里云百炼 data_inspection_failed、
    /// OpenAI content_filter），命中时即使 HTTP 4xx（400/403）也触发容灾——换个渠道往往能放行。
    /// 与用户配置的"错误关键字"合并生效，无需额外配置。
    /// </summary>
    static readonly string[] BuiltinFallbackMarkers =
    {
        "data_inspection_failed",    // 阿里云百炼：输入内容安全检查失败
        "content_filter",            // OpenAI / 中转渠道：内容过滤拒绝
        "content_policy_violation",  // 内容策略违规
        "inappropriate content",     // 输入内容不合规
        "unsafe_content",            // 不安全内容
        "sensitive_content",         // 敏感内容
        "moderation_failed"          // 内容审核失败
    };

    public FallbackHandler(
        HttpMessageHandler innerHandler,
        Func<List<FallbackGroup>> getGroups,
        Func<List<string>>? getErrorKeywords = null,
        Func<int>? getRetryDelayMs = null,
        Func<int>? getRequestTimeoutMs = null,
        Func<int>? getForcedGroupIndex = null,
        Func<bool>? getAutoFailoverEnabled = null,
        Action<int>? onFailover = null,
        Func<bool>? consumeTestFlag = null,
        Func<bool>? getShowThinkingChain = null,
        Func<bool>? getThinkingMode = null,
        Action<int>? onServed = null
    ) : base(innerHandler)
    {
        this.getGroups = getGroups;
        this.getErrorKeywords = getErrorKeywords ?? (() => new List<string>());
        this.getRetryDelayMs = getRetryDelayMs ?? (() => 1000);
        this.getRequestTimeoutMs = getRequestTimeoutMs ?? (() => 30000);
        this.getForcedGroupIndex = getForcedGroupIndex ?? (() => -1);
        this.getAutoFailoverEnabled = getAutoFailoverEnabled ?? (() => true);
        this.onFailover = onFailover;
        this.consumeTestFlag = consumeTestFlag;
        this.getShowThinkingChain = getShowThinkingChain ?? (() => true);
        this.getThinkingMode = getThinkingMode ?? (() => true);
        this.onServed = onServed;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var groups = getGroups() ?? new List<FallbackGroup>();
        if (groups.Count == 0)
            return new HttpResponseMessage(HttpStatusCode.BadGateway)
            {
                Content = new StringContent("灵枢：未配置任何渠道组")
            };

        int forcedIdx = getForcedGroupIndex();
        bool autoEnabled = getAutoFailoverEnabled();
        var errorKeywords = getErrorKeywords();
        int retryDelayMs = getRetryDelayMs();
        bool thinkingMode = getThinkingMode();
        int requestTimeoutMs = getRequestTimeoutMs();
        // 有效超时：<=0 视为关闭但保留 100s 兜底（等价旧 HttpClient 默认值）；>0 则下限 1s、上限 300s，
        // 保证单次尝试超时恒为唯一生效的超时，不会与 HttpClient 内置 100s 超时冲突
        int effectiveTimeoutMs = requestTimeoutMs > 0 ? Math.Clamp(requestTimeoutMs, 1000, 300000) : 100000;

        // 用户配置的关键字 + 内置内容安全检查标记，合并后统一参与响应体匹配
        var effectiveKeywords = MergeKeywords(errorKeywords, BuiltinFallbackMarkers);

        int startGroup = forcedIdx >= 0 && forcedIdx < groups.Count ? forcedIdx : 0;
        int maxAttempts = autoEnabled ? groups.Count : 1;

        HttpResponseMessage? lastResponse = null;
        Exception? lastError = null;

        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            int groupIdx = (startGroup + attempt) % groups.Count;
            var group = groups[groupIdx];

            // 每次请求都按当前组重写目标与模型，拖动排序后无需重建内核即生效
            HttpRequestMessage req = await CloneRequestAsync(request);
            req.RequestUri = BuildNewUri(request.RequestUri!, group.Endpoint.AbsoluteUri);
            if (req.Headers.Authorization != null)
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", group.ApiKey);
            await RewriteRequestBody(req, group.ModelId, group.ReasoningEffort, group.ExtraBody, group.ExtraBodyNotThinking, thinkingMode);
            if (group.ExtraHeaders != null)
            {
                foreach (var header in group.ExtraHeaders)
                    req.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            if (attempt > 0 && retryDelayMs > 0)
                await Task.Delay(retryDelayMs, cancellationToken);

            HttpResponseMessage response;
            if (attempt == 0 && consumeTestFlag?.Invoke() == true)
            {
                response = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
                {
                    Content = new StringContent("{\"error\":\"[灵枢 测试] 模拟 429\"}")
                };
            }
            else
            {
                try
                {
                    // 单次尝试限时：渠道挂起/无响应时按 effectiveTimeoutMs 快速放弃并容灾。
                    // 若不显式限时，HttpClient 默认 100s 超时且异常不走进容灾分支，切换会卡到极慢甚至直接失败。
                    using var timeoutCts = effectiveTimeoutMs > 0
                        ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
                        : null;
                    if (timeoutCts != null)
                        timeoutCts.CancelAfter(effectiveTimeoutMs);

                    response = await base.SendAsync(req, timeoutCts?.Token ?? cancellationToken);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    // 单次尝试超时（非用户主动取消）：视为该渠道不可用，继续下一组
                    lastError = new TimeoutException($"灵枢：渠道 #{groupIdx + 1} 请求超时（{effectiveTimeoutMs}ms）");
                    Console.WriteLine($"[灵枢] #{groupIdx + 1} {group.Endpoint.Host} ✗ 请求超时（{effectiveTimeoutMs}ms）" +
                        (attempt < maxAttempts - 1 ? " → 容灾切换" : "，无备用渠道可切换"));
                    continue;
                }
                catch (HttpRequestException ex)
                {
                    // 网络层错误（DNS 解析失败/连接拒绝/连接中断等）：同样视为该渠道不可用，继续容灾
                    lastError = ex;
                    Console.WriteLine($"[灵枢] #{groupIdx + 1} {group.Endpoint.Host} ✗ 网络错误：{ex.Message}" +
                        (attempt < maxAttempts - 1 ? " → 容灾切换" : "，无备用渠道可切换"));
                    continue;
                }
            }
            lastResponse = response;

            if (response.IsSuccessStatusCode)
            {
                if (attempt > 0)
                {
                    onFailover?.Invoke(groupIdx);
                }
                onServed?.Invoke(group.Slot);   // 新增：无条件上报本轮实际服务的组（含主组直接成功）
                await ProcessReasoningStreamAsync(response);
                return response;
            }

            if (!autoEnabled)
            {
                Console.WriteLine($"[灵枢] #{groupIdx + 1} {group.Endpoint.Host} ✗{(int)response.StatusCode} 容灾已关闭");
                return response;
            }

            bool shouldFallback = IsFallbackStatus(response.StatusCode);
            string? errorBody = null;
            if (!shouldFallback && effectiveKeywords.Count > 0)
            {
                // 非 429/402/5xx 时读响应体匹配关键字（含内置内容安全检查标记）。读完必须恢复 body，
                // 否则返回给上层时读到的是空串，错误信息会丢失
                errorBody = await SafeReadBodyAsync(response, cancellationToken);
                shouldFallback = errorBody != null && ContainsAnyKeyword(errorBody, effectiveKeywords);
                if (errorBody != null)
                    RestoreBody(response, errorBody);
            }

            if (!shouldFallback)
            {
                Console.WriteLine($"[灵枢] #{groupIdx + 1} {group.Endpoint.Host} ✗{(int)response.StatusCode}");
                return response;
            }

            if (attempt < maxAttempts - 1)
                Console.WriteLine($"[灵枢] #{groupIdx + 1} {group.Endpoint.Host} ✗{(int)response.StatusCode} → 容灾切换");
            else
                Console.WriteLine($"[灵枢] #{groupIdx + 1} {group.Endpoint.Host} ✗{(int)response.StatusCode}，无备用渠道可切换");

            if (attempt < maxAttempts - 1)
            {
                try { await response.Content.ReadAsStringAsync(cancellationToken); } catch { }
                response.Dispose();
                lastResponse = null; // 该响应已销毁，不能作为最终结果返回（避免最后一组超时时返回已释放对象）
            }
        }

        if (lastResponse == null)
        {
            // 所有尝试均超时/网络错误，没有可用响应：合成 504 让上层展示明确错误
            Console.WriteLine("[灵枢] 所有渠道均请求失败（超时或网络错误）");
            return new HttpResponseMessage(HttpStatusCode.GatewayTimeout)
            {
                Content = new StringContent($"灵枢：所有渠道均请求失败（最后错误：{lastError?.Message ?? "未知"}）")
            };
        }

        Console.WriteLine("[灵枢] 所有渠道均已失败");
        return lastResponse;
    }

    static bool IsFallbackStatus(HttpStatusCode status)
    {
        int code = (int)status;
        return code == 429 || code == 402 || code >= 500;
    }

    /// <summary>合并用户关键字与内置内容安全检查标记（去重，忽略大小写）</summary>
    static List<string> MergeKeywords(IReadOnlyList<string> userKeywords, IReadOnlyList<string> builtin)
    {
        var result = new List<string>(userKeywords.Count + builtin.Count);
        foreach (var kw in userKeywords)
        {
            if (!string.IsNullOrWhiteSpace(kw) && !result.Contains(kw, StringComparer.OrdinalIgnoreCase))
                result.Add(kw);
        }
        foreach (var kw in builtin)
        {
            if (!result.Contains(kw, StringComparer.OrdinalIgnoreCase))
                result.Add(kw);
        }
        return result;
    }

    static bool ContainsAnyKeyword(string body, IReadOnlyList<string> keywords)
    {
        foreach (var kw in keywords)
        {
            if (body.Contains(kw, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>安全读取响应体（读失败返回 null，不抛异常）</summary>
    static async Task<string?> SafeReadBodyAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            return await response.Content.ReadAsStringAsync(ct);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>关键字匹配读掉了响应体后，用缓存内容重建 Content，保证上层仍能读到完整错误体</summary>
    static void RestoreBody(HttpResponseMessage response, string body)
    {
        string? mediaType = response.Content.Headers.ContentType?.MediaType;
        response.Content = new StringContent(body, Encoding.UTF8, mediaType ?? "application/json");
    }

    /// <summary>
    /// 克隆 HttpRequestMessage（请求只能发送一次）
    /// </summary>
    static async Task<HttpRequestMessage> CloneRequestAsync(HttpRequestMessage original)
    {
        var clone = new HttpRequestMessage(original.Method, original.RequestUri)
        {
            Version = original.Version,
            VersionPolicy = original.VersionPolicy
        };

        // 克隆 Content
        if (original.Content != null)
        {
            byte[] contentBytes = await original.Content.ReadAsByteArrayAsync();
            clone.Content = new ByteArrayContent(contentBytes);

            // 复制 Content Headers
            foreach (var header in original.Content.Headers)
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        // 复制 Request Headers
        foreach (var header in original.Headers)
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);

        // 复制 Properties/Options
        foreach (var option in original.Options)
            clone.Options.TryAdd(option.Key, option.Value);

        return clone;
    }

    static async Task RewriteRequestBody(HttpRequestMessage req, string newModelId, string? reasoningEffort = null, IReadOnlyDictionary<string, object?>? extraBody = null, IReadOnlyDictionary<string, object?>? extraBodyNotThinking = null, bool thinkingMode = true)
    {
        if (req.Content == null) return;
        try
        {
            byte[] body = await req.Content.ReadAsByteArrayAsync();
            JObject obj = JObject.Parse(Encoding.UTF8.GetString(body));
            obj["model"] = newModelId;

            if (thinkingMode)
            {
                // 思考模式：按组配置写入推理强度
                if (!string.IsNullOrWhiteSpace(reasoningEffort))
                    obj["reasoning_effort"] = reasoningEffort;
            }
            else
            {
                // 非思考模式：先清掉 thinking / reasoning_effort，稍后显式写入禁用思考
                obj.Remove("reasoning_effort");
                obj.Remove("thinking");
            }

            void ApplyDict(IReadOnlyDictionary<string, object?> dict)
            {
                foreach (var kvp in dict)
                {
                    // ParseExtraBody 对嵌套对象/数组返回 System.Text.Json.JsonElement，
                    // 必须用 GetRawText 重新解析，否则 JToken.FromObject(JsonElement) 会损坏结构
                    // （如 thinking 嵌套对象丢失字段导致上游 400 "missing field type"）
                    if (kvp.Value is System.Text.Json.JsonElement el)
                        obj[kvp.Key] = JToken.Parse(el.GetRawText());
                    else if (kvp.Value == null)
                        obj[kvp.Key] = JValue.CreateNull();
                    else
                        obj[kvp.Key] = JToken.FromObject(kvp.Value);
                }
            }

            if (extraBody != null)
            {
                foreach (var kvp in extraBody)
                {
                    // 非思考模式下跳过 thinking / reasoning_effort，避免组配置的 ExtraBody 又把思考相关参数写回请求体
                    if (!thinkingMode && (kvp.Key.Equals("thinking", StringComparison.OrdinalIgnoreCase)
                        || kvp.Key.Equals("reasoning_effort", StringComparison.OrdinalIgnoreCase)))
                        continue;

                    // ParseExtraBody 对嵌套对象/数组返回 System.Text.Json.JsonElement，
                    // 必须用 GetRawText 重新解析，否则 JToken.FromObject(JsonElement) 会损坏结构
                    // （如 thinking 嵌套对象丢失字段导致上游 400 "missing field type"）
                    if (kvp.Value is System.Text.Json.JsonElement el)
                        obj[kvp.Key] = JToken.Parse(el.GetRawText());
                    else if (kvp.Value == null)
                        obj[kvp.Key] = JValue.CreateNull();
                    else
                        obj[kvp.Key] = JToken.FromObject(kvp.Value);
                }
            }

            if (!thinkingMode)
            {
                if (extraBodyNotThinking is { Count: > 0 })
                {
                    // 组级非思考请求体：完全由用户指定（可自定义 thinking 禁用写法）
                    ApplyDict(extraBodyNotThinking);
                }
                else
                {
                    // 默认对齐官方 extraBodyNotThinking：显式禁用思考，
                    // 解决个别渠道（如硅基）不传 thinking 参数时仍输出思维链的问题
                    obj["thinking"] = JObject.Parse("{\"type\":\"disabled\"}");
                }
            }

            byte[] newBody = Encoding.UTF8.GetBytes(obj.ToString(Newtonsoft.Json.Formatting.None));
            req.Content = new ByteArrayContent(newBody);
            req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[灵枢] 请求体重写失败（按原请求发送）: {ex.Message}");
        }
    }

    /// <summary>
    /// 将原始 URI 的 scheme+host+port 替换为新 endpoint，保留原始路径和查询参数
    /// </summary>
    static Uri BuildNewUri(Uri originalUri, string newEndpoint)
    {
        if (!Uri.TryCreate(newEndpoint, UriKind.Absolute, out var endpointUri))
            return new Uri(newEndpoint);

        string basePath = endpointUri.AbsolutePath.TrimEnd('/');
        string path = basePath.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)
            ? basePath
            : basePath + "/chat/completions";
        string query = originalUri.Query;

        var builder = new UriBuilder(endpointUri)
        {
            Path = path,
            Query = query
        };
        return builder.Uri;
    }

    /// <summary>
    /// 处理 SSE 流中的 reasoning_content → content 转换
    /// </summary>
    async Task ProcessReasoningStreamAsync(HttpResponseMessage response)
    {
        if (response.Content.Headers.ContentType?.MediaType == "text/event-stream")
        {
            Stream stream = await response.Content.ReadAsStreamAsync();
            response.Content = new StreamContent(new CompatibleStreamWrapper(stream, getShowThinkingChain));
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("text/event-stream");
        }
    }

    class CompatibleStreamWrapper : Stream
    {
        readonly Stream innerStream;
        readonly StreamReader reader;
        readonly MemoryStream outputBuffer = new();
        readonly Func<bool> getShowThinkingChain;

        public CompatibleStreamWrapper(Stream innerStream, Func<bool> getShowThinkingChain)
        {
            this.innerStream = innerStream;
            this.reader = new StreamReader(innerStream, Encoding.UTF8);
            this.getShowThinkingChain = getShowThinkingChain;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (outputBuffer.Position >= outputBuffer.Length)
            {
                outputBuffer.SetLength(0);
                outputBuffer.Position = 0;

                string? line = await reader.ReadLineAsync(cancellationToken);
                if (line == null) return 0;

                string processedLine = ProcessLine(line) + "\n";
                byte[] bytes = Encoding.UTF8.GetBytes(processedLine);
                outputBuffer.Write(bytes);
                outputBuffer.Position = 0;
            }

            int count = await outputBuffer.ReadAsync(buffer, cancellationToken);
            return count;
        }

        private string ProcessLine(string line)
        {
            // 兼容两种 SSE 行格式："data: {...}"（带空格）与 "data:{...}"（无空格）
            if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return line;

            string jsonPart = line.Substring(5).Trim();
            if (string.IsNullOrWhiteSpace(jsonPart) || jsonPart == "[DONE]") return line;

            try
            {
                JObject obj = JObject.Parse(jsonPart);
                // choices 可能为空数组（完成帧/异常帧），直接索引 [0] 会越界
                JToken? delta = null;
                if (obj["choices"] is JArray jChoices && jChoices.Count > 0)
                    delta = jChoices[0]?["delta"];
                if (delta is JObject deltaObj)
                {
                    bool showThinking = getShowThinkingChain();
                    foreach (var key in ReasoningKeys)
                    {
                        JToken? reasoning = deltaObj[key];
                        if (reasoning != null && reasoning.Type != JTokenType.Null)
                        {
                            string val = reasoning.ToString();
                            if (!string.IsNullOrEmpty(val))
                            {
                                if (showThinking)
                                {
                                    JToken? curContent = deltaObj["content"];
                                    bool hasContent = curContent != null && curContent.Type != JTokenType.Null
                                        && !string.IsNullOrEmpty(curContent.ToString());
                                    if (!hasContent)
                                        deltaObj["content"] = $"{LanguageModelRouter.ThinkContentPrefix}{val}";
                                }
                                deltaObj.Remove(key);
                                break;
                            }
                        }
                    }
                }
                return "data: " + obj.ToString(Newtonsoft.Json.Formatting.None);
            }
            catch
            {
                return line;
            }
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        // 底层是网络流不可 Seek，访问 Length 会抛 NotSupportedException；返回 0 保持健壮
        public override long Length => 0;
        public override long Position { get => innerStream.Position; set => throw new NotSupportedException(); }
        public override void Flush() => innerStream.Flush();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException("请使用 ReadAsync");
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                reader.Dispose();
                innerStream.Dispose();
                outputBuffer.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}

public record FallbackGroup(int Slot, Uri Endpoint, string ModelId, string ApiKey, IReadOnlyDictionary<string, string>? ExtraHeaders = null, string? ReasoningEffort = null, IReadOnlyDictionary<string, object?>? ExtraBody = null, IReadOnlyDictionary<string, object?>? ExtraBodyNotThinking = null);