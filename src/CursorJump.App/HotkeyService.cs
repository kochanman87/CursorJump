using System;
using System.ComponentModel;

namespace CursorJump.App;

/// <summary>
/// グローバルホットキーの登録・解除を管理するサービス。
/// WM_HOTKEY メッセージのルーティングは呼び出し元（MainWindow）が担う。
/// </summary>
internal sealed class HotkeyService : IDisposable
{
    // アプリケーション固有のホットキー ID（0x0000〜0xBFFF の範囲）
    private const int HotkeyId = 0x3001;

    // デフォルトホットキー: Ctrl+Alt+Home
    private const int Modifiers = NativeMethods.MOD_CONTROL
                                | NativeMethods.MOD_ALT
                                | NativeMethods.MOD_NOREPEAT;
    private const int VirtualKey = NativeMethods.VK_HOME;

    private readonly IntPtr _hwnd;
    private bool _registered;
    private bool _disposed;

    /// <summary>ホットキーが押されたときに発火するイベント。</summary>
    public event EventHandler? HotkeyPressed;

    /// <summary>トレイアイコン等で表示するホットキーの説明文。</summary>
    public string HotkeyDescription => "Ctrl+Alt+Home";

    public HotkeyService(IntPtr hwnd)
    {
        _hwnd = hwnd;
    }

    /// <summary>
    /// ホットキーを Windows に登録する。失敗時は Win32Exception をスローする。
    /// </summary>
    public void Register()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(HotkeyService));
        if (_registered) return;

        bool ok = NativeMethods.RegisterHotKey(_hwnd, HotkeyId, Modifiers, VirtualKey);
        if (!ok)
            throw new Win32Exception();

        _registered = true;
    }

    /// <summary>
    /// HwndSource のメッセージフックから呼び出す。
    /// WM_HOTKEY を受信したら HotkeyPressed を発火し handled=true をセットする。
    /// </summary>
    public IntPtr HandleWindowMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            HotkeyPressed?.Invoke(this, EventArgs.Empty);
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_registered)
        {
            NativeMethods.UnregisterHotKey(_hwnd, HotkeyId);
            _registered = false;
        }
    }
}
