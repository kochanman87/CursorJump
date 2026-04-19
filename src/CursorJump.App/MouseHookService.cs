using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
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

    // 中ボタン拡張トリガー（Chord / 多重クリック）用ステート
    private const int ChordWindowMs = 200;        // MDOWN 後 L/R を待つ時間 & 多重クリック待ち時間
    private const int MultiClickWindowMs = 350;   // 前 MUP → 次 MDOWN の間隔上限
    private readonly object _middleLock = new();
    private int _middleDownTickCount;
    private int _middleDownX;
    private int _middleDownY;
    private int _middleClickCount;
    private int _lastMiddleUpTickCount = -100000;
    private bool _middleChordHeld;  // MDOWN 消費中かつ MUP 未検知 → L/R と Chord 成立可能
    private Timer? _middleDeferTimer;

    private readonly SettingsService _settingsService;

    public event EventHandler<MouseHookEventArgs>? SaveRequested;
    public event EventHandler<MouseHookEventArgs>? NavigateRequested;
    public event EventHandler<MouseHookEventArgs>? NavigateCurrentMonitorRequested;
    public event EventHandler<MouseHookEventArgs>? DisplayDeleteRequested;

    // 削除モード用イベント
    public event EventHandler<MouseHookEventArgs>? DeleteModeClicked;
    public event EventHandler<MouseHookEventArgs>? DeleteModeMoved;
    public event EventHandler? DeleteModeEscPressed;
    /// <summary>削除モード中に DisplayDeleteShortcut がマッチしたとき発火（全削除2段階確認に使用）。</summary>
    public event EventHandler<MouseHookEventArgs>? DeleteAllConfirmRequested;

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
        ResetMiddleState();
    }

    private void ResetMiddleState()
    {
        lock (_middleLock)
        {
            _middleChordHeld = false;
            _middleClickCount = 0;
            _middleDeferTimer?.Dispose();
            _middleDeferTimer = null;
        }
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

            // 削除モード: UPイベントの消費チェック（DOWNを消費済みの場合）
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

            // 削除モード: ボタン DOWNイベント → DisplayDeleteShortcut / SaveShortcut に従って処理
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

                // 削除モード中は修飾キー不要で対応ボタン単押しでOK
                // 優先1: DisplayDeleteShortcut マッチ → 全削除確認リクエスト
                if (IsShortcutMatchForDeleteMode(pressedButton.Value, settings.DisplayDeleteShortcut))
                {
                    DebugLog.Write($"DeleteMode: DisplayDeleteShortcut matched at ({hookStruct.pt.X},{hookStruct.pt.Y})");
                    DeleteAllConfirmRequested?.Invoke(this, args);
                    SetSwallowUpFlag(pressedButton.Value);
                    return (IntPtr)1;
                }

                // 優先2: SaveShortcut マッチ → 追加/削除（ハイブリッド）
                if (IsShortcutMatchForDeleteMode(pressedButton.Value, settings.SaveShortcut))
                {
                    DebugLog.Write($"DeleteMode: SaveShortcut matched at ({hookStruct.pt.X},{hookStruct.pt.Y})");
                    DeleteModeClicked?.Invoke(this, args);
                    SetSwallowUpFlag(pressedButton.Value);
                    return (IntPtr)1;
                }

                // 優先3: NavigateShortcut マッチ → ESC扱い（削除モード終了）
                if (IsShortcutMatchForDeleteMode(pressedButton.Value, settings.NavigateShortcut))
                {
                    DebugLog.Write($"DeleteMode: NavigateShortcut matched → ESC");
                    DeleteModeEscPressed?.Invoke(this, EventArgs.Empty);
                    SetSwallowUpFlag(pressedButton.Value);
                    return (IntPtr)1;
                }

                // それ以外はパススルー（右クリックも含む）
            }

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

            // MUP: 拡張トリガー用に MUP 時刻を記録し、Chord 判定を解除
            if (msg == NativeMethods.WM_MBUTTONUP)
            {
                lock (_middleLock)
                {
                    _lastMiddleUpTickCount = Environment.TickCount;
                    _middleChordHeld = false;
                }
            }

            // Middle 拡張: L/R DOWN で Chord 判定
            if (msg == NativeMethods.WM_LBUTTONDOWN || msg == NativeMethods.WM_RBUTTONDOWN)
            {
                if (TryHandleMiddleChord(msg, hookStruct.pt.X, hookStruct.pt.Y))
                    return (IntPtr)1;
            }

            // Middle 拡張: MDOWN を遅延判定に回す（拡張トリガー割当時のみ）
            if (msg == NativeMethods.WM_MBUTTONDOWN)
            {
                if (TryDeferMiddleDown(hookStruct.pt.X, hookStruct.pt.Y))
                    return (IntPtr)1;
            }

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

    // 削除モード中専用: 対応ボタンの単押しだけでマッチ（修飾キー不問）
    private static bool IsShortcutMatchForDeleteMode(MouseButtonType pressedButton, Models.ActionShortcut shortcut)
    {
        if (!shortcut.EnabledTriggers.HasFlag(Models.TriggerType.Mouse))
            return false;
        return pressedButton == shortcut.MouseButton;
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

    // ===== 中ボタン拡張トリガー（Chord / 多重クリック）の判定 =====

    /// <summary>
    /// WM_MBUTTONDOWN を拡張トリガー用に消費するか判定。
    /// 拡張ボタン（MiddleLeftChord/MiddleRightChord/MiddleDoubleClick/MiddleTripleClick）が
    /// どれか1つでも割り当てられていれば、MDOWN を消費し、ChordWindowMs 後に
    /// クリック数に応じて適切なアクションを発火する。
    /// </summary>
    private bool TryDeferMiddleDown(int x, int y)
    {
        var settings = _settingsService.Current;
        if (!AnyShortcutUsesMiddleExtended(settings))
            return false;

        lock (_middleLock)
        {
            int now = Environment.TickCount;
            if (now - _lastMiddleUpTickCount <= MultiClickWindowMs)
                _middleClickCount++;
            else
                _middleClickCount = 1;

            _middleDownTickCount = now;
            _middleDownX = x;
            _middleDownY = y;
            _middleChordHeld = true;
            _swallowNextMiddleUp = true;

            _middleDeferTimer?.Dispose();
            _middleDeferTimer = new Timer(_ => OnMiddleDeferElapsed(), null, ChordWindowMs, Timeout.Infinite);
        }

        DebugLog.Write($"MiddleExtended: MDOWN deferred at ({x},{y}), clickCount={_middleClickCount}");
        return true;
    }

    /// <summary>
    /// L/R DOWN で Chord（ホイール押下＋L/R）を検知して発火する。
    /// 中ボタンが物理的に押下中かつ対応 Chord が割り当てられていれば true を返す。
    /// </summary>
    private bool TryHandleMiddleChord(int msg, int x, int y)
    {
        ActionShortcut? sc;
        AppSettings settings;
        MouseButtonType chordBtn;
        lock (_middleLock)
        {
            if (!_middleChordHeld) return false;
            // 注: GetAsyncKeyState(VK_MBUTTON) は、フックで WM_MBUTTONDOWN を消費する
            // (return (IntPtr)1) と OS の非同期キー状態に反映されないため使わない。
            // _middleChordHeld フラグのみで判定する（MUP 到達時にクリアされる）。

            settings = _settingsService.Current;
            chordBtn = msg == NativeMethods.WM_LBUTTONDOWN
                ? MouseButtonType.MiddleLeftChord
                : MouseButtonType.MiddleRightChord;

            sc = FindShortcutByButton(settings, chordBtn);
            if (sc is null) return false;

            // Chord 成立: 単押し/連打判定は打ち切り、後続 MUP/LUP/RUP も消費する
            _middleClickCount = 0;
            _middleChordHeld = false;
            _middleDeferTimer?.Dispose();
            _middleDeferTimer = null;

            if (msg == NativeMethods.WM_LBUTTONDOWN) _swallowNextLeftUp = true;
            else _swallowNextRightUp = true;
        }

        DebugLog.Write($"MiddleExtended: Chord {chordBtn} fired at ({x},{y})");
        var args = new MouseHookEventArgs(x, y);
        FireShortcutOnUiThread(sc, settings, args);
        return true;
    }

    /// <summary>
    /// ChordWindowMs 経過後に呼ばれる。クリック数に応じてアクションを発火。
    /// Chord が成立していた場合はすでに状態がリセットされているので何もしない。
    /// </summary>
    private void OnMiddleDeferElapsed()
    {
        AppSettings settings;
        int count, x, y;
        lock (_middleLock)
        {
            if (_middleClickCount == 0) return; // Chord が先に消費した
            count = _middleClickCount;
            x = _middleDownX;
            y = _middleDownY;
            _middleClickCount = 0;
            _middleChordHeld = false; // 以降の L/R は通常クリック扱い
            _middleDeferTimer = null;
            settings = _settingsService.Current;
        }

        ActionShortcut? sc = null;
        if (count >= 3)
            sc = FindShortcutByButton(settings, MouseButtonType.MiddleTripleClick);
        if (sc is null && count >= 2)
            sc = FindShortcutByButton(settings, MouseButtonType.MiddleDoubleClick);
        if (sc is null && count == 1)
        {
            // Middle 単押し（従来の修飾キー付き）にフォールバック
            sc = FindMiddleSinglePressMatch(settings);
        }

        if (sc is null)
        {
            DebugLog.Write($"MiddleExtended: timer elapsed, count={count}, no match");
            return;
        }

        DebugLog.Write($"MiddleExtended: timer elapsed, count={count}, fire {sc.MouseButton}");
        var args = new MouseHookEventArgs(x, y);
        FireShortcutOnUiThread(sc, settings, args);
    }

    private static bool AnyShortcutUsesMiddleExtended(AppSettings s) =>
        UsesMiddleExtended(s.SaveShortcut) ||
        UsesMiddleExtended(s.NavigateShortcut) ||
        UsesMiddleExtended(s.NavigateCurrentMonitorShortcut) ||
        UsesMiddleExtended(s.DisplayDeleteShortcut);

    private static bool UsesMiddleExtended(ActionShortcut sc) =>
        sc.EnabledTriggers.HasFlag(TriggerType.Mouse) &&
        sc.MouseButton is MouseButtonType.MiddleLeftChord
                       or MouseButtonType.MiddleRightChord
                       or MouseButtonType.MiddleDoubleClick
                       or MouseButtonType.MiddleTripleClick;

    private static ActionShortcut? FindShortcutByButton(AppSettings s, MouseButtonType btn)
    {
        if (s.SaveShortcut.EnabledTriggers.HasFlag(TriggerType.Mouse) && s.SaveShortcut.MouseButton == btn) return s.SaveShortcut;
        if (s.NavigateShortcut.EnabledTriggers.HasFlag(TriggerType.Mouse) && s.NavigateShortcut.MouseButton == btn) return s.NavigateShortcut;
        if (s.NavigateCurrentMonitorShortcut.EnabledTriggers.HasFlag(TriggerType.Mouse) && s.NavigateCurrentMonitorShortcut.MouseButton == btn) return s.NavigateCurrentMonitorShortcut;
        if (s.DisplayDeleteShortcut.EnabledTriggers.HasFlag(TriggerType.Mouse) && s.DisplayDeleteShortcut.MouseButton == btn) return s.DisplayDeleteShortcut;
        return null;
    }

    /// <summary>Middle 単押し（MouseButton==Middle）のアクションで修飾キーもマッチするもの。</summary>
    private static ActionShortcut? FindMiddleSinglePressMatch(AppSettings s)
    {
        ActionShortcut[] all = { s.SaveShortcut, s.NavigateShortcut, s.NavigateCurrentMonitorShortcut, s.DisplayDeleteShortcut };
        foreach (var sc in all)
        {
            if (!sc.EnabledTriggers.HasFlag(TriggerType.Mouse)) continue;
            if (sc.MouseButton != MouseButtonType.Middle) continue;
            if (sc.Modifiers == ModifierKeyFlags.None) continue; // Middle 単押し・修飾なしは許可しない（現行仕様）
            if (AreModifiersHeld(sc.Modifiers)) return sc;
        }
        return null;
    }

    private void FireShortcutOnUiThread(ActionShortcut sc, AppSettings s, MouseHookEventArgs args)
    {
        var dispatcher = Application.Current?.Dispatcher;
        Action fire = () =>
        {
            if (ReferenceEquals(sc, s.SaveShortcut)) SaveRequested?.Invoke(this, args);
            else if (ReferenceEquals(sc, s.NavigateShortcut)) NavigateRequested?.Invoke(this, args);
            else if (ReferenceEquals(sc, s.NavigateCurrentMonitorShortcut)) NavigateCurrentMonitorRequested?.Invoke(this, args);
            else if (ReferenceEquals(sc, s.DisplayDeleteShortcut)) DisplayDeleteRequested?.Invoke(this, args);
        };
        if (dispatcher is null || dispatcher.CheckAccess()) fire();
        else dispatcher.BeginInvoke(fire);
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

        ResetMiddleState();
    }
}
