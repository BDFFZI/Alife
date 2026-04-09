using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Alife.Function.DeskPet;

/// <summary>
/// 整合了原生 Hook 逻辑的鼠标追踪器
/// </summary>
public class MouseTracker
{
    public event Action<int, int>? MouseMoved;

    public void Start()
    {
        proc = MouseProc;
        using (Process curProcess = Process.GetCurrentProcess())
        using (ProcessModule curModule = curProcess.MainModule!)
        {
            hookID = SetWindowsHookEx(whMouseLl, proc, GetModuleHandle(curModule.ModuleName), 0);
        }

        if (hookID == IntPtr.Zero)
            throw new Exception("[MouseTracker] 无法设置全局鼠标钩子");
    }

    public void Stop()
    {
        if (hookID != IntPtr.Zero)
        {
            UnhookWindowsHookEx(hookID);
            hookID = IntPtr.Zero;
        }
    }

    IntPtr hookID = IntPtr.Zero;
    LowLevelMouseProc? proc;

    IntPtr MouseProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (int)wParam == wmMousemove)
        {
            Msllhookstruct hookStruct = Marshal.PtrToStructure<Msllhookstruct>(lParam);
            MouseMoved?.Invoke(hookStruct.pt.x, hookStruct.pt.y);
        }
        return CallNextHookEx(hookID, nCode, wParam, lParam);
    }

    #region Win32 API

    [StructLayout(LayoutKind.Sequential)]
    struct Point { public int x; public int y; }

    [StructLayout(LayoutKind.Sequential)]
    struct Msllhookstruct { public Point pt; public int mouseData; public int flags; public int time; public IntPtr dwExtraInfo; }

    delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    const int whMouseLl = 14;
    const int wmMousemove = 0x0200;

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    static extern IntPtr GetModuleHandle(string lpModuleName);

    #endregion

}
