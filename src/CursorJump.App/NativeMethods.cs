using System;
using System.Runtime.InteropServices;

namespace CursorJump.App;

internal static class NativeMethods
{
    // ── Virtual key codes ──
    internal const int VK_LCONTROL = 0xA2;
    internal const int VK_RCONTROL = 0xA3;
    internal const int VK_MENU     = 0x12; // Alt キー（汎用: Left/Right 区別なし。Win+Alt 時の fallback 用）
    internal const int VK_LMENU   = 0xA4; // Left Alt
    internal const int VK_RMENU   = 0xA5; // Right Alt
    internal const int VK_LSHIFT  = 0xA0;
    internal const int VK_RSHIFT  = 0xA1;
    internal const int VK_LWIN    = 0x5B;
    internal const int VK_RWIN    = 0x5C;
    internal const int VK_ESCAPE  = 0x1B;
    internal const int VK_MBUTTON = 0x04;

    // F13-F24（VIAキーボードマクロ等で使用される拡張ファンクションキー）
    internal const int VK_F13 = 0x7C;
    internal const int VK_F14 = 0x7D;
    internal const int VK_F15 = 0x7E;
    internal const int VK_F16 = 0x7F;
    internal const int VK_F17 = 0x80;
    internal const int VK_F18 = 0x81;
    internal const int VK_F19 = 0x82;
    internal const int VK_F20 = 0x83;
    internal const int VK_F21 = 0x84;
    internal const int VK_F22 = 0x85;
    internal const int VK_F23 = 0x86;
    internal const int VK_F24 = 0x87;

    // ── Mouse messages ──
    internal const int WM_MOUSEMOVE   = 0x0200;
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

    // ── Keyboard messages ──
    internal const int WM_KEYDOWN    = 0x0100;
    internal const int WM_KEYUP      = 0x0101;
    internal const int WM_SYSKEYDOWN = 0x0104;
    internal const int WM_SYSKEYUP   = 0x0105;

    // ── Low-level hooks ──
    internal const int WH_KEYBOARD_LL = 13;
    internal const int WH_MOUSE_LL    = 14;

    internal delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);
    internal delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

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

    // ── Cursor APIs ──
    [DllImport("user32.dll")]
    internal static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    internal static extern bool GetCursorPos(out POINT lpPoint);

    // ── Foreground window activation ──
    [DllImport("user32.dll")]
    internal static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll")]
    internal static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    internal static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    // ── SendInput（合成マウス入力）──
    internal const uint INPUT_MOUSE = 0;
    internal const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
    internal const uint MOUSEEVENTF_MIDDLEUP   = 0x0040;
    internal const uint MOUSEEVENTF_MOVE        = 0x0001;
    internal const uint MOUSEEVENTF_ABSOLUTE    = 0x8000;
    internal const uint MOUSEEVENTF_VIRTUALDESK = 0x4000;

    // 仮想スクリーン関連の SystemMetrics
    internal const int SM_XVIRTUALSCREEN  = 76;
    internal const int SM_YVIRTUALSCREEN  = 77;
    internal const int SM_CXVIRTUALSCREEN = 78;
    internal const int SM_CYVIRTUALSCREEN = 79;

    [DllImport("user32.dll")]
    internal static extern int GetSystemMetrics(int nIndex);

    // Per-Monitor DPI Awareness Context (Win10 1607+)
    // 値は内部ハンドル相当 (IntPtr)。負の小整数を Magic 値として OS が解釈する。
    internal static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = new IntPtr(-4);

    [DllImport("user32.dll")]
    internal static extern IntPtr SetThreadDpiAwarenessContext(IntPtr dpiContext);

    // MSLLHOOKSTRUCT.flags: 合成入力の判定に使用
    internal const uint LLMHF_INJECTED = 0x00000001;

    [StructLayout(LayoutKind.Sequential)]
    internal struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct INPUT
    {
        public uint type;
        public INPUTUNION u;
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct INPUTUNION
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
    }

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
}
