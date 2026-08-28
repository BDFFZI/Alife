using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ElectronNET.API;

namespace Alife.Function.GameCompanion.Editor;

/// <summary>
/// 陪玩编辑器的全局 IPC 桥。
/// 插件支持热重载，每次重载都载入新的 AssemblyLoadContext（静态状态不复存在）。
/// 为兼容该场景，本桥采用「每次注册都先摘除旧处理器再重新挂载」的幂等策略：
/// 保证同一频道在主进程中始终只有一个有效处理器，并将请求转发给「最近打开/使用」的控制器。
/// </summary>
public static class GameCompanionIpcBridge
{
    static readonly string[] Channels =
    {
"companion:get-config",
        "companion:get-screen-size",
        "companion:collector-types",
        "companion:save-config",
        "companion:show-overlay",
        "companion:hide-overlay",
        "companion:region-changed",
        "companion:pick-color",
        "companion:pick-at",
        "companion:pick-cancel",
        "companion:pick-result",
        "companion:open-config-folder",
        "companion:open-test-scene",
        "companion:get-test-scene-bounds",
        "companion:float-collapse",
        "companion:float-expand",
        "companion:float-edit",
        "companion:float-move",
        "companion:float-set-pos",
        "companion:float-get-pos",
        "companion:float-pause",
        "companion:float-games",
        "companion:float-start",
        "companion:float-stop",
        "companion:float-toggle",
"companion:float-overlay-view"
    };

    static readonly List<GameCompanionEditorController> Controllers = new();

    /// <summary>
    /// 注册控制器并重建全局处理器。幂等：先移除旧处理器再重新挂载，规避热重载导致的重复注册。
    /// </summary>
    public static void Register(GameCompanionEditorController controller)
    {
        lock (Controllers)
        {
            Controllers.Remove(controller);
            Controllers.Add(controller); // 最近打开/使用 → 列表末尾

            foreach (string channel in Channels)
            {
                try { Electron.IpcMain.RemoveHandler(channel); } catch { }
            }
            Electron.IpcMain.Handle("companion:get-config", _ => Dispatch(c => Task.FromResult<object>(c.SafeReadConfig())));
            Electron.IpcMain.Handle("companion:get-screen-size", _ => Dispatch(c => c.GetScreenSizeAsync()));
            Electron.IpcMain.Handle("companion:collector-types", _ => Dispatch(c => c.CollectorTypesAsync()));
            Electron.IpcMain.Handle("companion:save-config", payload => Dispatch(c => c.SaveConfigAsync(payload)));
            try { Electron.IpcMain.RemoveAllListeners("companion:save-config-sync"); } catch { }
            Electron.IpcMain.OnSync("companion:save-config-sync", payload => DispatchSync(c => c.SaveConfig(payload)));
            Electron.IpcMain.Handle("companion:show-overlay", payload => Dispatch(c => { _ = c.ShowOverlayAsync(payload); return Task.FromResult<object>(true); }));
            Electron.IpcMain.Handle("companion:hide-overlay", _ => Dispatch(c => { c.HideOverlay(); return Task.FromResult<object>(true); }));
            Electron.IpcMain.Handle("companion:region-changed", payload => Dispatch(c => { c.ForwardRegionChange(payload); return Task.FromResult<object>(true); }));
            Electron.IpcMain.Handle("companion:pick-color", _ => Dispatch(c => c.PickColorAsync()));
            Electron.IpcMain.Handle("companion:pick-at", payload => Dispatch(c => c.PickAtAsync(payload)));
            Electron.IpcMain.Handle("companion:pick-cancel", _ => Dispatch(c => c.PickCancelAsync()));
            Electron.IpcMain.Handle("companion:pick-result", payload => Dispatch(c => c.PickResultAsync(payload)));
            Electron.IpcMain.Handle("companion:open-config-folder", _ => Dispatch(c => { c.OpenConfigFolder(); return Task.FromResult<object>(true); }));
            Electron.IpcMain.Handle("companion:open-test-scene", _ => Dispatch(c => { c.OpenTestScene(); return Task.FromResult<object>(true); }));
            Electron.IpcMain.Handle("companion:get-test-scene-bounds", _ => Dispatch(c => c.GetTestSceneBoundsAsync()));
            Electron.IpcMain.Handle("companion:float-collapse", _ => Dispatch(c => c.FloatCollapseAsync()));
            Electron.IpcMain.Handle("companion:float-expand", _ => Dispatch(c => c.FloatExpandAsync()));
            Electron.IpcMain.Handle("companion:float-edit", payload => Dispatch(c => c.FloatEditAsync(payload)));
            Electron.IpcMain.Handle("companion:float-move", payload => Dispatch(c => c.FloatMoveAsync(payload)));
            Electron.IpcMain.Handle("companion:float-set-pos", payload => Dispatch(c => c.FloatMoveToAsync(payload)));
            Electron.IpcMain.Handle("companion:float-get-pos", _ => Dispatch(c => c.FloatGetPosAsync()));
            Electron.IpcMain.Handle("companion:float-pause", _ => Dispatch(c => c.FloatPauseToggleAsync()));
            Electron.IpcMain.Handle("companion:float-games", _ => Dispatch(c => c.FloatGamesAsync()));
            Electron.IpcMain.Handle("companion:float-start", payload => Dispatch(c => c.FloatStartAsync(payload)));
            Electron.IpcMain.Handle("companion:float-stop", _ => Dispatch(c => c.FloatStopAsync()));
            Electron.IpcMain.Handle("companion:float-toggle", payload => Dispatch(c => c.FloatToggleAsync(payload)));
            Electron.IpcMain.Handle("companion:float-overlay-view", payload => Dispatch(c => c.FloatOverlayViewAsync(payload)));
            // 浮窗调试日志转发到主进程控制台
            try { Electron.IpcMain.RemoveAllListeners("companion:debug"); } catch { }
            Electron.IpcMain.On("companion:debug", msg => Console.WriteLine($"[Float] {msg}"));
        }
    }

    public static void Unregister(GameCompanionEditorController controller)
    {
        lock (Controllers)
        {
            Controllers.Remove(controller);
            // 处理器保留，供其它角色的控制器继续复用；下次 Register 会幂等重建
        }
    }

    static GameCompanionEditorController? Active()
    {
        lock (Controllers)
        {
            return Controllers.Count == 0 ? null : Controllers[^1];
        }
    }

    static Task<object> Dispatch(Func<GameCompanionEditorController, Task<object>> action)
    {
        GameCompanionEditorController? controller = Active();
        return controller == null
            ? Task.FromResult<object>(false)
            : action(controller);
    }

    static object DispatchSync(Func<GameCompanionEditorController, object> action)
    {
        GameCompanionEditorController? controller = Active();
        return controller == null ? false : action(controller);
    }
}