using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace Alife.Function.Language.OpenAI;

/// <summary>Maps Semantic Kernel content items to GLM's multimodal chat protocol.</summary>
public sealed class GlmChatCompletionService : IChatCompletionService
{
    readonly HttpClient httpClient;
    readonly string apiKey;
    readonly string modelId;
    readonly Uri chatCompletionsUri;

    public GlmChatCompletionService(HttpClient httpClient, string apiKey, string modelId, Uri chatCompletionsUri)
    {
        this.httpClient = httpClient;
        this.apiKey = apiKey;
        this.modelId = modelId;
        this.chatCompletionsUri = chatCompletionsUri;
        Attributes = new Dictionary<string, object?> { ["model_id"] = modelId };
    }

    public IReadOnlyDictionary<string, object?> Attributes { get; }

    public static Uri CreateChatCompletionsUri(string endpoint)
    {
        string normalized = endpoint.TrimEnd('/');
        if (normalized.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
            return new Uri(normalized);
        return new Uri(normalized + "/chat/completions");
    }

    public async Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(
        ChatHistory chatHistory,
        PromptExecutionSettings? executionSettings = null,
        Kernel? kernel = null,
        CancellationToken cancellationToken = default)
    {
        JsonObject responseJson = await SendAsync(chatHistory, executionSettings, cancellationToken);
        string content = responseJson["choices"]?[0]?["message"]?["content"]?.GetValue<string>()
            ?? throw new InvalidOperationException("GLM response does not contain choices[0].message.content.");
        return [new ChatMessageContent(AuthorRole.Assistant, content, modelId, responseJson)];
    }

    public async IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(
        ChatHistory chatHistory,
        PromptExecutionSettings? executionSettings = null,
        Kernel? kernel = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ChatMessageContent> messages = await GetChatMessageContentsAsync(
            chatHistory, executionSettings, kernel, cancellationToken);
        foreach (ChatMessageContent message in messages)
            yield return new StreamingChatMessageContent(AuthorRole.Assistant, message.Content, modelId);
    }

    async Task<JsonObject> SendAsync(
        ChatHistory history,
        PromptExecutionSettings? executionSettings,
        CancellationToken cancellationToken)
    {
        JsonObject payload = new() {
            ["model"] = modelId,
            ["messages"] = SerializeHistory(history),
            ["stream"] = false
        };

#pragma warning disable SKEXP0010
        if (executionSettings is OpenAIPromptExecutionSettings openAISettings &&
            openAISettings.ExtraBody != null)
        {
            foreach ((string key, object? value) in openAISettings.ExtraBody)
                payload[key] = JsonSerializer.SerializeToNode(value);
        }
#pragma warning restore SKEXP0010

        using HttpRequestMessage request = new(HttpMethod.Post, chatCompletionsUri) {
            Content = JsonContent.Create(payload)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
        string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (response.IsSuccessStatusCode == false)
            throw new HttpRequestException($"GLM returned {(int)response.StatusCode} ({response.ReasonPhrase}): {responseBody}");

        return JsonNode.Parse(responseBody)?.AsObject()
            ?? throw new InvalidOperationException("GLM returned an empty response.");
    }

    static JsonArray SerializeHistory(ChatHistory history)
    {
        JsonArray messages = new();
        foreach (ChatMessageContent message in history)
        {
            JsonArray content = new();
            foreach (KernelContent item in message.Items)
                content.Add(SerializeContent(item));
            messages.Add(new JsonObject {
                ["role"] = message.Role.ToString().ToLowerInvariant(),
                ["content"] = content
            });
        }
        return messages;
    }

    static JsonObject SerializeContent(KernelContent content) => content switch {
        TextContent text => new JsonObject { ["type"] = "text", ["text"] = text.Text },
        ImageContent image => new JsonObject { ["type"] = "image_url", ["image_url"] = new JsonObject { ["url"] = GetImageUrl(image) } },
        GlmVideoUrlContent video => new JsonObject { ["type"] = "video_url", ["video_url"] = new JsonObject { ["url"] = video.Url.ToString() } },
        GlmFileUrlContent file => new JsonObject { ["type"] = "file", ["file"] = new JsonObject { ["file_url"] = file.Url.ToString() } },
        _ => throw new NotSupportedException($"GLM does not support the Semantic Kernel content type '{content.GetType().Name}'.")
    };

    static string GetImageUrl(ImageContent image)
    {
        if (image.Uri is not null)
            return image.Uri.ToString();
        if (image.DataUri is not null)
            return image.DataUri;
        if (image.Data is { } data && data.IsEmpty == false)
            return $"data:{image.MimeType ?? "application/octet-stream"};base64,{Convert.ToBase64String(data.Span)}";
        throw new NotSupportedException("ImageContent must contain a URL, data URI, or binary data.");
    }
}
