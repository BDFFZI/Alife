using System.Text;
using Alife.Vision;
using Alife.Test;
using Alife.OfficialPlugins;
using Alife.Plugins.Official.Implement;
using Microsoft.SemanticKernel.ChatCompletion;

Terminal.Log("========================================", ConsoleColor.Magenta);
Terminal.Log("   Alife Vision AI 智能场景集成 Demo", ConsoleColor.Magenta);
Terminal.Log("========================================", ConsoleColor.Magenta);

// 1. 定义视觉 AI 角色
var character = new Character {
    ID = "VisionMao",
    Name = "真央",
    Prompt = "你是一个桌面上名为真央的 AI 视觉侦探。你非常活泼，喜欢模仿猫娘（说话带喵）。\n" +
             "你拥有通过 OpenCV 提取图像特征的能力。主人会提供图片，我会作为系统向你描述图片中的特征（标签）。\n" +
             "请根据我提供的视觉线索，以一种调皮且充满好奇心的方式与主人讨论这些图片内容喵！",
    Plugins = new HashSet<Type> {
        typeof(OpenAIChatService),
        typeof(InterpreterService),
    }
};

// 2. 初始化套件
var suite = await DemoSuite.InitializeAsync(character);
var analyzer = new ImageAnalyzer();
string workDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Results");
if (!Directory.Exists(workDir)) Directory.CreateDirectory(workDir);

Terminal.LogInfo($"工作目录: {workDir}");
Terminal.LogInfo("提示：您可以直接输入图片路径进行分析，或者输入文字与真央交流。输入 'exit' 退出。");

// 3. 级联分类器准备
var cascades = new Dictionary<string, string> {
    { "人脸", "haarcascade_frontalface_default.xml" },
    { "眼睛", "haarcascade_eye.xml" },
    { "猫脸", "haarcascade_frontalcatface.xml" }
};
foreach (var kvp in cascades)
{
    string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, kvp.Value);
    if (!File.Exists(path))
    {
        Terminal.LogSystem($"正在下载 {kvp.Key} 识别模型...");
        await DownloadFileAsync($"https://raw.githubusercontent.com/opencv/opencv/master/data/haarcascades/{kvp.Value}", path);
    }
}

// 4. 交互循环
while (true)
{
    Console.Write("\n> ");
    string? input = Console.ReadLine();

    if (string.IsNullOrWhiteSpace(input) || input.ToLower() == "exit") break;

    // 检查是否是合法的图片路径
    if (File.Exists(input) && (input.EndsWith(".jpg") || input.EndsWith(".png") || input.EndsWith(".jpeg")))
    {
        try
        {
            Terminal.LogSystem($"正在用 OpenCV 解码图像 '{Path.GetFileName(input)}' ...");
            
            // 执行分析
            string faceOutput = Path.Combine(workDir, "result_faces.jpg");
            analyzer.DetectAndDrawFaces(input, faceOutput, Path.Combine(AppDomain.CurrentDomain.BaseDirectory, cascades["人脸"]));
            
            var cascadeFullPaths = cascades.ToDictionary(k => k.Key, v => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, v.Value));
            var tags = analyzer.GetSemanticTags(input, cascadeFullPaths);

            // 构建视觉观察结果，喂给 AI
            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"[视觉观察结果] 真央在图片中看到了以下特征喵：");
            foreach (var tag in tags)
            {
                sb.AppendLine($" - {tag}");
                Terminal.LogSuccess($"[CV Tag] {tag}");
            }
            sb.AppendLine("\n请基于这些线索给主人一个惊喜的反馈喵！");

            // 作为系统提示注入（或用户消息，这里作为用户视角观察）
            suite.Activity.ChatBot.Chat(sb.ToString(), AuthorRole.User);
        }
        catch (Exception ex)
        {
            Terminal.LogError($"处理失败: {ex.Message}");
        }
    }
    else
    {
        // 普通文字交流
        suite.Activity.ChatBot.Chat(input);
    }
}

Terminal.Log("演示结束，再见喵！", ConsoleColor.Magenta);

static async Task DownloadFileAsync(string url, string path)
{
    using HttpClient client = new HttpClient();
    byte[] data = await client.GetByteArrayAsync(url);
    await File.WriteAllBytesAsync(path, data);
}
