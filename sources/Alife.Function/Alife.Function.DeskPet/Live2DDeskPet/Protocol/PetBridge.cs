using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using ElectronNET.API;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace Alife.Function.DeskPet;

/// <summary>
/// 桌宠主进程 ⇄ 渲染进程的 Electron IPC 桥。
/// 频道固定为 "pet"，信封对象为 { type: string, ...payload }。
/// 渲染进程 → 主进程消息经 <see cref="OnMessage"/> 事件分发。
/// </summary>
public sealed class PetBridge : IDisposable
{
    public event Action<string, JsonElement>? OnMessage;

    /// <summary>向渲染进程发送消息（合并 type 与 payload 为单一信封对象）。</summary>
    public void SendMessage(string type, object? payload = null)
    {
        BrowserWindow? w = window.Window;
        if (w == null) return;
        try
        {
            JObject envelope = new() { ["type"] = type };
            if (payload != null)
            {
                JObject payloadObj = JObject.FromObject(payload);
                foreach (KeyValuePair<string, JToken?> kvp in payloadObj)
                    envelope[kvp.Key] = kvp.Value;
            }
            Electron.IpcMain.Send(w, "pet", envelope);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "发送桌宠消息失败");
        }
    }

    /// <summary>在渲染进程执行 JS 并返回结果。</summary>
    public async Task<string?> ExecuteJavaScriptAsync(string script)
    {
        BrowserWindow? w = window.Window;
        if (w == null) return null;
        try
        {
            return await w.WebContents.ExecuteJavaScriptAsync<string>(script, true);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "执行桌宠脚本失败");
            return null;
        }
    }

    readonly PetWindow window;
    readonly ILogger<Live2DDeskPet> logger;

    public PetBridge(PetWindow window, ILogger<Live2DDeskPet> logger)
    {
        this.window = window;
        this.logger = logger;
        Electron.IpcMain.On("pet", OnIpcMessage);
    }
    public void Dispose()
    {
        Electron.IpcMain.RemoveAllListeners("pet");
    }

    void OnIpcMessage(object? payload)
    {
        string? json = payload?.ToString();
        if (string.IsNullOrEmpty(json))
            return;

        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;
            if (root.TryGetProperty("type", out JsonElement typeProp) == false)
                return;

            string? type = typeProp.GetString();
            if (type == null)
                return;
            
            OnMessage?.Invoke(type, root);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "解析桌宠消息失败");
        }
    }
}