using Alife.Interpreter;
using System.ComponentModel;
using System.Text;

namespace Alife.Interpreter.Test;

public class InteractiveExplorer
{
    public static async Task RunAsync()
    {
        Console.OutputEncoding = Encoding.UTF8;
        
        var handlerTable = new XmlHandlerCompiler()
            .Register(new InteractiveHandlers())
            .Compile();

        var parser = new XmlStreamParser();
        var breakers = new List<string> { "，", "。", "！", "？", "！！", "[DONE]" };
        var executor = new XmlStreamExecutor(parser, handlerTable, breakers, minResultLength: 5);

        Console.Clear();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("==================================================");
        Console.WriteLine("          Alife XML 流式解析 交互测试环境");
        Console.WriteLine("==================================================");
        Console.ResetColor();

        Console.WriteLine("\n[系统] 已加载处理器文档：");
        Console.WriteLine(handlerTable.GenerateDocumentation());
        
        Console.WriteLine("\n[指南]：");
        Console.WriteLine("1. 直接输入 XML 片段并回车，执行器会流式处理。");
        Console.WriteLine("2. 输入 'clear' 清空屏幕并重置执行器。");
        Console.WriteLine("3. 输入 'exit' 退出。");
        Console.WriteLine("4. 尝试输入：<say>你好啊，<intercept>这句话被拦截</intercept>这一句会被收到。</say>");
        Console.WriteLine("--------------------------------------------------\n");

        while (true)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write("XML > ");
            Console.ResetColor();
            
            string? input = Console.ReadLine();
            if (string.IsNullOrEmpty(input)) continue;

            string cmd = input.Trim().ToLower();
            if (cmd == "exit") break;
            if (cmd == "clear")
            {
                Console.Clear();
                executor.Reset();
                Console.WriteLine("[系统] 已重置。");
                continue;
            }

            // 模拟流式输入：逐字符喂入，可以清晰观察到分词触发
            foreach (char ch in input)
            {
                await executor.FeedAsync(ch);
                // 模拟一点点延迟，让日志输出更有节奏点
                await Task.Delay(5);
            }
            
            // 自动补齐点，为了方便交互，每次回车可以认为是一个小语块结束
            await executor.FeedAsync("\n"); 
        }

        Console.WriteLine("\n[系统] 测试结束。");
    }
}

public class InteractiveHandlers
{
    [XmlHandler(description: "向用户说话。")]
    [Description]
    public void Say(XmlTagContext ctx, string message)
    {
        Log("Say", ctx, $"内容参数: \"{message}\"", ConsoleColor.Green);
    }

    [XmlHandler(description: "拦截器：清除内容。")]
    public void Intercept(XmlTagContext ctx, ref string content)
    {
        Log("Intercept", ctx, $"拦截前: \"{content}\"", ConsoleColor.Red);
        content = "";
    }

    [XmlHandler(description: "元数据展示：显示当前上下文。")]
    public void Inspect(XmlTagContext ctx)
    {
        var chain = string.Join(" -> ", ctx.CallChain.Select(c => c.Name));
        Log("Inspect", ctx, $"调用链: {chain} | 状态: {ctx.Status}", ConsoleColor.Magenta);
    }

    private static void Log(string handler, XmlTagContext ctx, string detail, ConsoleColor color)
    {
        Console.ForegroundColor = color;
        Console.Write($"[{handler}] ");
        Console.ResetColor();
        Console.WriteLine($"Status: {ctx.Status,-8} | Chunk: \"{ctx.ChunkContent}\" | Full: \"{ctx.FullContent}\"");
        Console.WriteLine($"      > {detail}");
    }
}
