using Alife.Interpreter;
using System.ComponentModel;
using System.Text;

Console.OutputEncoding = Encoding.UTF8;

Console.WriteLine("========================================");
Console.WriteLine("   Alife XML 处理器映射与元数据 示例");
Console.WriteLine("========================================");

// 1. 定义一个包含多种新特性标记的处理器类
var myHandlers = new SampleHandlers();

// 2. 编译处理器表
var compiler = new XmlHandlerCompiler();
compiler.Register(myHandlers);
var handlerTable = compiler.Compile();

// 3. 打印生成的文档（展示自动映射的名称和 Description 属性）
Console.WriteLine("\n[系统] 正在生成 AI 接口文档...");
string documentation = handlerTable.GenerateDocumentation();
Console.WriteLine("----------------------------------------");
Console.WriteLine(documentation);
Console.WriteLine("----------------------------------------");

// 4. 演示解析和执行
var parser = new XmlStreamParser();
// 设定分词符（包含单字符和多字符）以及最小句长（10个字符，不计分词符）
var breakers = new List<string> { "。", "！", "？", "！！", "[DONE]" };
var executor = new XmlStreamExecutor(parser, handlerTable, breakers, minResultLength: 10);

string xmlInput = 
    "<SAY message='你好' repeat='1'>短句。不触发分词。</SAY>" + 
    "<say message='长句测试'>这是一个足够长的句子，应该会触发分词。！！</say>" +
    "<DirectMapping color='green'>多字符分词器测试[DONE]</DirectMapping>";

Console.WriteLine("\n[系统] 正在处理示例输入 (包含断句逻辑测试):");
Console.WriteLine($"[配置] 最小句长: 10, 分词符: {string.Join(", ", breakers)}");
Console.WriteLine(xmlInput);
Console.WriteLine("\n[执行日志]:");
await executor.FeedAsync(xmlInput);

// 等待一小会儿让后台处理完成（因为执行器运行在后台循环中）
await Task.Delay(200);

Console.WriteLine("\n[系统] 演示结束。");

/// <summary>
/// 示例处理器类
/// </summary>
public class SampleHandlers
{
    [XmlHandler(description: "向用户说话，并支持重复次数。")]
    public Task Say(
        [Description("要发送的消息内容")] string message,
        [Description("重复播放的次数")] int repeat = 1)
    {
        for (int i = 0; i < repeat; i++)
        {
            Console.WriteLine($"[Say Handler]({i + 1}/{repeat}): {message}");
        }
        return Task.CompletedTask;
    }

    [XmlHandler("CUSTOM_TAG")]
    [Description("使用自定义标签名和 Description 属性定义的处理器。")]
    public Task LegacyStyle(
        [Description("标签内的文字内容")] string content)
    {
        Console.WriteLine($"[CustomTag Handler]: {content}");
        return Task.CompletedTask;
    }

    [XmlHandler]
    [Description("演示直接映射：方法名即为标签名。")]
    public Task DirectMapping(string color)
    {
        Console.WriteLine($"[DirectMapping Handler]: Color is {color}");
        return Task.CompletedTask;
    }
}
