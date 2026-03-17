using System.Text;
using Alife.Vision;

Console.OutputEncoding = Encoding.UTF8;
Console.WriteLine("========================================");
Console.WriteLine("   Alife Vision 图像识别验证 Demo");
Console.WriteLine("========================================");

var analyzer = new ImageAnalyzer();
string workDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Results");
if (!Directory.Exists(workDir)) Directory.CreateDirectory(workDir);

Console.WriteLine($"[系统] 工作目录: {workDir}");

// 1. 获取测试图片
Console.WriteLine("\n[提示] 请输入测试图片路径 (直接回车将搜索 samples 下的图片):");
string? inputPath = Console.ReadLine();

if (string.IsNullOrWhiteSpace(inputPath))
{
    // 尝试找一个现有的图片，或者提示用户
    Console.WriteLine("[警告] 未提供路径。请确保工作目录下有图片喵！");
    return;
}

if (!File.Exists(inputPath))
{
    Console.WriteLine($"[错误] 找不到文件: {inputPath}");
    return;
}

try 
{
    // --- 任务 1: 灰度化 ---
    string grayOutput = Path.Combine(workDir, "result_gray.jpg");
    Console.WriteLine("[演示] 正在生成灰度图...");
    analyzer.MakeGrayScale(inputPath, grayOutput);
    Console.WriteLine($"[完成] 灰度图已保存至: {grayOutput}");

    // --- 任务 2: 边缘检测 ---
    string edgeOutput = Path.Combine(workDir, "result_edges.jpg");
    Console.WriteLine("[演示] 正在执行 Canny 边缘检测...");
    analyzer.DetectEdges(inputPath, edgeOutput);
    Console.WriteLine($"[完成] 边缘图已保存至: {edgeOutput}");

    // --- 任务 3: 人脸检测 ---
    string faceOutput = Path.Combine(workDir, "result_faces.jpg");
    string cascadePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "haarcascade_frontalface_default.xml");
    if (File.Exists(cascadePath))
    {
        Console.WriteLine("[演示] 正在尝试检测人脸...");
        int count = analyzer.DetectAndDrawFaces(inputPath, faceOutput, cascadePath);
        Console.WriteLine($"[完成] 检测到 {count} 张人脸。结果已保存至: {faceOutput}");
    }

    // --- 任务 4: AI 语义识别 (多模态) ---
    Console.WriteLine("\n[系统] 是否尝试使用多模态 AI 进行语义识别？(y/n)");
    string? choice = Console.ReadLine()?.ToLower();
    if (choice == "y" || choice == "yes")
    {
        Console.WriteLine("[配置] 请输入 Vision 模型的 API Key (或留空使用默认设置调试):");
        string? apiKey = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(apiKey)) apiKey = "YOUR_VISION_API_KEY";

        Console.WriteLine("[配置] 正在启动 Vision AI (建议模型: gpt-4o 或 gpt-4o-mini)...");
        // 注意：DeepSeek 目前暂不支持原生视觉。建议配置为 OpenAI/Gemini/Claude 的视觉地址。
        var visionService = new VisionAIService(
            endpoint: "https://api.openai.com/v1", // 演示默认地址
            modelId: "gpt-4o-mini",
            apiKey: apiKey
        );

        Console.WriteLine("[演示] 正在请求 AI 描述图片内容喵...");
        string description = await visionService.DescribeImageAsync(inputPath);
        Console.WriteLine("\n----------------------------------------");
        Console.WriteLine("【AI 语义描述成果】");
        Console.WriteLine(description);
        Console.WriteLine("----------------------------------------");
    }

    Console.WriteLine("\n[系统] 所有演示任务完成！你可以去 Results 文件夹查看效果喵！");
}
catch (Exception ex)
{
    Console.WriteLine($"[异常] 处理失败: {ex.Message}");
    if (ex.InnerException != null) Console.WriteLine($"[内部详细信息]: {ex.InnerException.Message}");
}
