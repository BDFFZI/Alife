using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Alife.Function.DeskPet;

public record InteractionItem
{
    public string? Text { get; set; }
    public string? Exp { get; set; }
    public MotionRef? Mtn { get; set; }
}

public record MotionRef(string Group, int Index);


/// <summary>
/// 负责桌宠的自主业务行为逻辑 (灵魂)
/// </summary>
public class PetActivity
{
    public PetBridge? Bridge { get; private set; }
    public Dictionary<string, List<InteractionItem>> Interactions { get; } = new();

    public PetActivity(PetProcess process, IPetWindow window)
    {
        this.process = process;
        this.window = window;
        detector = new InterferenceDetector();
        tracker = new MouseTracker();

        this.process.InputReceived += OnCommandReceived;
        
        detector.Shaked += () => {
            ExecuteInteraction("shake");
            this.process.SendOutput(new ShakeEvent());
        };
        detector.Moved += () => {
            ExecuteInteraction("move");
            this.process.SendOutput(new MoveEvent());
        };
        detector.MouseShaked += () => ExecuteInteraction("random");
        
        tracker.MouseMoved += (int x, int y) => HandleMouseMove(x, y);

        LoadInteractionConfig();
    }

    public void Initialize(PetBridge bridge)
    {
        this.Bridge = bridge;
        this.Bridge.OnHit += (List<string> areas) => {
            HandleHit(areas);
            process.SendOutput(new HitEvent(areas));
        };
        this.Bridge.OnChat += (string text) => process.SendOutput(new ChatEvent(text));
        this.Bridge.OnReady += () => {
            Bridge.LoadModelAsync("model/Mao/Mao.model3.json");
            ExecuteInteraction("startup");
            process.SendOutput(new ReadyEvent());
        };
        this.Bridge.OnDragRequest += () => process.SendOutput(new DragRequestEvent());

        tracker.Start();
        _ = StartFocusResetLoop();
    }

    public void HandleMouseMove(int x, int y)
    {
        lastMouseMoveTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
        (double ScaleX, double ScaleY) dpi = window.GetDpi();
        (double Left, double Top, double Width, double Height) layout = window.GetLayout();

        double logicalMouseX = x / dpi.ScaleX;
        double logicalMouseY = y / dpi.ScaleY;
        double centerX = layout.Left + layout.Width / 2;
        double centerY = layout.Top + layout.Height / 2;

        detector.ReportMouseLocation(logicalMouseX, logicalMouseY, centerX, centerY);
        detector.ReportLocation(layout.Left, layout.Top);

        double nx = (logicalMouseX - centerX) / (layout.Width / 2);
        double ny = (logicalMouseY - centerY) / (layout.Height / 2);

        Bridge?.SetFocusAsync(nx, ny);
    }

    public void ExecuteInteraction(string type)
    {
        if (Interactions.TryGetValue(type, out List<InteractionItem>? pool) == false || pool.Count == 0) return;
        InteractionItem item = pool[Random.Shared.Next(pool.Count)];
        
        if (string.IsNullOrEmpty(item.Exp) == false) PlayExpression(item.Exp);
        if (item.Mtn != null) Bridge?.PlayMotionAsync(item.Mtn.Group, item.Mtn.Index);
        if (string.IsNullOrEmpty(item.Text) == false) 
        {
            Bridge?.ShowBubbleAsync(item.Text);
            process.SendOutput(new PokeEvent($"(交互: {type}) {item.Text}"));
        }
    }

    public void PlayExpression(string? id, int duration = 5000)
    {
        if (Bridge == null) return;
        Bridge.PlayExpressionAsync(id);

        expressionCts?.Cancel();
        expressionCts = null;

        if (string.IsNullOrEmpty(id) == false)
        {
            expressionCts = new CancellationTokenSource();
            CancellationToken token = expressionCts.Token;
            _ = Task.Run(async () => {
                try {
                    await Task.Delay(duration, token);
                    if (token.IsCancellationRequested == false) Bridge.PlayExpressionAsync(null);
                } catch (OperationCanceledException) { }
            }, token);
        }
    }

    void LoadInteractionConfig()
    {
        try
        {
            string modelJsonPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wwwroot/model/Mao/Mao.model3.json");
            if (File.Exists(modelJsonPath) == false) return;

            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(modelJsonPath));
            if (doc.RootElement.TryGetProperty("Interaction", out JsonElement interactJson) &&
                interactJson.TryGetProperty("Dialogues", out JsonElement dialogues))
            {
                foreach (JsonProperty poolProp in dialogues.EnumerateObject())
                {
                    Interactions[poolProp.Name] = JsonSerializer.Deserialize<List<InteractionItem>>(poolProp.Value.GetRawText(), PetProcess.JsonOptions) ?? new();
                }
            }
        }
        catch { }
    }

    void HandleHit(List<string> areas)
    {
        long now = DateTimeOffset.Now.ToUnixTimeMilliseconds();
        if (now - lastInteractionTime < 2500) comboCount++;
        else comboCount = 1;
        lastInteractionTime = now;

        if (comboCount >= 5 && comboCount % 5 == 0)
        {
            ExecuteInteraction("combo");
            return;
        }

        string category = "random";
        if (areas.Any(a => a.Contains("Head", StringComparison.OrdinalIgnoreCase))) category = "head";
        else if (areas.Any(a => a.Contains("Body", StringComparison.OrdinalIgnoreCase))) category = "body";

        ExecuteInteraction(category);
    }

    void OnCommandReceived(IpcCommand cmd)
    {
        switch (cmd)
        {
            case WindowMoveCommand moveCmd:
                window.ProgrammaticMove(moveCmd.X, moveCmd.Y, moveCmd.Duration);
                break;
            case GetPositionCommand:
                (double Left, double Top, double Width, double Height) layout = window.GetLayout();
                (double ScaleX, double ScaleY) dpi = window.GetDpi();
                process.SendOutput(new PositionEvent((layout.Left + layout.Width/2) * dpi.ScaleX, (layout.Top + layout.Height/2) * dpi.ScaleY));
                break;
            case BubbleCommand b: Bridge?.ShowBubbleAsync(b.Text); break;
            case PlayExpressionCommand e: PlayExpression(e.Id); break;
            case MotionCommand m: Bridge?.PlayMotionAsync(m.Group, m.Index); break;
            case HideBubbleCommand: Bridge?.HideBubbleAsync(); break;
        }
    }

    async Task StartFocusResetLoop()
    {
        while (true)
        {
            await Task.Delay(500);
            if (DateTimeOffset.Now.ToUnixTimeMilliseconds() - lastMouseMoveTime > 3000)
            {
                Bridge?.SetFocusAsync(0, 0);
            }
        }
    }

    PetProcess process;
    IPetWindow window;
    InterferenceDetector detector;
    MouseTracker tracker;
    
    int comboCount;
    long lastInteractionTime;
    long lastMouseMoveTime;
    CancellationTokenSource? expressionCts;
}
