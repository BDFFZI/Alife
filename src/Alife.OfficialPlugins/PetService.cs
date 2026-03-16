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
    private readonly ChatWindow _chatWindow;
    private ChatActivity? _chatActivity;
    private readonly StringBuilder _bubbleBuffer = new();

    public PetService(InterpreterService interpreterService, ChatWindow chatWindow)
    {
        _chatWindow = chatWindow;
        interpreterService.RegisterHandler(this);
    }

    public override Task StartAsync(Kernel kernel, ChatActivity chatActivity)
    {
        _chatActivity = chatActivity;
        
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

            ProcessStartInfo psi = new ProcessStartInfo
            {
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
            _petProcess.ErrorDataReceived += (s, e) => { if (e.Data != null) Console.WriteLine($"[Pet Error] {e.Data}"); };

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
                    _chatWindow.AddMessage(new ChatMessage { content = text, isUser = true });
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
    public Task PetBubble(
        XmlTagContext context, 
        [XmlTagContent] string? content = null)
    {
        if (!string.IsNullOrEmpty(content))
        {
            _bubbleBuffer.Append(content);
        }

        if (context.IsClosing)
        {
            string finalResult = _bubbleBuffer.ToString();
            _bubbleBuffer.Clear();
            if (!string.IsNullOrWhiteSpace(finalResult))
            {
                LookForward();
                int duration = 2000 + (finalResult.Length * 100);
                SendToPet(new { type = "bubble", text = finalResult, duration });
            }
        }
        return Task.CompletedTask;
    }

    [XmlHandler("pet_exp")]
    [Description("表情喵。示例: <pet_exp>06</pet_exp> (01-08)")]
    public Task PetExpression([XmlTagContent] string id = "exp_01")
    {
        id = id.Trim();
        if (id.Length <= 2 && int.TryParse(id, out _)) id = "exp_" + id.PadLeft(2, '0');
        SendToPet(new { type = "expression", id });
        return Task.CompletedTask;
    }

    [XmlHandler("pet_mtn")]
    [Description("动作喵。示例: <pet_mtn>5</pet_mtn> (0-5)")]
    public Task PetMotion([XmlTagContent] string motion = "2")
    {
        motion = motion.Trim();
        int index = 0;
        if (!int.TryParse(motion, out index))
        {
            index = motion switch {
                "害羞" => 0, "摇头" => 1, "点头" => 2, "欢迎" => 3, "旋转" => 4, "跳舞" => 5,
                _ => 2
            };
        }
        SendToPet(new { type = "motion", group = "TapBody", index });
        return Task.CompletedTask;
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
