using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System.Text.Json;
using System.Windows;

namespace Alife.Function.DeskPet;

public class PetBridge
{
    public event Action? OnReady;
    public event Action<List<string>>? OnHit;
    public event Action<string>? OnChat;
    public event Action? OnDragRequest;

    public PetBridge(WebView2 webView)
    {
        this.webView = webView;
        this.webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
    }

    public async Task LoadModelAsync(string url)
    {
        await SendCommandAsync(new { type = "load", url });
    }

    public async Task SetExpressionAsync(string id)
    {
        await SendCommandAsync(new { type = "expression", id });
    }

    public async Task PlayMotionAsync(string group, int index)
    {
        await SendCommandAsync(new { type = "motion", group, index });
    }

    public async Task ShowBubbleAsync(string text, int duration)
    {
        await SendCommandAsync(new { type = "bubble", text, duration });
    }

    public async Task SetFocusAsync(double x, double y, bool instant = false)
    {
        await SendCommandAsync(new { type = "look", x, y, instant });
    }

    public async Task SetParameterAsync(string name, double value, int duration)
    {
        await SendCommandAsync(new { type = "parameter", name, value, duration });
    }

    public void HandleRawMouseMove(int x, int y)
    {
        _ = webView.CoreWebView2.ExecuteScriptAsync($"window.handleMouseMove({{ x: {x}, y: {y} }});");
    }

    async Task SendCommandAsync(object command)
    {
        if (webView.CoreWebView2 == null) return;
        string json = JsonSerializer.Serialize(command);
        webView.CoreWebView2.PostWebMessageAsJson(json);
        await Task.CompletedTask;
    }

    void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            string json = e.WebMessageAsJson;
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;

            if (root.TryGetProperty("type", out JsonElement typeProp) == false) return;
            string? type = typeProp.GetString();

            switch (type)
            {
                case "ready":
                    OnReady?.Invoke();
                    break;
                case "hit":
                    List<string> areas = new();
                    if (root.TryGetProperty("areas", out JsonElement areasProp))
                    {
                        foreach (JsonElement area in areasProp.EnumerateArray())
                        {
                            areas.Add(area.GetString() ?? "");
                        }
                    }
                    OnHit?.Invoke(areas);
                    break;
                case "chat":
                    OnChat?.Invoke(root.GetProperty("text").GetString() ?? "");
                    break;
                case "drag-request":
                    OnDragRequest?.Invoke();
                    break;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"PetBridge Message Error: {ex.Message}");
        }
    }

    readonly WebView2 webView;
}
