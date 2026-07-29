using Alife.Function.AIModelUtility;
using Alife.Function.Vision.MiniCPM;
using Microsoft.Extensions.Logging;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("========== 测试1: PythonPipeProcess 基础通信 ==========");
        await TestBasicPipe();

        Console.WriteLine();
        Console.WriteLine("========== 测试2: MiniCPMVisionModel 识图 ==========");
        await TestMiniCPMVision(args);
    }

    static async Task TestBasicPipe()
    {
        string code = """
                      def add(a, b):
                          return a + b

                      def greet(name):
                          return f"Hello, {name}!"

                      def div(a, b):
                          return a / b
                      """;

        await using var pipe = new PythonPipeProcess("test_basic", code);
        pipe.OnStderr += line => Console.WriteLine($"  [stderr] {line}");
        await pipe.StartAsync();
        Console.WriteLine("  进程已启动");

        // 正常调用
        int sum = await pipe.InvokeAsync<int>("add", 3, 4);
        Console.WriteLine($"  add(3, 4) = {sum}");

        // 字符串返回
        string greeting = await pipe.InvokeAsync<string>("greet", "World");
        Console.WriteLine($"  greet(\"World\") = {greeting}");

        // 异常透传
        try
        {
            await pipe.InvokeAsync<int>("div", 1, 0);
        }
        catch (PythonException ex)
        {
            Console.WriteLine($"  div(1, 0) 异常: {ex.Message}");
        }

        Console.WriteLine("  基础通信测试通过!");
    }

    static async Task TestMiniCPMVision(string[] args)
    {
        // 支持命令行传入图片路径，否则提示用户
        string imagePath = args.Length > 0 ? args[0] : "";

        if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath))
        {
            Console.WriteLine("  跳过：请提供一张本地图片路径作为命令行参数");
            Console.WriteLine("  用法: dotnet run -- <图片路径>");
            return;
        }

        Console.WriteLine($"  图片: {imagePath}");
        Console.WriteLine("  正在初始化 MiniCPMVisionModel (首次加载模型可能需要几分钟)...");

        using var loggerFactory = LoggerFactory.Create(builder => {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Warning);
        });
        var logger = loggerFactory.CreateLogger<MiniCPMVisionModel>();
        var model = new MiniCPMVisionModel(logger);
        try
        {
            string question = "请用中文详细描述这张图片的内容";
            Console.WriteLine($"  提问: {question}");

            string result = await model.QueryAsync(imagePath, question, 512);
            Console.WriteLine($"  回答: {result}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  异常: {ex}");
        }
    }
}
