using Alife;
using Alife.Vision;
namespace Alife.Vision.Demo;

class Program
{
    static async Task Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        PrintBanner();

        Console.WriteLine("  正在启动视觉模型，请稍候（首次运行会下载模型，约 6GB）...");
        Console.WriteLine();

        using var analyzer = new VisionAnalyzer();
        try
        {
            string modelsDir = PathEnvironment.ModelsPath;
            string qwenPath = Path.Combine(modelsDir, "Qwen2.5-VL-3B-Instruct");
            await analyzer.InitAsync(modelPath: qwenPath, timeoutSeconds: 300, onLog: msg => Console.Write(msg));
        }
        catch (Exception ex)
        {
            PrintError($"模型加载失败：{ex.Message}");
            Console.WriteLine("  请确认已安装依赖：pip install transformers torch pillow qwen-vl-utils accelerate");
            return;
        }

        PrintSuccess("视觉模型加载完成！");
        Console.WriteLine();

        await RunInteractiveLoop(analyzer);
    }

    static async Task RunInteractiveLoop(VisionAnalyzer analyzer)
    {
        while (true)
        {
            Console.WriteLine("─────────────────────────────────────────");
            Console.Write("  请输入图片路径（或输入 exit 退出）：");
            string? imagePath = Console.ReadLine()?.Trim().Trim('"');

            if (string.IsNullOrWhiteSpace(imagePath)) continue;
            if (imagePath.Equals("exit", StringComparison.OrdinalIgnoreCase)) break;

            if (!File.Exists(imagePath))
            {
                PrintError($"文件不存在：{imagePath}");
                continue;
            }

            Console.WriteLine();
            Console.WriteLine("  功能选择：");
            Console.WriteLine("    1  图像描述（让 AI 描述图片内容）");
            Console.WriteLine("    2  视觉问答（自定义提问）");
            Console.Write("  请选择 [1/2]：");

            string? choice = Console.ReadLine()?.Trim();
            Console.WriteLine();

            try
            {
                string result;

                if (choice == "2")
                {
                    Console.Write("  请输入你的问题：");
                    string? question = Console.ReadLine()?.Trim();
                    if (string.IsNullOrWhiteSpace(question)) continue;

                    Console.WriteLine("  正在分析...");
                    result = await analyzer.QueryAsync(imagePath, question);
                }
                else
                {
                    Console.WriteLine("  正在分析...");
                    result = await analyzer.CaptionAsync(imagePath);
                }

                Console.WriteLine();
                PrintSuccess("分析结果：");
                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine($"  {result}");
                Console.ResetColor();
            }
            catch (Exception ex)
            {
                PrintError($"分析失败：{ex.Message}");
            }

            Console.WriteLine();
        }

        PrintSuccess("已退出。");
    }

    static void PrintBanner()
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine();
        Console.WriteLine("  ╔══════════════════════════════════════╗");
        Console.WriteLine("  ║       Alife Vision Demo              ║");
        Console.WriteLine("  ║       Qwen2.5-VL-3B · 中文视觉理解  ║");
        Console.WriteLine("  ╚══════════════════════════════════════╝");
        Console.ResetColor();
        Console.WriteLine();
    }

    static void PrintSuccess(string msg)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"  ✓ {msg}");
        Console.ResetColor();
    }

    static void PrintError(string msg)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"  ✗ {msg}");
        Console.ResetColor();
    }
}
