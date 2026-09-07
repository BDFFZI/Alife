namespace Alife.Function.Language.OpenAI;

using System.Collections.Generic;

public class OpenAILanguageModelConfig
{
    public string endpoint = "";
    public string modelId = "";
    public string apiKey = "";
    public bool defaultThinking = true;
    public string extraHeaders = "";
    //采样温度，越低越认真/严谨，越高越发散/有创造性
    public double temperature = 0.6;
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

    // 多模态输入：勾选启用对应内容类型（IAlifeContentType.RegistrationKey）的 AI 上传注册
    public HashSet<string> enabledContentTypes = new();

    // 明确禁用保留模式的内容类型（空 = 全部允许保留，默认）；AI 在未授权时以保留模式调用会收到报错
    public HashSet<string> persistentDisabledContentTypes = new();
}
