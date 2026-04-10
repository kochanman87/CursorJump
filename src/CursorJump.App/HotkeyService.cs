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

    private readonly IntPtr _hwnd;
    private readonly SettingsService _settingsService;
    private bool _registered;
    private bool _disposed;

    /// <summary>ホットキーが押されたときに発火するイベント。</summary>
    public event EventHandler? HotkeyPressed;

    /// <summary>トレイアイコン等で表示するホットキーの説明文。</summary>
    public string HotkeyDescription => BuildDescription();

    public HotkeyService(IntPtr hwnd, SettingsService settingsService)
    {
        _hwnd = hwnd;
        _settingsService = settingsService;
    }

    /// <summary>
    /// ホットキーを Windows に登録する。失敗時は Win32Exception をスローする。
    /// </summary>
    public void Register()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(HotkeyService));
        if (_registered) return;

        var settings = _settingsService.Current;
        int modifiers = settings.CenterJumpModifiers | NativeMethods.MOD_NOREPEAT;
        int vk = settings.CenterJumpKey;

        bool ok = NativeMethods.RegisterHotKey(_hwnd, HotkeyId, modifiers, vk);
        if (!ok)
            throw new Win32Exception();

        _registered = true;
    }

    public void Reregister()
    {
        if (_registered)
        {
            NativeMethods.UnregisterHotKey(_hwnd, HotkeyId);
            _registered = false;
        }
        Register();
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

    private string BuildDescription()
    {
        var settings = _settingsService.Current;
        var parts = new System.Collections.Generic.List<string>();

        int mod = settings.CenterJumpModifiers;
        if ((mod & NativeMethods.MOD_CONTROL) != 0) parts.Add("Ctrl");
        if ((mod & NativeMethods.MOD_ALT) != 0) parts.Add("Alt");
        if ((mod & NativeMethods.MOD_SHIFT) != 0) parts.Add("Shift");
        if ((mod & NativeMethods.MOD_WIN) != 0) parts.Add("Win");

        string keyName = settings.CenterJumpKey switch
        {
            NativeMethods.VK_HOME => "Home",
            _ => $"0x{settings.CenterJumpKey:X2}"
        };
        parts.Add(keyName);

        return string.Join("+", parts);
    }
}
