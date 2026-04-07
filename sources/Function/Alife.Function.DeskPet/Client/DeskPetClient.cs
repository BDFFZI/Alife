using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using Environment = Alife.Basic.Environment;

namespace Alife.Function.DeskPet;

/// <summary>
/// 负责与 Alife.DeskPet.exe 进程的 IPC 通讯
/// </summary>
public class DeskPetClient : IAsyncDisposable
{
    public event Action<string>? OnChat;
    public event Action<string>? OnPoke;

    public List<string> SupportedExpressions { get; private set; } = new();
    
    // Key: Semantic Action Name, Value: (Group, Index)
    public Dictionary<string, (string Group, int Index)> SupportedMotions { get; } = new();

    public DeskPetClient()
    {
        string modelJsonPath = $"{Environment.OutputsFolderPath}/wwwroot/model/Mao/Mao.model3.json";
        if (File.Exists(modelJsonPath) == false)
            throw new FileNotFoundException($"找不到桌宠模型配置文件：{modelJsonPath}");

        using JsonDocument jsonDoc = JsonDocument.Parse(File.ReadAllText(modelJsonPath));
        if (jsonDoc.RootElement.TryGetProperty("FileReferences", out JsonElement refs) == false ||
            refs.TryGetProperty("Expressions", out JsonElement exps) == false)
        {
            throw new InvalidDataException("模型配置文件无效：缺少 Expressions 节点配置");
        }

        SupportedExpressions.Clear();
        foreach (JsonElement exp in exps.EnumerateArray())
        {
            if (exp.TryGetProperty("Name", out JsonElement nameProp))
            {
                string? name = nameProp.GetString();
                if (string.IsNullOrEmpty(name) == false)
                {
                    SupportedExpressions.Add(name);
                }
            }
        }
        
        SupportedMotions.Clear();
        if (refs.TryGetProperty("Motions", out JsonElement motionsJson))
        {
            foreach (JsonProperty groupProp in motionsJson.EnumerateObject())
            {
                string groupName = groupProp.Name;
                int index = 0;
                foreach (JsonElement motionItem in groupProp.Value.EnumerateArray())
                {
                    if (motionItem.TryGetProperty("Name", out JsonElement motionNameProp))
                    {
                        string? name = motionNameProp.GetString();
                        if (string.IsNullOrEmpty(name) == false)
                        {
                            SupportedMotions[name] = (groupName, index);
                        }
                    }
                    index++;
                }
            }
        }
    }

    public void ResetInteractions()
    {
        InternalReset();
    }

    public void ShowBubble(string text, int duration)
    {
        Send(new { type = "bubble", text, duration });
    }

    public void SetExpression(string id)
    {
        Send(new { type = "expression", id });
    }

    public void PlayMotion(string group, int index)
    {
        Send(new { type = "motion", group, index });
    }

    public async Task MoveAsync(double x, double y, int duration)
    {
        moveTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Send(new { type = "window-move", x, y, duration });
        await Task.WhenAny(moveTcs.Task, Task.Delay(duration + 1000));
        moveTcs = null;
    }

    public async Task<(double x, double y)> GetPositionAsync(int timeoutMs = 2000)
    {
        posTcs = new TaskCompletionSource<(double, double)>(TaskCreationOptions.RunContinuationsAsynchronously);
        Send(new { type = "get-position" });

        Task completedTask = await Task.WhenAny(posTcs.Task, Task.Delay(timeoutMs));
        if (completedTask == posTcs.Task)
        {
            (double x, double y) result = await posTcs.Task;
            posTcs = null;
            return result;
        }
        
        posTcs = null;
        throw new TimeoutException("获取位置超时");
    }

    Process? petProcess;
    TaskCompletionSource? moveTcs;
    TaskCompletionSource<(double, double)>? posTcs;

    void OnOutputDataReceived(object sender, DataReceivedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.Data)) return;

        try
        {
            using JsonDocument doc = JsonDocument.Parse(e.Data);
            JsonElement root = doc.RootElement;
            if (root.TryGetProperty("type", out JsonElement typeProp) == false) return;
            
            string? type = typeProp.GetString();

            if (type == "chat")
            {
                OnChat?.Invoke(root.GetProperty("text").GetString() ?? "");
            }
            else if (type == "poke")
            {
                OnPoke?.Invoke(root.GetProperty("text").GetString() ?? "");
            }
            else if (type == "pmove-finished")
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
        catch (JsonException)
        {
            // 非 JSON 格式的普通日志输出，可忽略或纯打印
        }
    }

    void Send(object msg)
    {
        if (petProcess is { HasExited: false })
        {
            string json = JsonSerializer.Serialize(msg);
            petProcess.StandardInput.WriteLine(json);
        }
    }

    void InternalReset()
    {
        moveTcs?.TrySetCanceled();
        posTcs?.TrySetCanceled();
    }

    public void Start()
    {
        string petExePath = $"{Environment.OutputsFolderPath}/Alife.Function.DeskPet.exe";
        if (File.Exists(petExePath) == false)
            throw new FileNotFoundException($"找不到桌宠前端程序：{petExePath}");

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
        petProcess.OutputDataReceived += OnOutputDataReceived;
        petProcess.ErrorDataReceived += (s, e) => {
            if (e.Data != null) Console.WriteLine($"[Pet Error] {e.Data}");
        };

        petProcess.Start();
        petProcess.BeginOutputReadLine();
        petProcess.BeginErrorReadLine();
    }

    public async ValueTask DisposeAsync()
    {
        InternalReset();
        if (petProcess != null && petProcess.HasExited == false)
        {
            petProcess.Kill();
            await petProcess.WaitForExitAsync();
            petProcess.Dispose();
            petProcess = null;
        }
    }
}
