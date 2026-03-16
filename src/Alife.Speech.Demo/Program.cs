using Alife.Speech;
using System.Text;

Console.OutputEncoding = Encoding.UTF8;

Console.WriteLine("========================================");
Console.WriteLine("   Alife 语音识别与合成 交互示例");
Console.WriteLine("========================================");

// 1. 初始化语音合成器
var synth = new LocalSpeechSynthesizer();
Console.WriteLine("[系统] 语音合成器已就绪。");

// 在大模型加载前先选择模式
Console.WriteLine("\n[选择模式]: 1. 麦克风交互  2. 连续拼读压力测试");
string? choice = Console.ReadLine();

if (choice == "2")
{
    Console.WriteLine("[压力测试] 正在依次合成并播放多个分段...");
    string[] segments = {
        "你好，欢迎使用我的语音合成引擎。",
        "这是一个无缝连接测试。",
        "我们正在检测首尾静音是否已经被完美裁切。",
        "如果听到的是连贯的声音，说明优化成功了。",
        "再见！"
    };

    foreach (var text in segments)
    {
        Console.WriteLine($"[合成中]: {text}");
        await synth.SpeakAsync(text);
    }
    Console.WriteLine("[压力测试] 完成。");
    return;
}

// 2. 初始化语音识别器
string? modelPath = GetModelPath();
if (modelPath == null)
{
    Console.WriteLine("[错误] 未找到语音模型，请确保模型已下载到 'model' 目录。");
    return;
}

Console.WriteLine($"[系统] 正在加载语音模型: {modelPath}");
try 
{
    using var recognizer = new LocalSpeechRecognizer(modelPath);
    Console.WriteLine("[系统] 语音识别器已就绪。");

    // 3. 设置识别回调
    recognizer.OnPartial += (text) => 
    {
        Console.Write($"\r[识别中]: {text}        ");
        Console.Out.Flush();
    };

    recognizer.OnRecognized += async (text, confidence) => 
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        Console.WriteLine($"\r[您说]: {text} (置信度: {confidence:P1})");

        if (text.Contains("退出") || text.Contains("再见") || text.Contains("exit"))
        {
            Console.WriteLine("[系统] 正在退出...");
            await synth.SpeakAsync("好的，再见！");
            Environment.Exit(0);
        }

        // 语音反馈
        Console.WriteLine("[系统] 正在回复...");
        await synth.SpeakAsync($"我听到您说：{text}");
    };

    // 麦克风交互模式
    Console.WriteLine("\n[提示] 麦克风已开启，请开始说话...");
    Console.WriteLine("[提示] 说出 '退出' 或 'exit' 以结束程序。\n");

    recognizer.Start();

    // 保持程序运行
    while (true)
    {
        await Task.Delay(1000);
    }
}
catch (Exception ex)
{
    Console.WriteLine($"\n[终端错误] 无法初始化语音识别: {ex.Message}");
    Console.WriteLine("提示: 这通常是因为 Vosk 模型与库版本不匹配导致的（当前库版本 0.3.38 不支持 HCLr.fst 动态模型）。");
}

static string? GetModelPath()
{
    var candidates = new[]
    {
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "model"),
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "Alife.Speech", "model"),
        "model"
    };

    foreach (var path in candidates)
    {
        if (Directory.Exists(path) && Directory.Exists(Path.Combine(path, "am")))
        {
            return Path.GetFullPath(path);
        }
    }
    return null;
}
