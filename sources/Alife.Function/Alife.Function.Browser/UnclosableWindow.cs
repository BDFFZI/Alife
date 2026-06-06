using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Alife.Function.Browser;

public class UnclosableWindow : Window
{
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        DisableCloseButton();
    }

    void DisableCloseButton()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        var hMenu = GetSystemMenu(hwnd, false);
        if (hMenu != IntPtr.Zero)
            RemoveMenu(hMenu, SC_CLOSE, MF_BYCOMMAND);
    }

    [DllImport("user32.dll")]
    static extern IntPtr GetSystemMenu(IntPtr hWnd, bool bRevert);
    [DllImport("user32.dll")]
    static extern bool RemoveMenu(IntPtr hMenu, uint uPosition, uint uFlags);

    const uint SC_CLOSE = 0xF060;
    const uint MF_BYCOMMAND = 0x00000000;
}
