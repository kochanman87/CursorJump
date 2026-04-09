using System;
using System.Runtime.InteropServices;

namespace CursorJump.App;

internal static class NativeMethods
{
    // ── Hotkey modifiers ──
    internal const int MOD_ALT      = 0x0001;
    internal const int MOD_CONTROL  = 0x0002;
    internal const int MOD_SHIFT    = 0x0004;
    internal const int MOD_WIN      = 0x0008;
    internal const int MOD_NOREPEAT = 0x4000;

    internal const int WM_HOTKEY = 0x0312;

    internal const int VK_HOME = 0x24;

    // ── Virtual key codes ──
    internal const int VK_LCONTROL = 0xA2;
    internal const int VK_RCONTROL = 0xA3;
    internal const int VK_LMENU   = 0xA4; // Left Alt
    internal const int VK_RMENU   = 0xA5; // Right Alt
    internal const int VK_LSHIFT  = 0xA0;
    internal const int VK_RSHIFT  = 0xA1;
    internal const int VK_LWIN    = 0x5B;
    internal const int VK_RWIN    = 0x5C;
    internal const int VK_ESCAPE  = 0x1B;

    // ── Mouse messages ──
    internal const int WM_LBUTTONDOWN = 0x0201;
    internal const int WM_LBUTTONUP   = 0x0202;
    internal const int WM_RBUTTONDOWN = 0x0204;
    internal const int WM_RBUTTONUP   = 0x0205;
    internal const int WM_MBUTTONDOWN = 0x0207;
    internal const int WM_MBUTTONUP   = 0x0208;
    internal const int WM_XBUTTONDOWN = 0x020B;
    internal const int WM_XBUTTONUP   = 0x020C;

    // WM_XBUTTONDOWN/UP の mouseData 上位ワードに格納されるボタンID
    internal const int XBUTTON1 = 0x0001;
    internal const int XBUTTON2 = 0x0002;

    // ── Low-level mouse hook ──
    internal const int WH_MOUSE_LL = 14;

    internal delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    internal static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    internal static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    internal static extern short GetAsyncKeyState(int vKey);

    // ── Window style manipulation (for overlay click-through) ──
    internal const int GWL_EXSTYLE = -20;
    internal const int WS_EX_TRANSPARENT = 0x00000020;
    internal const int WS_EX_TOOLWINDOW  = 0x00000080;

    [DllImport("user32.dll")]
    internal static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    internal static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    // ── Existing APIs ──
    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    internal static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    internal static extern bool GetCursorPos(out POINT lpPoint);
}
