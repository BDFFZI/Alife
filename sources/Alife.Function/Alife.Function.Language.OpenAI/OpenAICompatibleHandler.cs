using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Alife.Function.Language.OpenAI;

/// <summary>
/// 通用的 OpenAI 兼容协议处理器，用于拦截各种厂商的原始流，
/// 自动识别并捕获思维链内容（reasoning_content, thought, thinking 等），
/// 将其统一封装为带前缀的 content 字段，确保 UI 层能够显示思考过程。
/// </summary>
public class OpenAICompatibleHandler(HttpMessageHandler innerHandler) : DelegatingHandler(innerHandler)
{
    public const string ThinkContentPrefix = "__THINK__";

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        HttpResponseMessage response = await base.SendAsync(request, cancellationToken);

        // 仅对流式传输进行处理
        if (response.IsSuccessStatusCode &&
            response.Content.Headers.ContentType?.MediaType == "text/event-stream")
        {
            Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            response.Content = new StreamContent(new CompatibleStreamWrapper(stream));
            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/event-stream");
        }

        return response;
    }

    class CompatibleStreamWrapper(Stream innerStream) : Stream
    {
        readonly StreamReader reader = new(innerStream, Encoding.UTF8);
        readonly MemoryStream outputBuffer = new();
        static readonly string[] ReasoningKeys = [
            "reasoning_content",
            "thought",
            "thinking",
            "thought_content",
            "reasoning"
        ];

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            // 如果缓冲区空了，读取下一行并处理
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

        string ProcessLine(string line)
        {
            if (!line.StartsWith("data: ")) return line;

            string jsonPart = line.Substring(6).Trim();
            if (string.IsNullOrWhiteSpace(jsonPart) || jsonPart == "[DONE]") return line;

            try
            {
                JObject obj = JObject.Parse(jsonPart);
                JToken? delta = obj["choices"]?[0]?["delta"];
                if (delta is JObject deltaObj)
                {
                    // 扫描所有可能的思维链 Key
                    foreach (var key in ReasoningKeys)
                    {
                        JToken? reasoning = deltaObj[key];
                        if (reasoning != null && reasoning.Type != JTokenType.Null)
                        {
                            // 发现思考内容，将其转移到 content 并加上 UI 识别的前缀
                            string val = reasoning.ToString();
                            if (!string.IsNullOrEmpty(val))
                            {
                                deltaObj["content"] = $"{ThinkContentPrefix}{val}";
                                deltaObj.Remove(key);
                                break;// 匹配到一个即可
                            }
                        }
                    }
                }
                return "data: " + obj.ToString(Newtonsoft.Json.Formatting.None);
            }
            catch
            {
                // 如果解析失败，原样返回，避免破坏流
                return line;
            }
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => innerStream.Length;
        public override long Position { get => innerStream.Position; set => throw new NotSupportedException(); }
        public override void Flush() => innerStream.Flush();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException("Use ReadAsync");
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
