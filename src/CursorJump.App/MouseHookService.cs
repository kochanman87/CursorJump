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

    // DOWNイベントを消費した後、対応するUPイベントも消費するためのフラグ
    private bool _swallowNextLeftUp;
    private bool _swallowNextRightUp;
    private bool _swallowNextMiddleUp;

    private readonly SettingsService _settingsService;

    public event EventHandler<MouseHookEventArgs>? SaveRequested;
    public event EventHandler<MouseHookEventArgs>? NavigateRequested;
    public event EventHandler<MouseHookEventArgs>? DisplayDeleteRequested;

    public MouseHookService(SettingsService settingsService)
    {
        _settingsService = settingsService;
        _hookProc = HookCallback;
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

    public void Suspend() => _suspended = true;
    public void Resume() => _suspended = false;

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
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

            // DOWNイベントの処理
            MouseButtonType? pressedButton = msg switch
            {
                NativeMethods.WM_LBUTTONDOWN => MouseButtonType.Left,
                NativeMethods.WM_RBUTTONDOWN => MouseButtonType.Right,
                NativeMethods.WM_MBUTTONDOWN => MouseButtonType.Middle,
                _ => null
            };

            if (pressedButton is not null)
            {
                var settings = _settingsService.Current;
                var hookStruct = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);

                // 各アクションのショートカットを個別にチェック
                if (pressedButton == settings.SaveShortcut.MouseButton
                    && AreModifiersHeld(settings.SaveShortcut.Modifiers))
                {
                    var args = new MouseHookEventArgs(hookStruct.pt.X, hookStruct.pt.Y);
                    SaveRequested?.Invoke(this, args);
                    SetSwallowUpFlag(pressedButton.Value);
                    return (IntPtr)1;
                }

                if (pressedButton == settings.NavigateShortcut.MouseButton
                    && AreModifiersHeld(settings.NavigateShortcut.Modifiers))
                {
                    var args = new MouseHookEventArgs(hookStruct.pt.X, hookStruct.pt.Y);
                    NavigateRequested?.Invoke(this, args);
                    SetSwallowUpFlag(pressedButton.Value);
                    return (IntPtr)1;
                }

                if (pressedButton == settings.DisplayDeleteShortcut.MouseButton
                    && AreModifiersHeld(settings.DisplayDeleteShortcut.Modifiers))
                {
                    var args = new MouseHookEventArgs(hookStruct.pt.X, hookStruct.pt.Y);
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
        }
    }

    private static bool AreModifiersHeld(ModifierKeyFlags required)
    {
        if (required == ModifierKeyFlags.None)
            return false;

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

        if (_hookHandle != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }
    }
}
