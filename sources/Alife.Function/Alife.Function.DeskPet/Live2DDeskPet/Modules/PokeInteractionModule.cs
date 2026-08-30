using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Alife.Function.DeskPet;

public class PokeInteractionModule : IPetModule, IDisposable
{
    public string JsCode => @"
window.addEventListener('dblclick', async function(e) {
    if (e.target.tagName !== 'CANVAS') return;
    var areas = await model.hitTest(e.clientX, e.clientY);
    if (areas.length > 0) postMessage({type:'poke', areas:areas});
});
";

    readonly PetBridge bridge;
    readonly PetModelMetadata metadata;
    readonly SubtitleModule subtitleModule;
    readonly ExpressionModule expressionModule;
    readonly InteractedEventCallback onInteracted;
    int comboCount;
    long lastHitTime;

    public PokeInteractionModule(PetBridge bridge, PetModelMetadata metadata, SubtitleModule subtitleModule, ExpressionModule expressionModule,
        InteractedEventCallback onInteracted)
    {
        this.bridge = bridge;
        this.metadata = metadata;
        this.subtitleModule = subtitleModule;
        this.expressionModule = expressionModule;
        this.onInteracted = onInteracted;
        bridge.OnMessage += OnBridgeMessage;
    }
    public void Dispose()
    {
        bridge.OnMessage -= OnBridgeMessage;
    }

    void OnBridgeMessage(string type, JsonElement data)
    {
        if (type == "poke") HandlePoke(data);
    }

    void HandlePoke(JsonElement data)
    {
        List<string> areas = new();
        if (data.TryGetProperty("areas", out JsonElement areasProp))
        {
            foreach (JsonElement area in areasProp.EnumerateArray())
                areas.Add(area.GetString() ?? "");
        }

        string? category = null;
        if (areas.Any(a => a.Contains("Head", StringComparison.OrdinalIgnoreCase))) category = "head";
        else if (areas.Any(a => a.Contains("Body", StringComparison.OrdinalIgnoreCase))) category = "body";
        if (category == null) return;

        long now = DateTimeOffset.Now.ToUnixTimeMilliseconds();
        if (now - lastHitTime < 2500) comboCount++;
        else comboCount = 1;
        lastHitTime = now;

        if (comboCount != 0 && comboCount % 3 == 0)
        {
            onInteracted($"桌宠被连续触摸：{category}");
            HandleInteraction("mouse_combo");
            return;
        }

        HandleInteraction(category);
    }

    void HandleInteraction(string type)
    {
        if (metadata.Interactions.TryGetValue(type, out List<InteractionItem>? pool) == false || pool.Count == 0)
            return;

        InteractionItem item = pool[Random.Shared.Next(pool.Count)];
        if (string.IsNullOrEmpty(item.Text) == false)
            subtitleModule.Show(item.Text);
        if (string.IsNullOrEmpty(item.Exp) == false)
            expressionModule.PlayExpression(item.Exp);
        if (item.Mtn != null)
            expressionModule.PlayMotion(item.Mtn.Group, item.Mtn.Index);
    }
}