namespace Alife.Function.Language.OpenAI;

public class OpenAILanguageModelConfig
{
    public string endpoint = "";
    public string modelId = "";
    public string apiKey = "";
    public bool defaultThinking = true;
    public string extraHeaders = "";
    //思考模式
    public string reasoningEffort = "low";
    public string extraBody = """
                              {
                                "thinking": {"type": "enabled"}
                              }
                              """;
    //非思考模式
    public string extraBodyNotThinking = """
                                         {
                                           "thinking": {"type": "disabled"}
                                         }
                                         """;
}