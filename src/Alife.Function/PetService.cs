using System.Text;
using System.Text.Json;
using System.Diagnostics;
using Microsoft.SemanticKernel;
using Alife.Abstractions;
using Alife.Interpreter;
using System.ComponentModel;

namespace Alife.OfficialPlugins;

[Plugin("真央桌宠", "将真央桌宠接入AI系统，实现表现力同步和互动反馈。")]
public class PetService : Plugin, IAsyncDisposable
{
    private Process? _petProcess;
    private readonly DialogContext dialogContext;
    private ChatActivity? _chatActivity;
    private TaskCompletionSource? _moveTcs;

    public PetService(InterpreterService interpreterService, DialogContext dialogContext)
    {
        this.dialogContext = dialogContext;
        interpreterService.RegisterHandler(this);
    }

    public override Task AwakeAsync(AwakeContext context)
    {
        context.contextBuilder.ChatHistory.AddSystemMessage("""
# 互动指南 (针对 Poke 消息)
你会收到来自系统的特殊消息（Poke），表示主人正在与你进行物理互动。请根据互动类型给出极其自然的回复：
1. **(物理干扰)**：表示主人在拖动、摇晃或旋转你。请表现出相应的生理反应（如晕、惊讶、开心等），并巧妙地衔接对话。
2. **(连击干扰)**：表示主人在疯狂戳你。请表现出被打扰或者害羞的反应。
3. **通过属性表达 (参数控制)**：你可以通过 `<pet_param name="参数名" value="数值" duration="毫秒" />` 直接控制我的身体姿态。
   - `ParamAngleX/Y/Z`: 头部转动 (-30 到 30)
   - `ParamBodyAngleX`: 身体侧歪 (-10 到 10)
   - `ParamEyeBallX/Y`: 眼神位置 (-1 到 1)
   示例：`<pet_param name="ParamBodyAngleX" value="10" duration="1500" />` 表示身体缓缓向右歪。
不要在回复中复读这些提示语，直接进入角色进行互动喵！
""");
        return Task.CompletedTask;
    }

    public override Task StartAsync(Kernel kernel, ChatActivity chatActivity)
    {
        _chatActivity = chatActivity;
        chatActivity.ChatBot.ChatSent += (s) => InternalReset();

        try
        {
            // 启动桌宠进程
            string assemblyDir = AppDomain.CurrentDomain.BaseDirectory;
            string petExePath = Path.Combine(assemblyDir, "Alife.Pet.exe");

            if (!File.Exists(petExePath))
            {
                petExePath = Path.Combine(assemblyDir, "src", "Alife.Pet", "bin", "Debug", "net10.0-windows", "Alife.Pet.exe");
            }
            if (!File.Exists(petExePath))
            {
                petExePath = @"c:\Users\13309\Desktop\Alife\src\Alife.Pet\bin\Debug\net10.0-windows\Alife.Pet.exe";
            }

            if (!File.Exists(petExePath))
            {
                Console.WriteLine($"[PetService] Error: Could not find Pet EXE at {petExePath}");
                return Task.CompletedTask;
            }

            ProcessStartInfo psi = new ProcessStartInfo {
                FileName = petExePath,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardInputEncoding = Encoding.UTF8,
                StandardOutputEncoding = Encoding.UTF8,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(petExePath)
            };

            _petProcess = new Process { StartInfo = psi };
            _petProcess.OutputDataReceived += (s, e) => OnPetMessageReceived(e.Data);
            _petProcess.ErrorDataReceived += (s, e) => {
                if (e.Data != null) Console.WriteLine($"[Pet Error] {e.Data}");
            };

            _petProcess.Start();
            _petProcess.BeginOutputReadLine();
            _petProcess.BeginErrorReadLine();

            Console.WriteLine("[PetService] Pet process started successfully.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PetService] Failed to start pet: {ex.Message}");
        }

        return Task.CompletedTask;
    }

    private void OnPetMessageReceived(string? data)
    {
        if (string.IsNullOrWhiteSpace(data)) return;
        Console.WriteLine($"[IPC <- Pet] {data}");

        try
        {
            using var doc = JsonDocument.Parse(data);
            var root = doc.RootElement;
            if (!root.TryGetProperty("type", out var typeProp)) return;
            var type = typeProp.GetString();

            if (type == "chat")
            {
                var text = root.GetProperty("text").GetString();
                if (!string.IsNullOrEmpty(text))
                {
                    dialogContext.AddMessage(new ChatMessage { content = text, isUser = true });
                }
            }
            else if (type == "poke")
            {
                var text = root.GetProperty("text").GetString();
                if (!string.IsNullOrEmpty(text) && _chatActivity != null)
                {
                    _chatActivity.ChatBot.Poke(text);
                }
            }
            else if (type == "move-finished")
            {
                _moveTcs?.TrySetResult();
            }
        }
        catch
        {
            // 忽略非 JSON 输出
        }
    }

    private void SendToPet(object msg)
    {
        if (_petProcess is { HasExited: false })
        {
            try
            {
                string json = JsonSerializer.Serialize(msg);
                _petProcess.StandardInput.WriteLine(json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PetService] Send Error: {ex.Message}");
            }
        }
    }

    private void LookForward()
    {
        SendToPet(new { type = "look" });
    }

    [XmlHandler("pet_bubble")]
    [Description("气泡文字喵。示例: <pet_bubble>你好喵</pet_bubble>")]
    public Task PetBubble(XmlTagContext context)
    {
        if (context.Status != TagStatus.Closing) return Task.CompletedTask;
        if (string.IsNullOrWhiteSpace(context.FullContent)) return Task.CompletedTask;

        int duration = 2000 + context.FullContent.Length * 100;
        SendToPet(new { type = "bubble", text = context.FullContent, duration });
        return Task.CompletedTask;
    }

    [XmlHandler("pet_exp")]
    [Description("发表情喵。支持：开心, 闭眼, 晕乎, 悲伤, 害羞, 惊讶, 生气；示例: <pet_exp>害羞</pet_exp>")]
    public Task PetExpression(XmlTagContext context)
    {
        if (context.Status != TagStatus.Closing) return Task.CompletedTask;

        string expression = context.FullContent.Trim();
        string id = expression switch {
            "开心" => "exp_01",
            "闭眼" => "exp_03",
            "晕" or "晕乎" => "exp_04",
            "悲伤" or "委屈" => "exp_05",
            "害羞" or "脸红" => "exp_06",
            "惊讶" or "哇" => "exp_07",
            "生气" or "哼" => "exp_08",
            _ => int.TryParse(expression, out _) ? "exp_" + expression.PadLeft(2, '0') : "exp_01"
        };
        SendToPet(new { type = "expression", id });
        return Task.CompletedTask;
    }

    [XmlHandler("pet_move")]
    [Description("移动桌宠喵。示例: <pet_move x=\"100\" y=\"50\" duration=\"3000\" /> - 表示向右移100像素，下移50像素")]
    public async Task PetMove(XmlTagContext context)
    {
        if (context.Status == TagStatus.Opening) return;

        var currentTag = context.CallChain.LastOrDefault();
        string? sx = null, sy = null, sd = null;
        
        if (currentTag.Attributes != null)
        {
            currentTag.Attributes.TryGetValue("x", out sx);
            currentTag.Attributes.TryGetValue("y", out sy);
            currentTag.Attributes.TryGetValue("duration", out sd);
        }

        // 如果属性没有，尝试从内容解析 (x,y 格式)
        if (string.IsNullOrEmpty(sx) && !string.IsNullOrEmpty(context.FullContent))
        {
            var parts = context.FullContent.Split(',');
            if (parts.Length >= 2)
            {
                sx = parts[0].Trim();
                sy = parts[1].Trim();
            }
        }

        double.TryParse(sx, out double x);
        double.TryParse(sy, out double y);
        int.TryParse(sd, out int duration);
        if (duration <= 0) duration = 1000; // 默认 1 秒

        // 创建等待任务
        _moveTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        
        SendToPet(new { type = "window-move", x, y, duration });

        // 等待位移完成（附带安全超时，防止 UI 崩溃导致整个 AI 卡住）
        await Task.WhenAny(_moveTcs.Task, Task.Delay(duration + 1000));
        _moveTcs = null;
    }

    [XmlHandler("pet_mtn")]
    [Description("执行动作喵。支持：害羞，摇头，点头，欢迎，旋转，跳舞；示例: <pet_mtn>害羞</pet_mtn>")]
    public Task PetMotion(XmlTagContext context)
    {
        if (context.Status != TagStatus.Closing) return Task.CompletedTask;
        if (string.IsNullOrWhiteSpace(context.FullContent)) return Task.CompletedTask;

        string motion = context.FullContent.Trim();
        int index = motion switch {
            "害羞" => 0,
            "摇头" => 1,
            "点头" => 2,
            "欢迎" => 3,
            "旋转" => 4,
            "跳舞" => 5,
            _ => int.TryParse(motion, out var i) ? i : 2
        };

        SendToPet(new { type = "motion", group = "TapBody", index });
        return Task.CompletedTask;
    }

    [XmlHandler("pet_param")]
    [Description("直接控制 Live2D 参数喵。示例: <pet_param name=\"ParamBodyAngleX\" value=\"10\" duration=\"1000\" />")]
    public Task PetParameter(XmlTagContext context)
    {
        if (context.Status == TagStatus.Opening) return Task.CompletedTask;

        var currentTag = context.CallChain.LastOrDefault();
        string? name = null, sValue = null, sDuration = null;

        if (currentTag.Attributes != null)
        {
            currentTag.Attributes.TryGetValue("name", out name);
            currentTag.Attributes.TryGetValue("value", out sValue);
            currentTag.Attributes.TryGetValue("duration", out sDuration);
        }

        if (string.IsNullOrEmpty(name)) return Task.CompletedTask;

        float.TryParse(sValue, out float value);
        int.TryParse(sDuration, out int duration);
        if (duration <= 0) duration = 1000;

        SendToPet(new { type = "parameter", name, value, duration });
        return Task.CompletedTask;
    }

    private void InternalReset()
    {
        _moveTcs?.TrySetCanceled();
    }

    public override async Task DestroyAsync()
    {
        await DisposeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_petProcess != null && !_petProcess.HasExited)
        {
            _petProcess.Kill();
            await _petProcess.WaitForExitAsync();
            _petProcess.Dispose();
            _petProcess = null;
        }
    }
}
