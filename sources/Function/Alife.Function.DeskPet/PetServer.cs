using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using Environment = Alife.Basic.Environment;

namespace Alife.Function.DeskPet;

/// <summary>
/// 桌宠服务的控制中枢，负责管理进程生命周期与业务逻辑分配
/// </summary>
public class PetServer : IAsyncDisposable
{
    public event Action<string>? OnChat;
    public event Action<string>? OnPoke;

    public List<string> SupportedExpressions { get; } = new();
    public Dictionary<string, (string Group, int Index)> SupportedMotions { get; } = new();

    PetProcess process;
    Process? nativeProcess;
    PetActivity? activity;
    TaskCompletionSource<(double, double)>? posTcs;

    /// <summary>
    /// AI 宿主构造函数
    /// </summary>
    public PetServer()
    {
        process = null!; // 宿主模式下，在 Start() 时才挂载流
        string petExePath = Path.Combine(Environment.OutputsFolderPath, "Alife.Function.DeskPet.exe");
        if (File.Exists(petExePath) == false) throw new FileNotFoundException($"找不到桌宠程序: {petExePath}");

        nativeProcess = new Process {
            StartInfo = new ProcessStartInfo {
                FileName = petExePath,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardInputEncoding = Encoding.UTF8,
                StandardOutputEncoding = Encoding.UTF8,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(petExePath)
            }
        };

        ParseMetadata();
    }

    /// <summary>
    /// 桌宠桌面进程构造函数 (注入窗口服务)
    /// </summary>
    public PetServer(IPetWindow window)
    {
        process = new PetProcess(Console.Out, Console.In);
        activity = new PetActivity(process, window);

        ParseMetadata();
    }

    public void Start()
    {
        if (nativeProcess != null)
        {
            nativeProcess.Start();
            process = new PetProcess(nativeProcess.StandardInput, nativeProcess.StandardOutput);
            process.OutputReceived += OnEventReceived;

            nativeProcess.BeginErrorReadLine();
            nativeProcess.ErrorDataReceived += (object s, DataReceivedEventArgs e) => {
                if (e.Data != null) Console.WriteLine($"[PetProcess Error] {e.Data}");
            };

            process.ListenOutput(); // 宿主只听输出(Event)
        }
        else
        {
            process.ListenInput(); // 桌宠只听输入(Command)
        }
    }

    public void InitializeActivity(PetBridge bridge)
    {
        activity?.Initialize(bridge);
    }

    public void ShowBubble(string text) => process.SendInput(new BubbleCommand(text));

    public void HideBubble() => process.SendInput(new HideBubbleCommand());

    public void PlayExpression(string? id) => process.SendInput(new PlayExpressionCommand(id));

    public void PlayMotion(string group, int index) => process.SendInput(new MotionCommand(group, index));

    public async Task MoveAsync(double x, double y, int duration)
    {
        process.SendInput(new WindowMoveCommand(x, y, duration));
        await Task.Delay(duration + 200);
    }

    public async Task<(double x, double y)> GetPositionAsync()
    {
        posTcs = new TaskCompletionSource<(double, double)>(TaskCreationOptions.RunContinuationsAsynchronously);
        process.SendInput(new GetPositionCommand());

        Task completedTask = await Task.WhenAny(posTcs.Task, Task.Delay(2000));
        if (completedTask == posTcs.Task)
        {
            (double x, double y) result = await posTcs.Task;
            posTcs = null;
            return result;
        }

        posTcs = null;
        throw new TimeoutException("获取桌宠位置超时");
    }

    public void ResetInteractions()
    {
        posTcs?.TrySetCanceled();
    }

    public async ValueTask DisposeAsync()
    {
        ResetInteractions();
        if (nativeProcess != null && nativeProcess.HasExited == false)
        {
            nativeProcess.Kill();
            nativeProcess.Dispose();
        }
        process.Dispose();
        await Task.CompletedTask;
    }

    void ParseMetadata()
    {
        string modelJsonPath = Path.Combine(Environment.OutputsFolderPath, "wwwroot/model/Mao/Mao.model3.json");
        if (File.Exists(modelJsonPath) == false) return;

        using JsonDocument jsonDoc = JsonDocument.Parse(File.ReadAllText(modelJsonPath));
        JsonElement root = jsonDoc.RootElement;
        if (root.TryGetProperty("FileReferences", out JsonElement refs))
        {
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
        }
    }

    void OnEventReceived(IpcEvent ev)
    {
        switch (ev)
        {
            case ChatEvent chat: OnChat?.Invoke(chat.Text); break;
            case PokeEvent poke: OnPoke?.Invoke(poke.Text); break;
            case PositionEvent pos: posTcs?.TrySetResult((pos.X, pos.Y)); break;
        }
    }
}
