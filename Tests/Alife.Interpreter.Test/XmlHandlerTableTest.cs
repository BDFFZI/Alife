using System.ComponentModel;
using System.Text;
using Newtonsoft.Json;
using Description = System.ComponentModel.DescriptionAttribute;

namespace Alife.Interpreter.Test;

public class XmlHandlerTableTest
{
    [Test]
    public void TestXmlHandlerTableDocument()
    {
        XmlHandlerTable handlerTable = new XmlHandlerTable();
        handlerTable.Register(new MockPetHandler());
        handlerTable.Register(new MockSpeechHandler());
        handlerTable.Register(new MockSystemHandler());

        string actual = handlerTable.Document();
        const string Expected = @"# XmlHandler Document

## MockPetHandler
> Mock 宠物处理器：用于验证桌宠相关标签的解析。

- <petmove x=""Single"" y=""Single"" duration=""Int32""（毫秒） /> : 模拟位移。
- <speak>text</speak>

## MockSpeechHandler
> Mock 语音处理器：用于验证语音输出标签。

- <speak>text（需要转语音的文本）</speak>

## MockSystemHandler

- <continue />";

        Assert.That(actual, Is.EqualTo(Expected));
    }

    [Test]
    public void TestXmlHandlerTableHandle()
    {
        XmlHandleLog.Clear();

        XmlHandlerTable handlerTable = new XmlHandlerTable();
        handlerTable.Register(new MockPetHandler());
        handlerTable.Register(new MockSpeechHandler());
        handlerTable.Register(new MockSystemHandler());

        XmlContext speak = new() {
            Parameters = new Dictionary<string, string> {
                ["text"] = "异常参数"
            },
            Content = "测试文本",
        };
        handlerTable.Handle("speak", speak);

        XmlContext petmove = new() {
            Parameters = new Dictionary<string, string> {
                ["text"] = "多余参数",
                ["x"] = "12.34",
                ["y"] = "异常参数",
            },
            Content = "测试文本",
        };
        handlerTable.Handle("petmove", petmove);

        string actual = XmlHandleLog.ToString();
        const string Expected = @"========
MockPetHandler.Speak
测试文本
{
  ""Parameters"": {
    ""text"": ""异常参数""
  },
  ""Content"": ""测试文本""
}
========
MockSpeechHandler.Speak
测试文本[已弹出气泡]
{
  ""Parameters"": {
    ""text"": ""异常参数""
  },
  ""Content"": ""测试文本[已弹出气泡]""
}
========
MockPetHandler.PetMove
x=12.34, y=1, duration=1000
{
  ""Parameters"": {
    ""text"": ""多余参数"",
    ""x"": ""12.34"",
    ""y"": ""异常参数""
  },
  ""Content"": ""测试文本""
}
";

        Assert.That(actual, Is.EqualTo(Expected));
    }


    [Description("Mock 宠物处理器：用于验证桌宠相关标签的解析。")]
    class MockPetHandler
    {
        [XmlFunction("petmove")]
        [Description("模拟位移。")]
        public void PetMove(XmlContext context, float x = 1, float y = 1, [Description("毫秒")] int duration = 1000)
        {
            LogXmlHandle(context, "MockPetHandler.PetMove", $"x={x}, y={y}, duration={duration}");
        }

        [XmlFunction]
        public void Speak(XmlContext context, ref string text)
        {
            LogXmlHandle(context, "MockPetHandler.Speak", text);
            text += "[已弹出气泡]";
        }
    }
    [Description("Mock 语音处理器：用于验证语音输出标签。")]
    class MockSpeechHandler
    {
        [XmlFunction("speak")]
        public void Speak(XmlContext context, [Description("需要转语音的文本")] ref string text)
        {
            LogXmlHandle(context, "MockSpeechHandler.Speak", text);
        }
    }
    class MockSystemHandler
    {
        [XmlFunction("continue")]
        public void Continue()
        {
            LogXmlHandle(null, "MockSystemHandler.Continue", "continue");
        }
    }

    static readonly StringBuilder XmlHandleLog = new();
    static void LogXmlHandle(XmlContext? context, string source, string text)
    {
        XmlHandleLog.AppendLine("========");
        XmlHandleLog.AppendLine(source);
        XmlHandleLog.AppendLine(text);
        if (context != null)
            XmlHandleLog.AppendLine(JsonConvert.SerializeObject(context, Formatting.Indented));
    }
}
