using System.ComponentModel;
using Alife.Interpreter;

namespace Alife.Interpreter.Demo;

public class InteractiveExplorer
{
    public static async Task RunAsync()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.WriteLine("========================================");
        Console.WriteLine("   Alife Interpreter 纯协议解析验证器");
        Console.WriteLine("========================================");
        Console.ResetColor();

        // 1. 初始化编译器与处理器
        var compiler = new XmlHandlerCompiler();
        compiler.Register(new MockPetHandler());
        compiler.Register(new MockSpeechHandler());
        compiler.Register(new MockSystemHandler());

        var handlerTable = compiler.Compile();
        var parser = new XmlStreamParser { RootTagName = "Interpreter" };
        var executor = new XmlStreamExecutor(
            parser,
            handlerTable,
            ["，", "。", "！", "？", "......", "~"],
            minResultLength: 1
        );

        Console.WriteLine("已加载标签文档：");
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine(handlerTable.GenerateDocumentation());
        Console.ResetColor();

        Console.WriteLine("\n[操作说明]");
        Console.WriteLine("- 直接输入文字：模拟 LLM 吐字至解析器。");
        Console.WriteLine("- 输入 'test'：运行预设的自动解析用例。");
        Console.WriteLine("- 输入 'clear'：重置解析器状态。");
        Console.WriteLine("- 输入 'exit'：退出。");
        Console.WriteLine("--------------------------------------------------\n");

        while (true)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write("LLM Output > ");
            Console.ResetColor();

            string? input = Console.ReadLine();
            if (string.IsNullOrEmpty(input) || input.ToLower() == "exit") break;

            if (input.ToLower() == "clear")
            {
                executor.Reset();
                Console.WriteLine("解析器已重置。");
                continue;
            }

            if (input.ToLower() == "test")
            {
                await RunTestCases(executor);
                continue;
            }

            // 模拟流式喂入
            executor.Feed(input);
            executor.Flush();
        }

        Console.WriteLine("验证器已关闭。");
    }

    static async Task RunTestCases(XmlStreamExecutor executor)
    {
        string[] cases = [
            @"<Interpreter><speak>甚至可以在标签名里转义吗？\<speak> 显然不行，这应该是纯文本</speak></Interpreter>",
            @"<Interpreter><speak>未闭合测试：",
            @"<Interpreter><speak>错位闭合：</wrong></speak></Interpreter>",
            @"孤儿闭合标签：</speak>",
            @"<Interpreter><speak attr=""val\""ue"">属性转义测试（注意：C#双引号转义）</speak></Interpreter>",
            @"<<<<<<<<<",
            @">>>>>>>>>",
            @"< / >",
            @"<Interpreter><speak>连续转义：\\\<\<\<</speak></Interpreter>",
            @"接着输入其余部分</Interpreter>"
        ];

        foreach (var c in cases)
        {
            Console.WriteLine($"\n[Test Case] Feed: {c}");
            executor.Feed(c);
            executor.Flush();
            await Task.Delay(500);
        }
    }
}
[Description("Mock 宠物处理器：用于验证桌宠相关标签的解析。")]
public class MockPetHandler
{
    [XmlHandler("pet_exp")]
    [Description("模拟表情切换。")]
    public void PetExpression(XmlTagContext context)
    {
        if (context.Status == TagStatus.Closing)
            LogTag("pet_exp", context.FullContent);
    }

    [XmlHandler("pet_move")]
    [Description("模拟位移。")]
    public void PetMove(XmlTagContext context, double x = 0, double y = 0, int duration = 1000)
    {
        if (context.Status == TagStatus.OneShot)
            LogTag("pet_move", $"x={x}, y={y}, duration={duration}");
    }

    [XmlHandler("pet_bubble")]
    public void PetBubble(XmlTagContext context)
    {
        if (context.Status == TagStatus.Closing)
            LogTag("pet_bubble", context.FullContent);
    }

    private void LogTag(string tag, string info)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"  [EXEC Pet] <{tag}> : {info}");
        Console.ResetColor();
    }
}
[Description("Mock 语音处理器：用于验证语音输出标签。")]
public class MockSpeechHandler
{
    [XmlHandler("speak")]
    public void Speak(XmlTagContext context, string content)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"  [EXEC Speech {context.Status}] <speak> : {content}");
        Console.ResetColor();
    }
}
public class MockSystemHandler
{
    [XmlHandler("continue")]
    public void Continue()
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("  [EXEC System] <continue /> : 触发主动唤醒。");
        Console.ResetColor();
    }
}
