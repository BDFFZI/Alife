namespace Alife.Function.Language.OpenAI;

using System.Collections.Generic;

public class OpenAILanguageModelConfig
{
    public string endpoint = "";
    public string modelId = "";
    public string apiKey = "";
    public bool defaultThinking = false;
    public string extraHeaders = "";
    //采样温度，越低越认真/严谨，越高越发散/有创造性
    public double temperature = 0.6;
    //思考模式
    public string reasoningEffort = "high";
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
    
    public HashSet<string> enabledContentTypes = new();
    public HashSet<string> enabledPersistentContentTypes = new();
}
