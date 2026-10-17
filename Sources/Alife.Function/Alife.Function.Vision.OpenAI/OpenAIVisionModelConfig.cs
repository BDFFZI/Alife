namespace Alife.Function.Vision.OpenAI;

public class OpenAIVisionModelConfig
{
    public string BaseUrl { get; set; } = "https://api.moonshot.cn/v1/chat/completions";
    public string ModelName { get; set; } = "moonshot-v1-8k-vision-preview";
    public string ApiKey { get; set; } = "";
}
