using System.Text;
using Alife.Function.FunctionCaller;
using Newtonsoft.Json;
using DescriptionAttribute = System.ComponentModel.DescriptionAttribute;

[TestFixture]
public class XmlHandlerTableTests
{
    [Test]
    public async Task TestXmlHandlerTableHandle()
    {
        XmlHandleLog.Clear();

        XmlHandlerTable handlerTable = new XmlHandlerTable();
        handlerTable.Register(new XmlHandler(new MockPetHandler()));
        handlerTable.Register(new XmlHandler(new MockSpeechHandler()));
        handlerTable.Register(new XmlHandler(new MockSystemHandler()));

        XmlContext speak = new()
        {
            Parameters = new Dictionary<string, string>
            {
                ["text"] = "异常参数"
            },
            Content = "测试文本",
            CallMode = CallMode.Content
        };
        await handlerTable.Handle("speak", speak);

        XmlContext petmove = new()
        {
            Parameters = new Dictionary<string, string>
            {
                ["text"] = "多余参数",
                ["x"] = "12.34",
                ["y"] = "异常参数",
            },
            Content = "测试文本",
            CallMode = CallMode.OneShot
        };
        await handlerTable.Handle("petmove", petmove);

        string actual = XmlHandleLog.ToString();
        const string Expected = @"========
MockSpeechHandler.Speak
测试文本
{
  ""CallMode"": 1,
  ""Content"": ""测试文本"",
  ""Parameters"": {
    ""text"": ""异常参数""
  }
}
========
MockPetHandler.Speak
测试文本[已语音]
{
  ""CallMode"": 1,
  ""Content"": ""测试文本[已语音]"",
  ""Parameters"": {
    ""text"": ""异常参数""
  }
}
========
MockPetHandler.PetMove
x=12.34, y=1, duration=1000
{
  ""CallMode"": 3,
  ""Content"": ""测试文本"",
  ""Parameters"": {
    ""text"": ""多余参数"",
    ""x"": ""12.34"",
    ""y"": ""异常参数""
  }
}
";

        Assert.That(actual, Is.EqualTo(Expected));
    }


    [Description("Mock 宠物处理器：用于验证桌宠相关标签的解析。")]
    class MockPetHandler
    {
        [XmlFunction(FunctionMode.OneShot)]
        [Description("模拟位移。")]
        public void PetMove(XmlContext context, float x = 1, float y = 1, [Description("毫秒")] int duration = 1000)
        {
            LogXmlHandle(context, "MockPetHandler.PetMove", $"x={x}, y={y}, duration={duration}");
        }

        [XmlFunction(FunctionMode.Content)]
        public void Speak(XmlContext context, [XmlContent] string text)
        {
            LogXmlHandle(context, "MockPetHandler.Speak", text);
        }
    }

    [Description("Mock 语音处理器：用于验证语音输出标签。")]
    class MockSpeechHandler
    {
        [XmlFunction(FunctionMode.Content, order: -10)]
        public void Speak(XmlContext context, [Description("需要转语音的文本")] [XmlContent] string text, string tone = "")
        {
            LogXmlHandle(context, "MockSpeechHandler.Speak", context.Content);
            context.Content += "[已语音]";
        }
    }

    class MockSystemHandler
    {
        [XmlFunction(FunctionMode.OneShot)]
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