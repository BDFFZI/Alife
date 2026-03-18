using System.Text;
using System.Text.Json;
using System.Diagnostics;
using Microsoft.SemanticKernel;
using Alife.Abstractions;
using Alife.Interpreter;
using System.ComponentModel;

namespace Alife.OfficialPlugins;

[Plugin("真央桌宠", "将真央桌宠接入AI系统，实现表现力同步 and 互动反馈。")]
[Description("真央桌宠插件：提供气泡消息、表情控制、动作执行以及 Live2D 参数的底层控制能力。")]
public class PetService : Plugin, IAsyncDisposable
{
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
    [Description("移动桌宠喵。示例: <pet_move x=\"100\" y=\"50\" duration=\"3000\" /> - 表示向右移100像素，下移50像素")]
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
    [Description("执行动作喵。支持：害羞，摇头，点头，欢迎，旋转，跳舞；示例: <pet_mtn>害羞</pet_mtn>")]
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

    [XmlHandler("pet_param")]
    [Description("直接控制 Live2D 参数喵。示例: <pet_param name=\"ParamBodyAngleX\" value=\"10\" duration=\"1000\" />")]
    public void PetParameter(XmlTagContext context)
    {
        if (context.Status != TagStatus.OneShot)
            return;

        var currentTag = context.CallChain.LastOrDefault();
        string? name = null, sValue = null, sDuration = null;

        if (currentTag.Attributes != null)
        {
            currentTag.Attributes.TryGetValue("name", out name);
            currentTag.Attributes.TryGetValue("value", out sValue);
            currentTag.Attributes.TryGetValue("duration", out sDuration);
        }

        if (string.IsNullOrEmpty(name))
            return;

        float.TryParse(sValue, out float value);
        int.TryParse(sDuration, out int duration);
        if (duration <= 0) duration = 1000;

        SendToPet(new { type = "parameter", name, value, duration });
    }

    Process? petProcess;
    ChatBot chatBot = null!;
    TaskCompletionSource? moveTcs;

    public PetService(InterpreterService interpreterService)
    {
        interpreterService.RegisterHandler(this);
    }

    public override Task AwakeAsync(AwakeContext context)
    {
        context.contextBuilder.ChatHistory.AddSystemMessage("""
                                                            # 宠物互动指南
                                                            你可以通过特殊标签控制我的表现，请根据对话情境使用：
                                                            1. **发气泡**：`<pet_bubble>内容</pet_bubble>` (会自动正视主人回复)
                                                            2. **表情控制**：`<pet_exp>类型</pet_exp>`
                                                               - 支持：`开心` (繁星眼), `害羞`, `闭眼`, `悲伤`, `惊讶`, `生气`
                                                            3. **动作控制**：`<pet_mtn>类型</pet_mtn>`
                                                               - 支持：`点头`, `摇头`, `害羞`
                                                            4. **生理反应 (Poke)**：当你收到系统的物理干扰消息时，可以使用 `开心` 或 `惊讶` 来回应。
                                                            5. **高级控制 (参数)**：`<pet_param name="参数名" value="数值" duration="毫秒" />`
                                                               - `ParamAngleX/Y/Z`: 头部转动 (-30 到 30)
                                                               - `ParamBodyAngleX`: 身体侧歪 (-10 到 10)
                                                               - `ParamEyeBallX/Y`: 眼神位置 (-1 到 1)
                                                            """);
        return Task.CompletedTask;
    }

    public override Task StartAsync(Kernel kernel, ChatActivity chatActivity)
    {
        chatBot = chatActivity.ChatBot;
        chatActivity.ChatBot.ChatSent += _ => InternalReset();

        try
        {
            string assemblyDir = AppDomain.CurrentDomain.BaseDirectory;
            string petExePath = Path.Combine(assemblyDir, "Alife.Pet.exe");

            if (File.Exists(petExePath) == false)
            {
                petExePath = Path.Combine(assemblyDir, "src", "Alife.Pet", "bin", "Debug", "net10.0-windows", "Alife.Pet.exe");
            }
            if (File.Exists(petExePath) == false)
            {
                //TODO 不能使用绝对路径，应该是构建Pet项目时，自动复制文件
                petExePath = @"c:\Users\13309\Desktop\Alife\src\Alife.Pet\bin\Debug\net10.0-windows\Alife.Pet.exe";
            }
            if (File.Exists(petExePath) == false)
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

    public override async Task DestroyAsync()
    {
        await DisposeAsync();
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
                chatBot.Chat(text);
            }
        }
        else if (type == "poke")
        {
            var text = root.GetProperty("text").GetString();
            if (!string.IsNullOrEmpty(text))
            {
                chatBot.Poke(text);
            }
        }
        else if (type == "move-finished")
        {
            moveTcs?.TrySetResult();
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
    }
}
