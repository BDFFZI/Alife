using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Alife.Foundation;

namespace Alife.Function.DeskPet;

/// <summary>
/// 桌宠服务的控制中枢，负责管理进程生命周期与业务逻辑分配
/// </summary>
public class PetServer : IAsyncDisposable
{
    public event Action<string>? OnInput;
    public event Action<string>? OnInteracted;

    public IEnumerable<string> SupportedExpressions => metadata.Expressions;
    public IDictionary<string, (string Group, int Index)> SupportedMotions => metadata.Motions;

    public PetServer(string clientPath, string modelName)
    {
        //加载模型信息
        string modelJsonPath = PetModelMetadata.ResolveModelJsonPath(Path.Combine(clientPath, "wwwroot/model"), modelName);
        metadata = PetModelMetadata.Load(modelJsonPath);

        //创建进程
        string petExePath = Path.Combine(clientPath, "Alife.DeskPet.Client.exe");
        if (File.Exists(petExePath) == false)
            throw new FileNotFoundException($"找不到桌宠程序: {petExePath}");
        nativeProcess = new Process {
            StartInfo = new ProcessStartInfo {
                FileName = petExePath,
                Arguments = $"\"{modelJsonPath}\"",
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardInputEncoding = new UTF8Encoding(false),
                StandardOutputEncoding = new UTF8Encoding(false),
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(petExePath)
            }
        };
        nativeProcess.Start();
        ProcessTracker.Track(nativeProcess);

        //进程异常信息
        nativeProcess.BeginErrorReadLine();
        nativeProcess.ErrorDataReceived += (_, e) => {
            if (e.Data != null) Console.WriteLine($"[PetProcess Error] {e.Data}");
        };

        //创建桌宠进行封装器
        petProcess = new PetProcess(nativeProcess.StandardInput, nativeProcess.StandardOutput);
        petProcess.OutputReceived += OnEventReceived;
        petProcess.ListenOutput();// 宿主只听输出(Event)
    }
    public async ValueTask DisposeAsync()
    {
        ResetInteractions();
        if (nativeProcess.HasExited == false)
        {
            nativeProcess.Kill();
            nativeProcess.Dispose();
        }
        petProcess.Dispose();
        await Task.CompletedTask;
    }
    public async Task WaitReadyAsync()
    {
        using CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromSeconds(15));
        await using CancellationTokenRegistration registration = cancellationTokenSource.Token.Register(() => readyTask.TrySetException(new TimeoutException("无法连接到桌宠客户端")));
        await readyTask.Task;
    }

    public void ShowBubble(string text) => petProcess.SendInput(new BubbleCommand(text));

    public void HideBubble() => petProcess.SendInput(new HideBubbleCommand());

    public void PlayExpression(string? id) => petProcess.SendInput(new PlayExpressionCommand(id));

    public void PlayMotion(string group, int index) => petProcess.SendInput(new MotionCommand(group, index));
    public void SendStatus(bool working) => petProcess.SendInput(new StatusCommand(working));

    public async Task MoveAsync(double x, double y, int duration)
    {
        petProcess.SendInput(new WindowMoveCommand(x, y, duration));
        await Task.Delay(duration + 200);
    }


    public async Task<(double x, double y)> GetPositionAsync()
    {
        positionTask = new TaskCompletionSource<(double, double)>(TaskCreationOptions.RunContinuationsAsynchronously);
        petProcess.SendInput(new GetPositionCommand());

        Task completedTask = await Task.WhenAny(positionTask.Task, Task.Delay(2000));
        if (completedTask == positionTask.Task)
        {
            (double x, double y) result = await positionTask.Task;
            positionTask = null;
            return result;
        }

        positionTask = null;
        throw new TimeoutException("获取桌宠位置超时");
    }

    public void ResetInteractions()
    {
        positionTask?.TrySetCanceled();
    }

    readonly Process nativeProcess;
    readonly PetProcess petProcess;
    readonly PetModelMetadata metadata;
    readonly TaskCompletionSource readyTask = new();
    TaskCompletionSource<(double, double)>? positionTask;

    void OnEventReceived(IpcEvent ev)
    {
        switch (ev)
        {
            case ReadyEvent: readyTask.TrySetResult(); break;
            case InputEvent input: OnInput?.Invoke(input.Text); break;
            case InteractionEvent interaction: OnInteracted?.Invoke(interaction.Interaction); break;
            case PositionEvent position: positionTask?.TrySetResult((position.X, position.Y)); break;
        }
    }
}
