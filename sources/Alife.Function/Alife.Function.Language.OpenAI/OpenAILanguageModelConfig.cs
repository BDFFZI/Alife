namespace Alife.Function.Language.OpenAI;

public class OpenAILanguageModelConfig
{
    public string endpoint = "";
    public string modelId = "";
    public string apiKey = "";
    public string reasoningEffort = "low";
    public string extraHeaders = "";
    public string extraBody = """
                              {
                                "thinking": {"type": "enabled"}
                              }
                              """;
}
