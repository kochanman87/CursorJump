using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using CursorJump.App.Models;

namespace CursorJump.App;

/// <summary>
/// WH_KEYBOARD_LL を常時インストールし、F13-F24 等のキーボードトリガーを検知するサービス。
/// MouseHookService と並列に動作し、同じイベントシグネチャを発火する。
/// VIA キーボードのマクロ機能と連携して使用することを想定している。
/// </summary>
internal sealed class KeyboardHookService : IDisposable
{
    private IntPtr _hookHandle;
    private readonly NativeMethods.LowLevelKeyboardProc _hookProc;
    private bool _disposed;
    private volatile bool _suspended;

    // 削除モード中はトリガーキーを無視する
    private volatile bool _deleteMode;

    // KEYDOWNを消費した後、対応するKEYUPも消費するためのキーコードセット
    private readonly HashSet<int> _swallowNextKeyUp = new();

    private readonly SettingsService _settingsService;

    public event EventHandler<MouseHookEventArgs>? SaveRequested;
    public event EventHandler<MouseHookEventArgs>? NavigateRequested;
    public event EventHandler<MouseHookEventArgs>? NavigateCurrentMonitorRequested;
    public event EventHandler<MouseHookEventArgs>? DisplayDeleteRequested;
    /// <summary>第2座標セット（Set B）の座標保存リクエスト。</summary>
    public event EventHandler<MouseHookEventArgs>? SaveRequestedB;
    /// <summary>第2座標セット（Set B）の座標移動リクエスト。</summary>
    public event EventHandler<MouseHookEventArgs>? NavigateRequestedB;
    /// <summary>削除モード中に DisplayDeleteShortcut がマッチしたとき発火（全削除に使用）。</summary>
    public event EventHandler<MouseHookEventArgs>? DeleteAllConfirmRequested;
    /// <summary>削除モード中に SaveShortcut がマッチしたとき発火（追加/削除ハイブリッド）。</summary>
    public event EventHandler<MouseHookEventArgs>? DeleteModeClicked;
    /// <summary>削除モード中に NavigateShortcut がマッチしたとき発火（ESC扱い＝削除モード終了）。</summary>
    public event EventHandler? DeleteModeEscPressed;

    public KeyboardHookService(SettingsService settingsService)
    {
        _settingsService = settingsService;
        _hookProc = HookCallback;
    }

    public void Install()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(KeyboardHookService));
        if (_hookHandle != IntPtr.Zero) return;

        IntPtr moduleHandle = NativeMethods.GetModuleHandle(null);
        _hookHandle = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_KEYBOARD_LL,
            _hookProc,
            moduleHandle,
            0);

        if (_hookHandle == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error());

        DebugLog.Write("KeyboardHookService: Installed");
    }

    public void Suspend()
    {
        DebugLog.Write("KeyboardHookService: Suspend()");
        _suspended = true;
        _swallowNextKeyUp.Clear();
    }

    public void Resume()
    {
        DebugLog.Write("KeyboardHookService: Resume()");
        _suspended = false;
    }

    /// <summary>削除モード中はキーボードトリガーを無視する。</summary>
    public void EnterDeleteMode()
    {
        DebugLog.Write("KeyboardHookService: EnterDeleteMode()");
        _deleteMode = true;
        // Clear しない: DisplayDelete KEYDOWN で登録した swallow が KEYUP 前に
        // 消えると、UP がアプリへ素通りする（非同期 RaiseAsync 経由の競合）
    }

    /// <summary>削除モードを終了し、キーボードトリガーを再度受け付ける。</summary>
    public void ExitDeleteMode()
    {
        DebugLog.Write("KeyboardHookService: ExitDeleteMode()");
        _deleteMode = false;
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0)
            return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);

        int msg = wParam.ToInt32();
        int vkCode = Marshal.ReadInt32(lParam);

        // KU イベント: 消費対象なら消費してスルー
        if (msg == NativeMethods.WM_KEYUP || msg == NativeMethods.WM_SYSKEYUP)
        {
            if (_swallowNextKeyUp.Remove(vkCode))
                return (IntPtr)1;
            return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        // サスペンド中はスルー
        if (_suspended)
            return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);

        // KEYDOWN イベント: ショートカットマッチング
        if (msg == NativeMethods.WM_KEYDOWN || msg == NativeMethods.WM_SYSKEYDOWN)
        {
            // auto-repeat 抑止: KEYUP 到達前の同じ vkCode の再 DOWN は無視
            if (_swallowNextKeyUp.Contains(vkCode))
                return (IntPtr)1;

            var settings = _settingsService.Current;

            // 削除モード中は DisplayDeleteShortcut / SaveShortcut のみ処理
            if (_deleteMode)
            {
                // 優先1: DisplayDeleteShortcut キーボード側マッチ → 全削除確認リクエスト
                if (IsKeyboardShortcutMatch(vkCode, settings.DisplayDeleteShortcut))
                {
                    DebugLog.Write($"KeyboardHookService: DeleteAllConfirmRequested (vk=0x{vkCode:X2})");
                    var args = GetCurrentCursorArgs();
                    _swallowNextKeyUp.Add(vkCode);
                    RaiseAsync(DeleteAllConfirmRequested, args);
                    return (IntPtr)1;
                }

                // 優先2: SaveShortcut キーボード側マッチ → 追加/削除（ハイブリッド）
                if (IsKeyboardShortcutMatch(vkCode, settings.SaveShortcut))
                {
                    DebugLog.Write($"KeyboardHookService: DeleteModeClicked (vk=0x{vkCode:X2})");
                    var args = GetCurrentCursorArgs();
                    _swallowNextKeyUp.Add(vkCode);
                    RaiseAsync(DeleteModeClicked, args);
                    return (IntPtr)1;
                }

                // 優先3: NavigateShortcut キーボード側マッチ → ESC扱い（削除モード終了）
                if (IsKeyboardShortcutMatch(vkCode, settings.NavigateShortcut))
                {
                    DebugLog.Write($"KeyboardHookService: DeleteMode NavigateShortcut → ESC (vk=0x{vkCode:X2})");
                    _swallowNextKeyUp.Add(vkCode);
                    RaiseAsync(DeleteModeEscPressed);
                    return (IntPtr)1;
                }

                // それ以外はパススルー（ESCはMouseHookService側のキーボードフックで処理）
                return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
            }

            // 通常モード: 全ショートカットマッチング
            // フック内ではマッチ判定・swallow セット・return 1 のみ同期実行し、
            // 重い処理（WPF/I/O）はすべて RaiseAsync で UI スレッドへ委譲する。
            if (IsKeyboardShortcutMatch(vkCode, settings.SaveShortcut))
            {
                DebugLog.Write($"KeyboardHookService: SaveRequested (vk=0x{vkCode:X2})");
                _swallowNextKeyUp.Add(vkCode);
                RaiseAsync(SaveRequested, GetCurrentCursorArgs());
                return (IntPtr)1;
            }

            if (IsKeyboardShortcutMatch(vkCode, settings.NavigateShortcut))
            {
                DebugLog.Write($"KeyboardHookService: NavigateRequested (vk=0x{vkCode:X2})");
                _swallowNextKeyUp.Add(vkCode);
                RaiseAsync(NavigateRequested, GetCurrentCursorArgs());
                return (IntPtr)1;
            }

            if (IsKeyboardShortcutMatch(vkCode, settings.NavigateCurrentMonitorShortcut))
            {
                DebugLog.Write($"KeyboardHookService: NavigateCurrentMonitorRequested (vk=0x{vkCode:X2})");
                _swallowNextKeyUp.Add(vkCode);
                RaiseAsync(NavigateCurrentMonitorRequested, GetCurrentCursorArgs());
                return (IntPtr)1;
            }

            if (IsKeyboardShortcutMatch(vkCode, settings.DisplayDeleteShortcut))
            {
                DebugLog.Write($"KeyboardHookService: DisplayDeleteRequested (vk=0x{vkCode:X2})");
                var args = GetCurrentCursorArgs();
                _swallowNextKeyUp.Add(vkCode);
                RaiseAsync(DisplayDeleteRequested, args);
                return (IntPtr)1;
            }

            // ── Set B（独立した第2座標セット） ──
            if (IsKeyboardShortcutMatch(vkCode, settings.SaveShortcutB))
            {
                DebugLog.Write($"KeyboardHookService: SaveRequestedB (vk=0x{vkCode:X2})");
                _swallowNextKeyUp.Add(vkCode);
                RaiseAsync(SaveRequestedB, GetCurrentCursorArgs());
                return (IntPtr)1;
            }

            if (IsKeyboardShortcutMatch(vkCode, settings.NavigateShortcutB))
            {
                DebugLog.Write($"KeyboardHookService: NavigateRequestedB (vk=0x{vkCode:X2})");
                _swallowNextKeyUp.Add(vkCode);
                RaiseAsync(NavigateRequestedB, GetCurrentCursorArgs());
                return (IntPtr)1;
            }
        }

        return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    private static bool IsKeyboardShortcutMatch(int vkCode, ActionShortcut shortcut)
    {
        if (!shortcut.EnabledTriggers.HasFlag(TriggerType.Keyboard))
            return false;
        if (shortcut.VirtualKeyCode == 0)
            return false;
        return vkCode == shortcut.VirtualKeyCode;
    }

    /// <summary>
    /// 低レベルフックコールバックから重い処理を切り離すため、
    /// イベント発火を UI スレッドの Dispatcher キューに常に投函する。
    /// swallow セットへの追加・return 1 はフック側で同期完了させた後に呼ぶこと。
    /// </summary>
    private void RaiseAsync(EventHandler<MouseHookEventArgs>? handler, MouseHookEventArgs args)
    {
        if (handler is null) return;
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            handler(this, args);
            return;
        }
        dispatcher.BeginInvoke(new Action(() => handler(this, args)));
    }

    private void RaiseAsync(EventHandler? handler)
    {
        if (handler is null) return;
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null) { handler(this, EventArgs.Empty); return; }
        dispatcher.BeginInvoke(new Action(() => handler(this, EventArgs.Empty)));
    }

    /// <summary>キーボードトリガー時はフック座標がないため、GetCursorPos で現在位置を取得する。</summary>
    private static MouseHookEventArgs GetCurrentCursorArgs()
    {
        NativeMethods.GetCursorPos(out var pt);
        return new MouseHookEventArgs(pt.X, pt.Y);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_hookHandle != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
            DebugLog.Write("KeyboardHookService: Disposed");
        }
    }
}
