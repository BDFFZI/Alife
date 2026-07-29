using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Alife.Platform;

/// <summary>
/// 全局鼠标追踪器：监听系统级鼠标移动事件。
/// </summary>
public class MouseTracker
{
    public event Action<int, int>? MouseMoved;

    public void Start()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) == false) return;

        proc = MouseProc;
        using (Process curProcess = Process.GetCurrentProcess())
        using (ProcessModule curModule = curProcess.MainModule!)
        {
            hookId = SetWindowsHookEx(WhMouseLl, proc, GetModuleHandle(curModule.ModuleName), 0);
        }

        if (hookId == IntPtr.Zero)
            throw new Exception("[MouseTracker] 无法设置全局鼠标钩子");
    }

    public void Stop()
    {
        if (hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(hookId);
            hookId = IntPtr.Zero;
        }
    }

    IntPtr hookId = IntPtr.Zero;
    LowLevelMouseProc? proc;

    IntPtr MouseProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (int)wParam == WmMousemove)
        {
            Msllhookstruct hookStruct = Marshal.PtrToStructure<Msllhookstruct>(lParam);
            MouseMoved?.Invoke(hookStruct.pt.x, hookStruct.pt.y);
        }
        return CallNextHookEx(hookId, nCode, wParam, lParam);
    }

    const int WhMouseLl = 14;
    const int WmMousemove = 0x0200;

    public delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct Point
    {
        public int x;
        public int y;
    }
    
    [StructLayout(LayoutKind.Sequential)]
    public struct Msllhookstruct
    {
        public Point pt;
        public int mouseData;
        public int flags;
        public int time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);
}
