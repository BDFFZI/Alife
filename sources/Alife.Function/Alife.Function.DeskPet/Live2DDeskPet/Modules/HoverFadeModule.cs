using System;
using System.Text.Json;
using Alife.Foundation;

namespace Alife.Function.DeskPet;

/// <summary>
/// 悬停淡出模块：鼠标穿透（锁定）开启时，若光标移动到角色身上，则把角色淡为半透明，
/// 便于看穿角色遮挡的画面；光标移开角色后立即恢复不透明。
/// 本模块只消费 <c>pointer_hit</c> 上报里的 <c>overModel</c> 标志并转发淡出，
/// 不做任何命中判定、轮询或脚本执行；命中规则见 <see cref="PetWindow"/> 与 pet.js 的 <c>isOverModelAt</c>。
/// 判定复用 Live2D 命中区域（与双击互动同一套 <c>HitAreas</c>）：
/// 模型未在 model3.json 中声明 HitAreas 时该功能不会生效。
/// </summary>
public class HoverFadeModule : IPetModule
{
    public string CssCode => @"
#canvas { transition:opacity 0.18s ease; }
#canvas.hover-faded { opacity:0.4; }
";
    public string JsCode => @"
(function() {
    var canvas = document.getElementById('canvas');
    messageBus.on('hover_fade', function(msg) {
        canvas.classList.toggle('hover-faded', !!msg.on);
    });
})();
";

    readonly PetBridge bridge;
    readonly MouseThroughModule mouseThroughModule;
    bool isFaded;

    public HoverFadeModule(PetBridge bridge, MouseThroughModule mouseThroughModule)
    {
        this.bridge = bridge;
        this.mouseThroughModule = mouseThroughModule;
        bridge.OnMessage += OnBridgeMessage;
    }

    void OnBridgeMessage(string type, JsonElement data)
    {
        if (type != "pointer_hit")
            return;
        if (data.TryGetProperty("overModel", out JsonElement overModelProp) == false)
            return;

        //直接沿用渲染进程内判定好的 overModel，不做二次命中测试：
        //它只表示光标是否落在角色身上（交互 UI 上已被排除），与「是否需要恢复鼠标响应」互不相关。
        SetFaded(mouseThroughModule.IsMouseThrough && overModelProp.GetBoolean());
    }

    void SetFaded(bool faded)
    {
        if (isFaded == faded)
            return;
        isFaded = faded;
        bridge.SendMessage("hover_fade", new { on = faded });
    }
}
