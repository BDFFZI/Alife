using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Environment = Alife.Basic.Environment;

namespace Alife.Function.DeskPet;

/// <summary>
/// 桌宠服务的控制中枢，负责管理进程生命周期与业务逻辑分配
/// </summary>
public class PetServer : IAsyncDisposable
{
    public event Action<string>? OnChat;
    public event Action<string>? OnPoke;

    /// <summary>
    /// AI 宿主构造函数
    /// </summary>
    public PetServer()
    {
        string petExePath = Path.Combine(Environment.OutputsFolderPath, "Alife.Function.DeskPet.exe");
        process = new PetProcess(petExePath);
        process.EventReceived += OnEventReceived;
        
        ParseMetadata();
    }

    /// <summary>
    /// 桌宠桌面进程构造函数 (注入窗口服务)
    /// </summary>
    public PetServer(IPetWindow window)
    {
        process = new PetProcess();
        activity = new PetActivity(process, window);
        
        ParseMetadata();
    }

    public List<string> SupportedExpressions { get; private set; } = new();
    public Dictionary<string, (string Group, int Index)> SupportedMotions { get; } = new();

    public void Start()
    {
        process.Launch();
    }

    public void InitializeActivity(PetBridge bridge)
    {
        activity?.Initialize(bridge);
    }

    public void NotifyMoveFinished() => process.Send(new MoveFinishedEvent());

    public void ShowBubble(string text) => process.Send(new BubbleCommand(text));
    
    public void HideBubble() => process.Send(new HideBubbleCommand());
    
    public void PlayExpression(string? id) => process.Send(new PlayExpressionCommand(id));
    
    public void PlayMotion(string group, int index) => process.Send(new MotionCommand(group, index));

    public async Task MoveAsync(double x, double y, int duration)
    {
        moveTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        process.Send(new WindowMoveCommand(x, y, duration));
        await Task.WhenAny(moveTcs.Task, Task.Delay(duration + 1000));
        moveTcs = null;
    }

    public async Task<(double x, double y)> GetPositionAsync()
    {
        posTcs = new TaskCompletionSource<(double, double)>(TaskCreationOptions.RunContinuationsAsynchronously);
        process.Send(new GetPositionCommand());

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
        moveTcs?.TrySetCanceled();
        posTcs?.TrySetCanceled();
    }

    public async ValueTask DisposeAsync()
    {
        ResetInteractions();
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
            case MoveFinishedEvent: moveTcs?.TrySetResult(); break;
            case PositionEvent pos: posTcs?.TrySetResult((pos.X, pos.Y)); break;
        }
    }

    PetProcess process;
    PetActivity? activity;
    TaskCompletionSource? moveTcs;
    TaskCompletionSource<(double, double)>? posTcs;
}
