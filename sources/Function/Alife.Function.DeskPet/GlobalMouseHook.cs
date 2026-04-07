using Alife.Basic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Alife.Function.DeskPet;

public class GlobalMouseHook
{
    public event Action<int, int>? MouseMove;
    public event Action<int, int>? MouseClick;

    public void Start()
    {
        proc = MouseProc;

        using (Process curProcess = Process.GetCurrentProcess())
        using (ProcessModule curModule = curProcess.MainModule!)
        {
            hookID = SetWindowsHookEx(WH_MOUSE_LL, proc, GetModuleHandle(curModule.ModuleName), 0);
        }

        if (hookID == IntPtr.Zero)
            throw new Exception("无法设置全局鼠标钩子");
    }

    public void Stop()
    {
        if (hookID != IntPtr.Zero)
        {
            UnhookWindowsHookEx(hookID);
            hookID = IntPtr.Zero;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct POINT
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public int mouseData;
        public int flags;
        public int time;
        public IntPtr dwExtraInfo;
    }

    delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    const int WH_MOUSE_LL = 14;
    const int WM_MOUSEMOVE = 0x0200;
    const int WM_LBUTTONDOWN = 0x0201;

    IntPtr hookID = IntPtr.Zero;
    LowLevelMouseProc? proc;

    IntPtr MouseProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            MSLLHOOKSTRUCT hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            int x = hookStruct.pt.x;
            int y = hookStruct.pt.y;

            switch ((int)wParam)
            {
                case WM_MOUSEMOVE:
                    MouseMove?.Invoke(x, y);
                    break;
                case WM_LBUTTONDOWN:
                    MouseClick?.Invoke(x, y);
                    break;
            }
        }

        return CallNextHookEx(hookID, nCode, wParam, lParam);
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    static extern IntPtr GetModuleHandle(string lpModuleName);
}
