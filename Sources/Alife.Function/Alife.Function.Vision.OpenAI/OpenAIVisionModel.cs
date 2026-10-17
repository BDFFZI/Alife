using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Alife.Framework;
using Alife.Function.AIModelUtility;

namespace Alife.Function.Vision.OpenAI;

/// <summary>
/// 在线 API 视觉分析器，兼容 OpenAI Chat Completions 多模态标准协议（如 GPT-4o, DashScope 兼容端点等）。
/// </summary>
[Module("OpenAI视觉分析",
    "基于OpenAI兼容API的在线视觉分析引擎",
    defaultCategory: "Alife 官方/模型接入/视觉模型",
    EditorUI = typeof(OpenAIVisionModelUI))]
public class OpenAIVisionModel :
    IConfigurable<OpenAIVisionModelConfig>,
    IDisposable,
    IVisionModel
{
    public OpenAIVisionModelConfig Configuration { get; set; } = null!;

    public async Task<string> QueryAsync(string imagePath, string question, int maxResponseTokens, CancellationToken cancellationToken = default)
    {
        try
        {
            //构建图片段
            byte[] imageBytes = await File.ReadAllBytesAsync(imagePath, cancellationToken);
            string base64Image = Convert.ToBase64String(imageBytes);
            string dataUri = $"data:image/jpeg;base64,{base64Image}";

            //构造请求体
            var requestBody = new {
                model = Configuration.ModelName,
                messages = new[] {
                    new {
                        role = "user",
                        content = new object[] {
                            new { type = "text", text = question },
                            new { type = "image_url", image_url = new { url = dataUri } }
                        }
                    }
                },
                max_tokens = maxResponseTokens
            };

            string jsonContent = JsonSerializer.Serialize(requestBody);
            using var request = new HttpRequestMessage(HttpMethod.Post, Configuration.BaseUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Configuration.ApiKey);
            request.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            //进行HTTP请求
            using var response = await httpClient.SendAsync(request, cancellationToken);
            string responseString = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                return $"【API 错误】{response.StatusCode}: {responseString}";

            //解析结果
            JsonNode? node = JsonNode.Parse(responseString);
            string? resultText = node?["choices"]?[0]?["message"]?["content"]?.GetValue<string>();

            if (string.IsNullOrWhiteSpace(resultText))
                return "【API 返回解析失败】未能从返回结果中提取 content。";

            return resultText;
        }
        catch (Exception ex)
        {
            return $"【请求异常】调用在线 API 时发生错误: {ex}";
        }
    }

    readonly HttpClient httpClient = new();

    public void Dispose()
    {
        httpClient.Dispose();
    }
}
