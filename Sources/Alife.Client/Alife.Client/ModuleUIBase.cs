using System;
using Microsoft.AspNetCore.Components;

namespace Alife.Framework;

/// <summary>
/// 模块 UI 基类（带配置）。
/// TModule: 模块类型，TConfig: 配置类型。
/// ModuleType 由泛型自动推导，ConfigSaveUI 由框架层常驻渲染，子类无需关心。
/// </summary>
public abstract class ModuleUIBase<TModule, TConfig> : ComponentBase
    where TConfig : class, new()
{
    public Type ModuleType => typeof(TModule);

    [Parameter] public Character? Character { get; set; }
    [Parameter] public ChatActivity? ChatActivity { get; set; }
    [Parameter] public TModule? Module { get; set; }
    [Parameter] public TConfig Configuration { get; set; } = new();
    [Parameter] public RenderFragment DefaultUI { get; set; } = _ => {};
}

/// <summary>
/// 模块 UI 基类（无配置）。
/// </summary>
public abstract class ModuleUIBase<TModule> : ModuleUIBase<TModule, object>
    where TModule : ChatBehaviour {}
