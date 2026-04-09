using System.Text.Json;
using System.Text.Json.Serialization;

namespace Alife.Function.DeskPet;

/// <summary>
/// 基础 IPC 指令基类
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type", IgnoreUnrecognizedTypeDiscriminators = true)]
[JsonDerivedType(typeof(WindowMoveCommand), "window-move")]
[JsonDerivedType(typeof(GetPositionCommand), "get-position")]
[JsonDerivedType(typeof(GenericBridgeCommand), "bubble")]
[JsonDerivedType(typeof(GenericBridgeCommand), "expression")]
[JsonDerivedType(typeof(GenericBridgeCommand), "motion")]
[JsonDerivedType(typeof(GenericBridgeCommand), "look")]
[JsonDerivedType(typeof(GenericBridgeCommand), "hide-bubble")]
public abstract record IpcCommand;

public record WindowMoveCommand(double X, double Y, int Duration) : IpcCommand;
public record GetPositionCommand() : IpcCommand;
public record GenericBridgeCommand(string type) : IpcCommand;

/// <summary>
/// 负责 StdIn/StdOut 的原始通讯适配
/// </summary>
public class PetIpcHandler
{
    public event Action<IpcCommand>? CommandReceived;
    public event Action? ConnectionLost;

    public PetIpcHandler()
    {
        Console.InputEncoding = System.Text.Encoding.UTF8;
        Console.OutputEncoding = System.Text.Encoding.UTF8;
    }

    public void StartListening()
    {
        Task.Run(() =>
        {
            while (true)
            {
                try
                {
                    string? line = Console.ReadLine();
                    if (line == null)
                    {
                        ConnectionLost?.Invoke();
                        break;
                    }

                    IpcCommand? cmd = JsonSerializer.Deserialize<IpcCommand>(line, jsonOptions);
                    if (cmd != null)
                    {
                        CommandReceived?.Invoke(cmd);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[IPC] Parse Error: {ex.Message}");
                }
            }
        });
    }

    public void SendEvent(object evt)
    {
        try
        {
            string json = JsonSerializer.Serialize(evt, jsonOptions);
            Console.WriteLine(json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[IPC] Send Error: {ex.Message}");
        }
    }

    readonly JsonSerializerOptions jsonOptions = new() { PropertyNameCaseInsensitive = true };
}
