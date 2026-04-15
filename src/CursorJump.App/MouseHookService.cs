using System;
using System.Runtime.InteropServices;
using CursorJump.App.Models;

namespace CursorJump.App;

internal sealed class MouseHookEventArgs : EventArgs
{
    public int X { get; }
    public int Y { get; }

    public MouseHookEventArgs(int x, int y)
    {
        X = x;
        Y = y;
    }
}

internal sealed class MouseHookService : IDisposable
{
    private IntPtr _hookHandle;
    private readonly NativeMethods.LowLevelMouseProc _hookProc;
    private bool _disposed;
    private bool _suspended;

    // 削除モード関連
    private bool _deleteMode;
    private IntPtr _keyboardHookHandle;
    private readonly NativeMethods.LowLevelKeyboardProc _keyboardHookProc;

    // DOWNイベントを消費した後、対応するUPイベントも消費するためのフラグ
    private bool _swallowNextLeftUp;
    private bool _swallowNextRightUp;
    private bool _swallowNextMiddleUp;
    private bool _swallowNextXButton1Up;
    private bool _swallowNextXButton2Up;

    private readonly SettingsService _settingsService;

    public event EventHandler<MouseHookEventArgs>? SaveRequested;
    public event EventHandler<MouseHookEventArgs>? NavigateRequested;
    public event EventHandler<MouseHookEventArgs>? NavigateCurrentMonitorRequested;
    public event EventHandler<MouseHookEventArgs>? DisplayDeleteRequested;

    // 削除モード用イベント
    public event EventHandler<MouseHookEventArgs>? DeleteModeClicked;
    public event EventHandler<MouseHookEventArgs>? DeleteModeMoved;
    public event EventHandler? DeleteModeEscPressed;

    public MouseHookService(SettingsService settingsService)
    {
        _settingsService = settingsService;
        _hookProc = HookCallback;
        _keyboardHookProc = KeyboardHookCallback;
    }

    public void Install()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MouseHookService));
        if (_hookHandle != IntPtr.Zero) return;

        IntPtr moduleHandle = NativeMethods.GetModuleHandle(null);
        _hookHandle = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_MOUSE_LL,
            _hookProc,
            moduleHandle,
            0);

        if (_hookHandle == IntPtr.Zero)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
    }

    public void Suspend()
    {
        DebugLog.Write("MouseHookService: Suspend()");
        _suspended = true;
        // Suspend中にUPイベントが来てもフラグがクリアされないため、ここで全クリア
        _swallowNextLeftUp = false;
        _swallowNextRightUp = false;
        _swallowNextMiddleUp = false;
        _swallowNextXButton1Up = false;
        _swallowNextXButton2Up = false;
    }

    public void Resume()
    {
        DebugLog.Write("MouseHookService: Resume()");
        _suspended = false;
    }

    /// <summary>
    /// 削除モードに入る。通常のジェスチャーマッチングを無効化し、
    /// 左クリック・マウス移動・ESCキーを専用イベントとして発火する。
    /// </summary>
    public void EnterDeleteMode()
    {
        DebugLog.Write("MouseHookService: EnterDeleteMode()");
        _deleteMode = true;
        _swallowNextLeftUp = false;
        _swallowNextRightUp = false;

        // ESC検出用キーボードフックをインストール
        IntPtr moduleHandle = NativeMethods.GetModuleHandle(null);
        _keyboardHookHandle = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_KEYBOARD_LL,
            _keyboardHookProc,
            moduleHandle,
            0);
        DebugLog.Write($"KeyboardHook installed: handle={_keyboardHookHandle}");
    }

    /// <summary>
    /// 削除モードを終了し、通常モードに戻る。
    /// </summary>
    public void ExitDeleteMode()
    {
        DebugLog.Write("MouseHookService: ExitDeleteMode()");
        _deleteMode = false;
        _swallowNextLeftUp = false;
        _swallowNextRightUp = false;

        // キーボードフックをアンインストール
        if (_keyboardHookHandle != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_keyboardHookHandle);
            _keyboardHookHandle = IntPtr.Zero;
            DebugLog.Write("KeyboardHook uninstalled");
        }
    }

    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && _deleteMode)
        {
            int msg = wParam.ToInt32();
            if (msg == NativeMethods.WM_KEYDOWN || msg == NativeMethods.WM_SYSKEYDOWN)
            {
                int vkCode = Marshal.ReadInt32(lParam);
                if (vkCode == NativeMethods.VK_ESCAPE)
                {
                    DebugLog.Write("KeyboardHook: ESC detected in delete mode");
                    DeleteModeEscPressed?.Invoke(this, EventArgs.Empty);
                    return (IntPtr)1; // ESCを消費
                }
            }
        }
        return NativeMethods.CallNextHookEx(_keyboardHookHandle, nCode, wParam, lParam);
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && _deleteMode)
        {
            int msg = wParam.ToInt32();
            var hookStruct = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);

            // 削除モード: マウス移動 → ハイライト用イベント（消費しない）
            if (msg == NativeMethods.WM_MOUSEMOVE)
            {
                DeleteModeMoved?.Invoke(this, new MouseHookEventArgs(hookStruct.pt.X, hookStruct.pt.Y));
                return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
            }

            // 削除モード: 左クリック → 削除イベント（消費する）
            if (msg == NativeMethods.WM_LBUTTONDOWN)
            {
                DebugLog.Write($"DeleteMode: LeftClick at ({hookStruct.pt.X},{hookStruct.pt.Y})");
                DeleteModeClicked?.Invoke(this, new MouseHookEventArgs(hookStruct.pt.X, hookStruct.pt.Y));
                _swallowNextLeftUp = true;
                return (IntPtr)1;
            }

            // 削除モード: 左ボタンUP消費
            if (msg == NativeMethods.WM_LBUTTONUP && _swallowNextLeftUp)
            {
                _swallowNextLeftUp = false;
                return (IntPtr)1;
            }

            // 座標表示モード: 右クリック → 終了（ESCと同じ）
            if (msg == NativeMethods.WM_RBUTTONDOWN)
            {
                DebugLog.Write("DeleteMode: RightClick - exiting");
                DeleteModeEscPressed?.Invoke(this, EventArgs.Empty);
                _swallowNextRightUp = true;
                return (IntPtr)1;
            }
            if (msg == NativeMethods.WM_RBUTTONUP && _swallowNextRightUp)
            {
                _swallowNextRightUp = false;
                return (IntPtr)1;
            }

            // 削除モード: その他のイベントはパススルー
            return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        if (nCode >= 0 && !_suspended)
        {
            int msg = wParam.ToInt32();

            // UPイベントの消費チェック（DOWNを消費済みの場合）
            if (msg == NativeMethods.WM_LBUTTONUP && _swallowNextLeftUp)
            {
                _swallowNextLeftUp = false;
                return (IntPtr)1;
            }
            if (msg == NativeMethods.WM_RBUTTONUP && _swallowNextRightUp)
            {
                _swallowNextRightUp = false;
                return (IntPtr)1;
            }
            if (msg == NativeMethods.WM_MBUTTONUP && _swallowNextMiddleUp)
            {
                _swallowNextMiddleUp = false;
                return (IntPtr)1;
            }
            if (msg == NativeMethods.WM_XBUTTONUP)
            {
                var upStruct = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
                int upButtonId = (int)(upStruct.mouseData >> 16);
                if (upButtonId == NativeMethods.XBUTTON1 && _swallowNextXButton1Up)
                {
                    _swallowNextXButton1Up = false;
                    return (IntPtr)1;
                }
                if (upButtonId == NativeMethods.XBUTTON2 && _swallowNextXButton2Up)
                {
                    _swallowNextXButton2Up = false;
                    return (IntPtr)1;
                }
            }

            // DOWNイベントの処理（hookStructを先にデコードしてXButton判定でも再利用）
            var hookStruct = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
            MouseButtonType? pressedButton = msg switch
            {
                NativeMethods.WM_LBUTTONDOWN => MouseButtonType.Left,
                NativeMethods.WM_RBUTTONDOWN => MouseButtonType.Right,
                NativeMethods.WM_MBUTTONDOWN => MouseButtonType.Middle,
                NativeMethods.WM_XBUTTONDOWN => ResolveXButton(hookStruct.mouseData),
                _ => null
            };

            if (pressedButton is not null)
            {
                var settings = _settingsService.Current;
                var args = new MouseHookEventArgs(hookStruct.pt.X, hookStruct.pt.Y);

                // 各アクションのショートカットを個別にチェック
                if (IsShortcutMatch(pressedButton.Value, settings.SaveShortcut))
                {
                    DebugLog.Write($"HookCallback: SaveRequested matched (button={pressedButton.Value})");
                    SaveRequested?.Invoke(this, args);
                    SetSwallowUpFlag(pressedButton.Value);
                    return (IntPtr)1;
                }

                if (IsShortcutMatch(pressedButton.Value, settings.NavigateShortcut))
                {
                    DebugLog.Write($"HookCallback: NavigateRequested matched (button={pressedButton.Value})");
                    NavigateRequested?.Invoke(this, args);
                    SetSwallowUpFlag(pressedButton.Value);
                    return (IntPtr)1;
                }

                if (IsShortcutMatch(pressedButton.Value, settings.NavigateCurrentMonitorShortcut))
                {
                    DebugLog.Write($"HookCallback: NavigateCurrentMonitorRequested matched (button={pressedButton.Value})");
                    NavigateCurrentMonitorRequested?.Invoke(this, args);
                    SetSwallowUpFlag(pressedButton.Value);
                    return (IntPtr)1;
                }

                if (IsShortcutMatch(pressedButton.Value, settings.DisplayDeleteShortcut))
                {
                    DebugLog.Write($"HookCallback: DisplayDeleteRequested matched (button={pressedButton.Value})");
                    DisplayDeleteRequested?.Invoke(this, args);
                    SetSwallowUpFlag(pressedButton.Value);
                    return (IntPtr)1;
                }
            }
        }

        return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    private void SetSwallowUpFlag(MouseButtonType button)
    {
        switch (button)
        {
            case MouseButtonType.Left: _swallowNextLeftUp = true; break;
            case MouseButtonType.Right: _swallowNextRightUp = true; break;
            case MouseButtonType.Middle: _swallowNextMiddleUp = true; break;
            case MouseButtonType.XButton1: _swallowNextXButton1Up = true; break;
            case MouseButtonType.XButton2: _swallowNextXButton2Up = true; break;
        }
    }

    // XButton か否かを判定するヘルパー
    private static bool IsXButton(MouseButtonType button)
        => button is MouseButtonType.XButton1 or MouseButtonType.XButton2;

    // mouseData 上位ワードから XButton の種類を解決する
    private static MouseButtonType? ResolveXButton(uint mouseData)
    {
        int xButtonId = (int)(mouseData >> 16);
        return xButtonId switch
        {
            NativeMethods.XBUTTON1 => MouseButtonType.XButton1,
            NativeMethods.XBUTTON2 => MouseButtonType.XButton2,
            _ => null
        };
    }

    // ショートカットがマッチするか判定する
    // Left/Right/Middle は誤クリック防止のため修飾キー必須、XButton は修飾キー不要も可
    private static bool IsShortcutMatch(MouseButtonType pressedButton, Models.ActionShortcut shortcut)
    {
        if (!shortcut.EnabledTriggers.HasFlag(Models.TriggerType.Mouse))
            return false; // マウストリガー無効時は KeyboardHookService 側のみで処理
        if (pressedButton != shortcut.MouseButton)
            return false;
        if (shortcut.Modifiers == ModifierKeyFlags.None && !IsXButton(shortcut.MouseButton))
            return false;
        return AreModifiersHeld(shortcut.Modifiers);
    }

    private static bool AreModifiersHeld(ModifierKeyFlags required)
    {
        if (required == ModifierKeyFlags.None)
            return true; // 「修飾キー不要」を意味する（XButton用）

        if (required.HasFlag(ModifierKeyFlags.Control))
        {
            if (!IsKeyDown(NativeMethods.VK_LCONTROL) && !IsKeyDown(NativeMethods.VK_RCONTROL))
                return false;
        }

        if (required.HasFlag(ModifierKeyFlags.Alt))
        {
            if (!IsKeyDown(NativeMethods.VK_LMENU) && !IsKeyDown(NativeMethods.VK_RMENU))
                return false;
        }

        if (required.HasFlag(ModifierKeyFlags.Shift))
        {
            if (!IsKeyDown(NativeMethods.VK_LSHIFT) && !IsKeyDown(NativeMethods.VK_RSHIFT))
                return false;
        }

        if (required.HasFlag(ModifierKeyFlags.Windows))
        {
            if (!IsKeyDown(NativeMethods.VK_LWIN) && !IsKeyDown(NativeMethods.VK_RWIN))
                return false;
        }

        return true;
    }

    private static bool IsKeyDown(int vk)
    {
        return (NativeMethods.GetAsyncKeyState(vk) & 0x8000) != 0;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_keyboardHookHandle != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_keyboardHookHandle);
            _keyboardHookHandle = IntPtr.Zero;
        }

        if (_hookHandle != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }
    }
}
