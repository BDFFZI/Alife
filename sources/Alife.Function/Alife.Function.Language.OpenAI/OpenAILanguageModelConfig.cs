namespace Alife.Function.Language.OpenAI;

public class OpenAILanguageModelConfig
{
    public string endpoint = "";
    public string modelId = "";
    public string apiKey = "";
    public bool defaultThinking = false;
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

    // 多模态输入
    public bool enableImageInput = false;
    public bool enableVideoInput = false;
    public bool enableFileInput = false;
    public bool useGlmMultimodalProtocol = false;
}
