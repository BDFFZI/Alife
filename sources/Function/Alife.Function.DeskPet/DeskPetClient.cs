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
    public Dictionary<string, (string Group, int Index)> SupportedMotions { get; } = new();
    public Dictionary<string, List<InteractionItem>> Interactions { get; } = new();

    public DeskPetClient()
    {
        string modelJsonPath = $"{Environment.OutputsFolderPath}/wwwroot/model/Mao/Mao.model3.json";
        if (File.Exists(modelJsonPath) == false)
            throw new FileNotFoundException($"找不到桌宠模型配置文件：{modelJsonPath}");

        using JsonDocument jsonDoc = JsonDocument.Parse(File.ReadAllText(modelJsonPath));
        JsonElement root = jsonDoc.RootElement;
        if (root.TryGetProperty("FileReferences", out JsonElement refs) == false)
            throw new InvalidDataException("模型配置文件无效：缺少 FileReferences 节点");

        // 1. Expressions
        if (refs.TryGetProperty("Expressions", out JsonElement exps))
        {
            foreach (JsonElement exp in exps.EnumerateArray())
            {
                if (exp.TryGetProperty("Name", out JsonElement nameProp))
                {
                    string? name = nameProp.GetString();
                    if (string.IsNullOrEmpty(name) == false) SupportedExpressions.Add(name);
                }
            }
        }

        // 2. Motions
        if (refs.TryGetProperty("Motions", out JsonElement motionsJson))
        {
            foreach (JsonProperty groupProp in motionsJson.EnumerateObject())
            {
                string groupName = groupProp.Name;
                int index = 0;
                foreach (JsonElement motionItem in groupProp.Value.EnumerateArray())
                {
                    if (motionItem.TryGetProperty("Name", out JsonElement nameProp))
                    {
                        string? name = nameProp.GetString();
                        if (string.IsNullOrEmpty(name) == false) SupportedMotions[name] = (groupName, index);
                    }
                    index++;
                }
            }
        }

        // 3. Interactions (Dialogues)
        if (root.TryGetProperty("Interaction", out JsonElement interactJson) &&
            interactJson.TryGetProperty("Dialogues", out JsonElement dialogues))
        {
            foreach (JsonProperty poolProp in dialogues.EnumerateObject())
            {
                Interactions[poolProp.Name] = JsonSerializer.Deserialize<List<InteractionItem>>(poolProp.Value.GetRawText(), jsonOptions) ?? new();
            }
        }
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

    public void ShowBubble(string text, int duration = 4000)
    {
        Send(new { type = "bubble", text });
        
        // 逻辑上移：在客户端处理气泡消失逻辑
        if (duration > 0)
        {
            bubbleCts?.Cancel();
            bubbleCts = new CancellationTokenSource();
            _ = HideBubbleDelayed(duration, bubbleCts.Token);
        }
    }

    public void HideBubble() => Send(new { type = "hide-bubble" });

    async Task HideBubbleDelayed(int delayMs, CancellationToken token)
    {
        try
        {
            await Task.Delay(delayMs, token);
            HideBubble();
        }
        catch (OperationCanceledException) { }
    }

    public void SetExpression(string id) => Send(new { type = "expression", id });
    public void PlayMotion(string group, int index) => Send(new { type = "motion", group, index });

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

    public void ResetInteractions()
    {
        comboCount = 0;
        lastInteractionTime = 0;
        bubbleCts?.Cancel();
        moveTcs?.TrySetCanceled();
        posTcs?.TrySetCanceled();
    }

    void OnOutputDataReceived(object sender, DataReceivedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.Data)) return;

        try
        {
            using JsonDocument doc = JsonDocument.Parse(e.Data);
            JsonElement root = doc.RootElement;
            if (root.TryGetProperty("type", out JsonElement typeProp) == false) return;
            string? type = typeProp.GetString();

            switch (type)
            {
                case "ready": ExecuteInteraction("startup"); break;
                case "hit": HandleHit(root.GetProperty("areas").EnumerateArray().Select(x => x.GetString() ?? "").ToList()); break;
                case "chat": OnChat?.Invoke(root.GetProperty("text").GetString() ?? ""); break;
                case "shake": ExecuteInteraction("shake", "(物理干扰) 用户在大幅移动你的位置！"); break;
                case "move": ExecuteInteraction("move", "(物理干扰) 用户在平移你的窗口！"); break;
                case "pmove-finished": moveTcs?.TrySetResult(); break;
                case "position":
                    posTcs?.TrySetResult((root.GetProperty("x").GetDouble(), root.GetProperty("y").GetDouble()));
                    break;
            }
        }
        catch { /* Ignore non-json or corrupt data */ }
    }

    void HandleHit(List<string> areas)
    {
        long now = DateTimeOffset.Now.ToUnixTimeMilliseconds();
        if (now - lastInteractionTime < 2500) comboCount++;
        else comboCount = 1;
        lastInteractionTime = now;

        if (comboCount >= 5 && comboCount % 5 == 0)
        {
            ExecuteInteraction("combo", $"(连击干扰) 用户一直在连戳你（Combo {comboCount}）");
            return;
        }

        string category = "random";
        if (areas.Any(a => a.Contains("Head", StringComparison.OrdinalIgnoreCase))) category = "head";
        else if (areas.Any(a => a.Contains("Body", StringComparison.OrdinalIgnoreCase))) category = "body";

        ExecuteInteraction(category);
    }

    void ExecuteInteraction(string type, string? pokeMsg = null)
    {
        if (Interactions.TryGetValue(type, out List<InteractionItem>? pool) == false || pool.Count == 0) return;

        InteractionItem item = pool[Random.Shared.Next(pool.Count)];
        
        if (string.IsNullOrEmpty(item.Exp) == false) SetExpression(item.Exp);
        if (item.Mtn != null) PlayMotion(item.Mtn.Group, item.Mtn.Index);
        
        // 如果有台词，则显示气泡
        if (string.IsNullOrEmpty(item.Text) == false) ShowBubble(item.Text);
        
        // 触发上报事件
        OnPoke?.Invoke(pokeMsg ?? $"(交互: {type}) {item.Text}");
    }

    void Send(object msg)
    {
        if (petProcess is { HasExited: false })
            petProcess.StandardInput.WriteLine(JsonSerializer.Serialize(msg));
    }

    public async ValueTask DisposeAsync()
    {
        ResetInteractions();
        if (petProcess != null && petProcess.HasExited == false)
        {
            petProcess.Kill();
            await petProcess.WaitForExitAsync();
            petProcess.Dispose();
            petProcess = null;
        }
    }

    Process? petProcess;
    TaskCompletionSource? moveTcs;
    TaskCompletionSource<(double, double)>? posTcs;
    CancellationTokenSource? bubbleCts;
    
    int comboCount;
    long lastInteractionTime;

    static readonly JsonSerializerOptions jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public class InteractionItem
    {
        public string Text { get; set; } = "";
        public string Exp { get; set; } = "";
        public MotionRef? Mtn { get; set; }
    }

    public class MotionRef
    {
        public string Group { get; set; } = "";
        public int Index { get; set; }
    }
}
