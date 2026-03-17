using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

/// <summary>
/// 全局鼠标钩子实现
/// </summary>
public class GlobalMouseHook
{
    private const int WH_MOUSE_LL = 14;
    private const int WM_MOUSEMOVE = 0x0200;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_RBUTTONDOWN = 0x0204;
    private const int WM_RBUTTONUP = 0x0205;

    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public int mouseData;
        public int flags;
        public int time;
        public IntPtr dwExtraInfo;
    }

    private IntPtr hookID = IntPtr.Zero;
    private LowLevelMouseProc proc;

    /// <summary>
    /// 鼠标移动事件
    /// </summary>
    public event Action<int, int> OnMouseMove;

    /// <summary>
    /// 鼠标点击事件
    /// </summary>
    public event Action<int, int> OnMouseClick;

    /// <summary>
    /// 启动鼠标钩子
    /// </summary>
    public void Start()
    {
        proc = MouseProc;

        using (var curProcess = Process.GetCurrentProcess())
        using (var curModule = curProcess.MainModule)
        {
            hookID = SetWindowsHookEx(WH_MOUSE_LL, proc, GetModuleHandle(curModule.ModuleName), 0);
        }

        if (hookID == IntPtr.Zero)
        {
            throw new Exception("无法设置全局鼠标钩子");
        }
    }

    /// <summary>
    /// 停止鼠标钩子
    /// </summary>
    public void Stop()
    {
        if (hookID != IntPtr.Zero)
        {
            UnhookWindowsHookEx(hookID);
            hookID = IntPtr.Zero;
        }
    }

    private IntPtr MouseProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            MSLLHOOKSTRUCT hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            int x = hookStruct.pt.x;
            int y = hookStruct.pt.y;

            switch ((int)wParam)
            {
                case WM_MOUSEMOVE:
                    OnMouseMove?.Invoke(x, y);
                    break;
                case WM_LBUTTONDOWN:
                    OnMouseClick?.Invoke(x, y);
                    break;
            }
        }

        return CallNextHookEx(hookID, nCode, wParam, lParam);
    }
}