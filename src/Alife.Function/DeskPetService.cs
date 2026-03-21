using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Alife.Abstractions;
using Alife.Interpreter;
using Microsoft.SemanticKernel;

namespace Alife.OfficialPlugins;

[Plugin("Live2D桌宠", "将Live2D桌宠接入AI系统，实现表现力同步和互动反馈。")]
[Description("此服务让你获得控制Live2D桌宠以及接收其交互的能力")]
public class DeskPetService : Plugin, IAsyncDisposable
{
    [XmlHandler("pet_bubble")]
    [Description("气泡文字：显示一段浮动文字。示例: <pet_bubble>你好</pet_bubble>")]
    public void PetBubble(XmlTagContext context)
    {
        if (string.IsNullOrWhiteSpace(context.ChunkContent))
            return;

        int duration = 2000 + context.ChunkContent.Length * 100;
        SendToPet(new { type = "bubble", text = context.ChunkContent, duration });
    }

    [XmlHandler("pet_exp")]
    [Description("控制表情：切换当前显示的表情。支持：开心, 闭眼, 悲伤, 害羞, 惊讶, 生气；示例: <pet_exp>害羞</pet_exp>")]
    public void PetExpression(XmlTagContext context)
    {
        if (context.Status != TagStatus.Closing)
            return;

        string expression = context.FullContent.Trim();
        string id = expression switch {
            "开心" or "晕" or "晕乎" => "exp_04", // exp_04 是繁星眼，适合表现开心或晕
            "闭眼" => "exp_03",
            "悲伤" or "委屈" => "exp_05",
            "害羞" or "脸红" => "exp_06",
            "惊讶" or "哇" => "exp_07",
            "生气" or "哼" => "exp_08",
            _ => int.TryParse(expression, out _) ? "exp_" + expression.PadLeft(2, '0') : "exp_01"
        };
        SendToPet(new { type = "expression", id });
    }

    [XmlHandler("pet_move")]
    [Description("移动位置：控制在屏幕上的位移。示例: <pet_move x=\"100\" y=\"50\" duration=\"3000\" /> - 表示向右移100像素，下移50像素")]
    public async Task PetMove(XmlTagContext context)
    {
        if (context.Status != TagStatus.OneShot)
            return;

        var currentTag = context.CallChain.LastOrDefault();
        string? sx = null, sy = null, sd = null;

        if (currentTag.Attributes != null)
        {
            currentTag.Attributes.TryGetValue("x", out sx);
            currentTag.Attributes.TryGetValue("y", out sy);
            currentTag.Attributes.TryGetValue("duration", out sd);
        }

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
        if (duration <= 0) duration = 1000;

        moveTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        SendToPet(new { type = "window-move", x, y, duration });
        await Task.WhenAny(moveTcs.Task, Task.Delay(duration + 1000));
        moveTcs = null;
    }

    [XmlHandler("pet_mtn")]
    [Description("执行动作：播放预设动画。支持：害羞，摇头，点头；示例: <pet_mtn>害羞</pet_mtn>")]
    public void PetMotion(XmlTagContext context)
    {
        if (context.Status != TagStatus.Closing)
            return;
        if (string.IsNullOrWhiteSpace(context.FullContent))
            return;

        string motion = context.FullContent.Trim();
        int index = motion switch {
            "害羞" => 0,
            "摇头" => 1,
            "点头" => 2,
            _ => int.TryParse(motion, out var i) ? i : 2
        };

        SendToPet(new { type = "motion", group = "TapBody", index });
    }

    [XmlHandler("pet_pos")]
    [Description("获取位置：获取当前在屏幕上的绝对坐标。示例: <pet_pos />")]
    public async Task PetPos(XmlTagContext context)
    {
        if (context.Status != TagStatus.OneShot)
            return;

        posTcs = new TaskCompletionSource<(double, double)>(TaskCreationOptions.RunContinuationsAsynchronously);
        SendToPet(new { type = "get-position" });

        var result = await Task.WhenAny(posTcs.Task, Task.Delay(2000));
        if (result == posTcs.Task)
        {
            var (x, y) = await posTcs.Task;
            chatBot.Poke($"[DeskPetService] 当前坐标: x={x}, y={y}");
        }
        else
        {
            chatBot.Poke("[DeskPetService] 获取坐标超时");
        }
        posTcs = null;
    }

    Process? petProcess;
    ChatBot chatBot = null!;
    TaskCompletionSource? moveTcs;
    TaskCompletionSource<(double, double)>? posTcs;

    public DeskPetService(InterpreterService interpreterService)
    {
        interpreterService.RegisterHandler(this);
    }

    public override Task AwakeAsync(AwakeContext context)
    {
        context.contextBuilder.ChatHistory.AddSystemMessage("""
                                                            # DeskPetService 互动功能指南
                                                            你可以通过特殊标签控制你的互动表现，请根据对话情境使用：
                                                            1. **气泡文字**：`<pet_bubble>内容</pet_bubble>` (文本消息的视觉呈现)
                                                            2. **表情控制**：`<pet_exp>类型</pet_exp>`
                                                               - 支持：`开心` (繁星眼), `害羞`, `闭眼`, `悲伤`, `惊讶`, `生气`
                                                            3. **动作控制**：`<pet_mtn>类型</pet_mtn>`
                                                               - 支持：`点头`, `摇头`, `害羞`
                                                            4. **生理反应 (Poke)**：当你收到系统的物理干扰消息时，应根据你的角色设定做出自然的反应（如惊讶、头晕、脸红等）。
                                                            5. **获取位置**：`<pet_pos />` (获取桌宠当前在屏幕上的坐标)
                                                            """);
        return Task.CompletedTask;
    }

    public override Task StartAsync(Kernel kernel, ChatActivity chatActivity)
    {
        chatBot = chatActivity.ChatBot;
        chatActivity.ChatBot.ChatSent += _ => InternalReset();

        try
        {
            const string PetExePath = "../../Alife.DeskPet/debug/Alife.DeskPet.exe";
            if (File.Exists(PetExePath) == false)
            {
                Console.WriteLine($"[PetService] Error: Could not find Pet EXE at {PetExePath}");
                return Task.CompletedTask;
            }

            ProcessStartInfo psi = new ProcessStartInfo {
                FileName = PetExePath,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardInputEncoding = Encoding.UTF8,
                StandardOutputEncoding = Encoding.UTF8,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(PetExePath)
            };

            petProcess = new Process { StartInfo = psi };
            petProcess.OutputDataReceived += (s, e) => OnPetMessageReceived(e.Data);
            petProcess.ErrorDataReceived += (s, e) => {
                if (e.Data != null) Console.WriteLine($"[Pet Error] {e.Data}");
            };

            petProcess.Start();
            petProcess.BeginOutputReadLine();
            petProcess.BeginErrorReadLine();

            Console.WriteLine("[PetService] Pet process started successfully.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PetService] Failed to start pet: {ex.Message}");
        }

        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (petProcess != null && !petProcess.HasExited)
        {
            petProcess.Kill();
            await petProcess.WaitForExitAsync();
            petProcess.Dispose();
            petProcess = null;
        }
    }

    void OnPetMessageReceived(string? data)
    {
        if (string.IsNullOrWhiteSpace(data))
            return;
        // Console.WriteLine($"[{nameof(PetService)}] {data}");

        using var doc = JsonDocument.Parse(data);
        var root = doc.RootElement;
        if (!root.TryGetProperty("type", out var typeProp)) return;
        var type = typeProp.GetString();

        if (type == "chat")
        {
            var text = root.GetProperty("text").GetString();
            if (!string.IsNullOrEmpty(text))
            {
                chatBot.Chat("[DeskPetService] " + text);
            }
        }
        else if (type == "poke")
        {
            var text = root.GetProperty("text").GetString();
            if (!string.IsNullOrEmpty(text))
            {
                chatBot.Poke("[DeskPetService] " + text);
            }
        }
        else if (type == "move-finished")
        {
            moveTcs?.TrySetResult();
        }
        else if (type == "position")
        {
            double x = root.GetProperty("x").GetDouble();
            double y = root.GetProperty("y").GetDouble();
            posTcs?.TrySetResult((x, y));
        }
    }

    void SendToPet(object msg)
    {
        if (petProcess is { HasExited: false })
        {
            try
            {
                string json = JsonSerializer.Serialize(msg);
                petProcess.StandardInput.WriteLine(json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PetService] Send Error: {ex.Message}");
            }
        }
    }

    void InternalReset()
    {
        moveTcs?.TrySetCanceled();
        posTcs?.TrySetCanceled();
    }
}
