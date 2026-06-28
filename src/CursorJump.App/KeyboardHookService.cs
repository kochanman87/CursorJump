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

    // 発火済みキーの追跡セット。
    // F13-F24 については KEYDOWN/KEYUP を消費する目印。
    // 任意キー（A-Z 等）については「auto-repeat KEYDOWN を再発火させない」目印
    // （KEYDOWN/KEYUP 自体は他アプリにパススルーする — 共存重視仕様）。
    // KEYUP 到達時に Remove で除去される。
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

    // ── Pro 追加機能（v1.9.0+） ──
    /// <summary>ジャンプ循環リセットのリクエスト（Pro）。</summary>
    public event EventHandler<MouseHookEventArgs>? ResetCycleRequested;
    /// <summary>フォアグラウンド窓中央へジャンプのリクエスト（Pro）。</summary>
    public event EventHandler<MouseHookEventArgs>? ActiveWindowJumpRequested;
    /// <summary>左クリック履歴を 1 つ戻るリクエスト（Pro）。</summary>
    public event EventHandler<MouseHookEventArgs>? ClickHistoryBackRequested;

    // 修飾キー連打ジェスチャ検出（観測専用、キーは消費しない）。
    private readonly ModifierGestureDetector _gestureDetector = new();
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
        _gestureDetector.Reset();
    }

    public void Resume()
    {
        DebugLog.Write("KeyboardHookService: Resume()");
        _suspended = false;
        _gestureDetector.Reset();
    }

    /// <summary>削除モード中はキーボードトリガーを無視する。</summary>
    public void EnterDeleteMode()
    {
        DebugLog.Write("KeyboardHookService: EnterDeleteMode()");
        _deleteMode = true;
        _gestureDetector.Reset();
        // Clear しない: DisplayDelete KEYDOWN で登録した swallow が KEYUP 前に
        // 消えると、UP がアプリへ素通りする（非同期 RaiseAsync 経由の競合）
    }

    /// <summary>削除モードを終了し、キーボードトリガーを再度受け付ける。</summary>
    public void ExitDeleteMode()
    {
        DebugLog.Write("KeyboardHookService: ExitDeleteMode()");
        _deleteMode = false;
        _gestureDetector.Reset();
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0)
            return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);

        int msg = wParam.ToInt32();
        int vkCode = Marshal.ReadInt32(lParam);

        // 修飾キー連打ジェスチャ検出（Pro、v1.9.0+）。
        // 観測専用＝キーは一切消費しない。down/up 双方を投入する。
        // サスペンド中・削除モード中は検出を行わない（_gestureDetector はその遷移時に Reset 済み）。
        if (!_suspended && !_deleteMode)
        {
            bool gDown = msg == NativeMethods.WM_KEYDOWN || msg == NativeMethods.WM_SYSKEYDOWN;
            bool gUp = msg == NativeMethods.WM_KEYUP || msg == NativeMethods.WM_SYSKEYUP;
            if (gDown || gUp)
            {
                var gesture = _gestureDetector.Feed(vkCode, gDown, Environment.TickCount64);
                if (gesture is not null && gesture.Value != ModifierGesture.None)
                    TryFireGesture(gesture.Value);
            }
        }

        // KU イベント: F13-F24 は消費、任意キーはパススルー（共存仕様）
        if (msg == NativeMethods.WM_KEYUP || msg == NativeMethods.WM_SYSKEYUP)
        {
            bool wasActive = _swallowNextKeyUp.Remove(vkCode);
            if (wasActive && ShouldSwallowKey(vkCode))
                return (IntPtr)1;
            return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        // サスペンド中はスルー
        if (_suspended)
            return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);

        // KEYDOWN イベント: ショートカットマッチング
        if (msg == NativeMethods.WM_KEYDOWN || msg == NativeMethods.WM_SYSKEYDOWN)
        {
            // auto-repeat 抑止: KEYUP 到達前の同じ vkCode の再 DOWN は再発火させない
            // F13-F24 は消費して OS への伝達も止める。任意キーは他アプリにパススルー。
            if (_swallowNextKeyUp.Contains(vkCode))
            {
                return ShouldSwallowKey(vkCode)
                    ? (IntPtr)1
                    : NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
            }

            var settings = _settingsService.Current;

            // 削除モード中は DisplayDeleteShortcut / SaveShortcut のみ処理
            if (_deleteMode)
            {
                // 優先1: DisplayDeleteShortcut キーボード側マッチ → 全削除確認リクエスト
                if (IsKeyboardShortcutMatch(vkCode, settings.DisplayDeleteShortcut))
                {
                    DebugLog.Write($"KeyboardHookService: DeleteAllConfirmRequested (vk=0x{vkCode:X2})");
                    RaiseAsync(DeleteAllConfirmRequested, GetCurrentCursorArgs());
                    return CompleteKeyEvent(vkCode, nCode, wParam, lParam);
                }

                // 優先2: SaveShortcut キーボード側マッチ → 追加/削除（ハイブリッド）
                if (IsKeyboardShortcutMatch(vkCode, settings.SaveShortcut))
                {
                    DebugLog.Write($"KeyboardHookService: DeleteModeClicked (vk=0x{vkCode:X2})");
                    RaiseAsync(DeleteModeClicked, GetCurrentCursorArgs());
                    return CompleteKeyEvent(vkCode, nCode, wParam, lParam);
                }

                // 優先3: NavigateShortcut キーボード側マッチ → ESC扱い（削除モード終了）
                if (IsKeyboardShortcutMatch(vkCode, settings.NavigateShortcut))
                {
                    DebugLog.Write($"KeyboardHookService: DeleteMode NavigateShortcut → ESC (vk=0x{vkCode:X2})");
                    RaiseAsync(DeleteModeEscPressed);
                    return CompleteKeyEvent(vkCode, nCode, wParam, lParam);
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
                RaiseAsync(SaveRequested, GetCurrentCursorArgs());
                return CompleteKeyEvent(vkCode, nCode, wParam, lParam);
            }

            if (IsKeyboardShortcutMatch(vkCode, settings.NavigateShortcut))
            {
                DebugLog.Write($"KeyboardHookService: NavigateRequested (vk=0x{vkCode:X2})");
                RaiseAsync(NavigateRequested, GetCurrentCursorArgs());
                return CompleteKeyEvent(vkCode, nCode, wParam, lParam);
            }

            if (IsKeyboardShortcutMatch(vkCode, settings.NavigateCurrentMonitorShortcut))
            {
                DebugLog.Write($"KeyboardHookService: NavigateCurrentMonitorRequested (vk=0x{vkCode:X2})");
                RaiseAsync(NavigateCurrentMonitorRequested, GetCurrentCursorArgs());
                return CompleteKeyEvent(vkCode, nCode, wParam, lParam);
            }

            if (IsKeyboardShortcutMatch(vkCode, settings.DisplayDeleteShortcut))
            {
                DebugLog.Write($"KeyboardHookService: DisplayDeleteRequested (vk=0x{vkCode:X2})");
                RaiseAsync(DisplayDeleteRequested, GetCurrentCursorArgs());
                return CompleteKeyEvent(vkCode, nCode, wParam, lParam);
            }

            // ── Set B（独立した第2座標セット） ──
            if (IsKeyboardShortcutMatch(vkCode, settings.SaveShortcutB))
            {
                DebugLog.Write($"KeyboardHookService: SaveRequestedB (vk=0x{vkCode:X2})");
                RaiseAsync(SaveRequestedB, GetCurrentCursorArgs());
                return CompleteKeyEvent(vkCode, nCode, wParam, lParam);
            }

            if (IsKeyboardShortcutMatch(vkCode, settings.NavigateShortcutB))
            {
                DebugLog.Write($"KeyboardHookService: NavigateRequestedB (vk=0x{vkCode:X2})");
                RaiseAsync(NavigateRequestedB, GetCurrentCursorArgs());
                return CompleteKeyEvent(vkCode, nCode, wParam, lParam);
            }

            // ── Pro 追加機能（v1.9.0+） ──
            if (IsKeyboardShortcutMatch(vkCode, settings.ResetCycleShortcut))
            {
                DebugLog.Write($"KeyboardHookService: ResetCycleRequested (vk=0x{vkCode:X2})");
                RaiseAsync(ResetCycleRequested, GetCurrentCursorArgs());
                return CompleteKeyEvent(vkCode, nCode, wParam, lParam);
            }

            if (IsKeyboardShortcutMatch(vkCode, settings.ActiveWindowJumpShortcut))
            {
                DebugLog.Write($"KeyboardHookService: ActiveWindowJumpRequested (vk=0x{vkCode:X2})");
                RaiseAsync(ActiveWindowJumpRequested, GetCurrentCursorArgs());
                return CompleteKeyEvent(vkCode, nCode, wParam, lParam);
            }

            if (IsKeyboardShortcutMatch(vkCode, settings.ClickHistoryBackShortcut))
            {
                DebugLog.Write($"KeyboardHookService: ClickHistoryBackRequested (vk=0x{vkCode:X2})");
                RaiseAsync(ClickHistoryBackRequested, GetCurrentCursorArgs());
                return CompleteKeyEvent(vkCode, nCode, wParam, lParam);
            }
        }

        return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    /// <summary>
    /// 完了した修飾キー連打ジェスチャに対応するショートカット（Pro 3 種）があれば発火する。
    /// ジェスチャは観測専用のため、ここではキーを消費しない。
    /// </summary>
    private void TryFireGesture(ModifierGesture gesture)
    {
        var s = _settingsService.Current;
        if (GestureMatches(s.ResetCycleShortcut, gesture))
        {
            DebugLog.Write($"KeyboardHookService: ResetCycleRequested (gesture={gesture})");
            RaiseAsync(ResetCycleRequested, GetCurrentCursorArgs());
            return;
        }
        if (GestureMatches(s.ActiveWindowJumpShortcut, gesture))
        {
            DebugLog.Write($"KeyboardHookService: ActiveWindowJumpRequested (gesture={gesture})");
            RaiseAsync(ActiveWindowJumpRequested, GetCurrentCursorArgs());
            return;
        }
        if (GestureMatches(s.ClickHistoryBackShortcut, gesture))
        {
            DebugLog.Write($"KeyboardHookService: ClickHistoryBackRequested (gesture={gesture})");
            RaiseAsync(ClickHistoryBackRequested, GetCurrentCursorArgs());
        }
    }

    private static bool GestureMatches(ActionShortcut sc, ModifierGesture gesture)
        => sc.EnabledTriggers.HasFlag(TriggerType.ModifierSequence)
           && sc.ModifierGesture != ModifierGesture.None
           && sc.ModifierGesture == gesture;

    private static bool IsKeyboardShortcutMatch(int vkCode, ActionShortcut shortcut)
    {
        if (!shortcut.EnabledTriggers.HasFlag(TriggerType.Keyboard))
            return false;
        if (shortcut.VirtualKeyCode == 0)
            return false;
        if (vkCode != shortcut.VirtualKeyCode)
            return false;

        // F13-F24 は VIA マクロ後方互換のため修飾キー無視（VK 単独一致のみ）。
        // VIA マクロでは修飾キー同時押下を保証できないため、UI で修飾キーが
        // 表示されていても実挙動では参照しない（CLAUDE.md 仕様）。
        if (IsViaTriggerKey(shortcut.VirtualKeyCode))
            return true;

        // 任意キー: マウス側と同じ完全一致判定で修飾キーを判定する
        return AreModifiersHeld(shortcut.Modifiers);
    }

    /// <summary>F13-F24 は VIA マクロ後方互換ゾーン（修飾キー無視、KEYDOWN 消費）。</summary>
    private static bool IsViaTriggerKey(int vkCode)
        => vkCode >= NativeMethods.VK_F13 && vkCode <= NativeMethods.VK_F24;

    /// <summary>
    /// 任意キートリガーで他アプリと共存するため、F13-F24 以外は KEYDOWN を消費せず素通しする。
    /// 例: Ctrl+Alt+Z を Navigate に割り当てても、エディタの「Ctrl+Alt+Z = Redo」等に届く。
    /// </summary>
    private static bool ShouldSwallowKey(int vkCode) => IsViaTriggerKey(vkCode);

    /// <summary>
    /// マッチした KEYDOWN の後処理: auto-repeat 抑止のためのマーキングと、
    /// F13-F24 のみ消費・任意キーはパススルーする戻り値を返す。
    /// </summary>
    private IntPtr CompleteKeyEvent(int vkCode, int nCode, IntPtr wParam, IntPtr lParam)
    {
        _swallowNextKeyUp.Add(vkCode);
        return ShouldSwallowKey(vkCode)
            ? (IntPtr)1
            : NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    // マウス側の AreModifiersHeld と同等仕様の完全一致判定。
    // required に含まれるキーが押下されており、かつ含まれないキーが押下されていないこと。
    // Win+Alt 時に OS が VK_LMENU/VK_RMENU の async 状態をクリアする場合があるため、
    // Alt 判定は汎用 VK_MENU もフォールバックに含める。
    private static bool AreModifiersHeld(ModifierKeyFlags required)
    {
        bool ctrlDown  = IsKeyDown(NativeMethods.VK_LCONTROL) || IsKeyDown(NativeMethods.VK_RCONTROL);
        bool altDown   = IsKeyDown(NativeMethods.VK_LMENU)    || IsKeyDown(NativeMethods.VK_RMENU)
                      || IsKeyDown(NativeMethods.VK_MENU);
        bool shiftDown = IsKeyDown(NativeMethods.VK_LSHIFT)   || IsKeyDown(NativeMethods.VK_RSHIFT);
        bool winDown   = IsKeyDown(NativeMethods.VK_LWIN)     || IsKeyDown(NativeMethods.VK_RWIN);

        if (ctrlDown  != required.HasFlag(ModifierKeyFlags.Control))  return false;
        if (altDown   != required.HasFlag(ModifierKeyFlags.Alt))      return false;
        if (shiftDown != required.HasFlag(ModifierKeyFlags.Shift))    return false;
        if (winDown   != required.HasFlag(ModifierKeyFlags.Windows))  return false;
        return true;
    }

    private static bool IsKeyDown(int vk)
        => (NativeMethods.GetAsyncKeyState(vk) & 0x8000) != 0;

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
