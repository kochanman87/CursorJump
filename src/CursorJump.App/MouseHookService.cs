using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using CursorJump.App.Models;

namespace CursorJump.App;

/// <summary>
/// ホイール由来のイベントで上下方向を呼出側へ伝えるためのフラグ。
/// Navigate 系のみ Up=GetPrev / Down=GetNext を切替に使用。Save/DisplayDelete は無視。
/// </summary>
internal enum WheelDirection
{
    None,
    Up,
    Down
}

internal sealed class MouseHookEventArgs : EventArgs
{
    public int X { get; }
    public int Y { get; }
    public WheelDirection Direction { get; }

    public MouseHookEventArgs(int x, int y, WheelDirection direction = WheelDirection.None)
    {
        X = x;
        Y = y;
        Direction = direction;
    }
}

internal sealed class MouseHookService : IDisposable
{
    private IntPtr _hookHandle;
    private readonly NativeMethods.LowLevelMouseProc _hookProc;
    private bool _disposed;
    private volatile bool _suspended;

    // 削除モード関連
    private volatile bool _deleteMode;
    private IntPtr _keyboardHookHandle;
    private readonly NativeMethods.LowLevelKeyboardProc _keyboardHookProc;

    // 削除モード mousemove throttle（フックスレッド書き込み / UI スレッド読み取り）
    private volatile MouseHookEventArgs? _pendingDeleteMove;
    private volatile bool _deleteMoveDispatchQueued;

    // DOWNイベントを消費した後、対応するUPイベントも消費するためのフラグ。
    // bool ではなく Environment.TickCount ベースの「期限」(ms) を持つ:
    // フックタイムアウト (Win11 既定 300ms) で UP が到達しなかった場合に
    // フラグが永続残留して以降のクリックが食われるバグ (バグ4) を防ぐため、
    // 500ms 経過したら自動失効させる。
    private const int SwallowTimeoutMs = 500;
    private long _swallowLeftUpUntil;
    private long _swallowRightUpUntil;
    private long _swallowMiddleUpUntil;
    private long _swallowXButton1UpUntil;
    private long _swallowXButton2UpUntil;
    private bool _swallowNextLeftUp
    {
        get => Environment.TickCount64 < _swallowLeftUpUntil;
        set => _swallowLeftUpUntil = value ? Environment.TickCount64 + SwallowTimeoutMs : 0;
    }
    private bool _swallowNextRightUp
    {
        get => Environment.TickCount64 < _swallowRightUpUntil;
        set => _swallowRightUpUntil = value ? Environment.TickCount64 + SwallowTimeoutMs : 0;
    }
    private bool _swallowNextMiddleUp
    {
        get => Environment.TickCount64 < _swallowMiddleUpUntil;
        set => _swallowMiddleUpUntil = value ? Environment.TickCount64 + SwallowTimeoutMs : 0;
    }
    private bool _swallowNextXButton1Up
    {
        get => Environment.TickCount64 < _swallowXButton1UpUntil;
        set => _swallowXButton1UpUntil = value ? Environment.TickCount64 + SwallowTimeoutMs : 0;
    }
    private bool _swallowNextXButton2Up
    {
        get => Environment.TickCount64 < _swallowXButton2UpUntil;
        set => _swallowXButton2UpUntil = value ? Environment.TickCount64 + SwallowTimeoutMs : 0;
    }

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
    /// <summary>第2座標セット（Set B）の座標保存リクエスト。</summary>
    public event EventHandler<MouseHookEventArgs>? SaveRequestedB;
    /// <summary>第2座標セット（Set B）の座標移動リクエスト。</summary>
    public event EventHandler<MouseHookEventArgs>? NavigateRequestedB;

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
        // 再入ガード: 非同期 RaiseAsync 経由で複数回キューに積まれた場合に
        // キーボードフックを二重インストールしてハンドルを失うのを防ぐ。
        if (_deleteMode)
        {
            DebugLog.Write("MouseHookService: EnterDeleteMode() ignored (already in delete mode)");
            return;
        }

        DebugLog.Write("MouseHookService: EnterDeleteMode()");
        _deleteMode = true;
        // 注: _swallowNextLeftUp/_swallowNextRightUp はここでクリアしない。
        // Chord 発火 → BeginInvoke → EnterDeleteMode の非同期経路で、
        // 直前に立てた swallow フラグが物理 UP 到達前に潰されるとメニューが出る。
        // フラグは対応 UP 到達時に自然消費される設計なので、ここで触る必要はない。

        // ESC検出用キーボードフックをインストール
        IntPtr moduleHandle = NativeMethods.GetModuleHandle(null);
        _keyboardHookHandle = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_KEYBOARD_LL,
            _keyboardHookProc,
            moduleHandle,
            0);
        // GetLastWin32Error は API 直後に保存する（後続の DebugLog.Write が上書きするため）
        int hookError = _keyboardHookHandle == IntPtr.Zero ? Marshal.GetLastWin32Error() : 0;
        DebugLog.Write($"KeyboardHook installed: handle={_keyboardHookHandle}");
        if (_keyboardHookHandle == IntPtr.Zero)
            DebugLog.Write($"KeyboardHook install failed: Win32Error={hookError}");
    }

    /// <summary>
    /// 削除モードを終了し、通常モードに戻る。
    /// </summary>
    public void ExitDeleteMode()
    {
        DebugLog.Write("MouseHookService: ExitDeleteMode()");
        _deleteMode = false;
        _pendingDeleteMove = null;
        _deleteMoveDispatchQueued = false;
        // 注: swallow フラグは EnterDeleteMode と同じ理由でクリアしない。

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
                    RaiseAsync(DeleteModeEscPressed);
                    return (IntPtr)1; // ESCを消費
                }
            }
        }
        return NativeMethods.CallNextHookEx(_keyboardHookHandle, nCode, wParam, lParam);
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = wParam.ToInt32();

            // UP swallow チェックを injected 判定より先に行う。
            // Win11 では他プロセス（マウスドライバー・入力支援ソフト等）が INJECTED フラグ付きの
            // UP イベントを生成することがある。injected チェックを先にすると swallow が機能せず、
            // 対象ウィンドウにイベントが届いてコンテキストメニュー等が出る。
            // DOWN を消費した場合は UP も必ず消費するため、injected に関わらず先に処理する。
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
                // Chord 判定フラグも同時にクリアする（この後 "MUP: Chord判定を解除" に到達しないため）。
                _swallowNextMiddleUp = false;
                lock (_middleLock)
                {
                    _lastMiddleUpTickCount = Environment.TickCount;
                    _middleChordHeld = false;
                }
                return (IntPtr)1;
            }
            if (msg == NativeMethods.WM_XBUTTONUP)
            {
                var xUpStruct = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
                int xUpButtonId = (int)(xUpStruct.mouseData >> 16);
                if (xUpButtonId == NativeMethods.XBUTTON1 && _swallowNextXButton1Up)
                {
                    _swallowNextXButton1Up = false;
                    return (IntPtr)1;
                }
                if (xUpButtonId == NativeMethods.XBUTTON2 && _swallowNextXButton2Up)
                {
                    _swallowNextXButton2Up = false;
                    return (IntPtr)1;
                }
            }

            // 合成入力（SendInput で再送した中クリック等）は以降を素通し。
            // 無限再帰・中ボタン単押しフォールバックの再遅延を防ぐ。
            // ※UP swallow チェックは上で済んでいるため、injected な UP も消費済み。
            var injectCheck = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
            if ((injectCheck.flags & NativeMethods.LLMHF_INJECTED) != 0)
                return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        if (nCode >= 0 && _deleteMode)
        {
            int msg = wParam.ToInt32();
            var hookStruct = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);

            // 削除モード: マウス移動 → ハイライト用イベント（消費しない）
            if (msg == NativeMethods.WM_MOUSEMOVE)
            {
                _pendingDeleteMove = new MouseHookEventArgs(hookStruct.pt.X, hookStruct.pt.Y);
                if (!_deleteMoveDispatchQueued)
                {
                    var moveDispatcher = Application.Current?.Dispatcher;
                    if (moveDispatcher is not null)
                    {
                        _deleteMoveDispatchQueued = true;
                        moveDispatcher.BeginInvoke(() =>
                        {
                            _deleteMoveDispatchQueued = false;
                            DeleteModeMoved?.Invoke(this, _pendingDeleteMove!);
                        });
                    }
                }
                return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
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
                    SetSwallowUpFlag(pressedButton.Value);
                    RaiseAsync(DeleteAllConfirmRequested, args);
                    return (IntPtr)1;
                }

                // 優先2: SaveShortcut マッチ → 追加/削除（ハイブリッド）
                if (IsShortcutMatchForDeleteMode(pressedButton.Value, settings.SaveShortcut))
                {
                    DebugLog.Write($"DeleteMode: SaveShortcut matched at ({hookStruct.pt.X},{hookStruct.pt.Y})");
                    SetSwallowUpFlag(pressedButton.Value);
                    RaiseAsync(DeleteModeClicked, args);
                    return (IntPtr)1;
                }

                // 優先3: NavigateShortcut マッチ → ESC扱い（削除モード終了）
                if (IsShortcutMatchForDeleteMode(pressedButton.Value, settings.NavigateShortcut))
                {
                    DebugLog.Write($"DeleteMode: NavigateShortcut matched → ESC");
                    SetSwallowUpFlag(pressedButton.Value);
                    RaiseAsync(DeleteModeEscPressed);
                    return (IntPtr)1;
                }

                // それ以外はパススルー（右クリックも含む）
            }

            // 削除モード中のホイール: Navigate=MouseWheel なら ESC、DisplayDelete=MouseWheel なら全削除
            // (v1.6.1: 単押し以外でも統合ホイールでモード解除/全削除できるように)
            if (msg == NativeMethods.WM_MOUSEWHEEL)
            {
                var settings = _settingsService.Current;
                if (MatchWheelInDeleteMode(settings.NavigateShortcut))
                {
                    DebugLog.Write("DeleteMode: NavigateShortcut wheel matched → ESC");
                    RaiseAsync(DeleteModeEscPressed);
                    return (IntPtr)1;
                }
                if (MatchWheelInDeleteMode(settings.DisplayDeleteShortcut))
                {
                    DebugLog.Write("DeleteMode: DisplayDeleteShortcut wheel matched → ClearAll");
                    var wheelArgs = new MouseHookEventArgs(hookStruct.pt.X, hookStruct.pt.Y);
                    RaiseAsync(DeleteAllConfirmRequested, wheelArgs);
                    return (IntPtr)1;
                }
            }

            return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        if (nCode >= 0 && !_suspended)
        {
            int msg = wParam.ToInt32();

            // DOWNイベントの処理（hookStructを先にデコードしてXButton判定でも再利用）
            var hookStruct = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);

            // MUP: 拡張トリガー用に MUP 時刻を記録し、Chord 判定を解除
            // （MUP が消費されずに到達した場合の二重保険）
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
                // フック内ではマッチ判定・swallow セット・return 1 のみ同期実行し、
                // 重い処理（WPF/I/O）はすべて RaiseAsync で UI スレッドへ委譲する。
                if (IsShortcutMatch(pressedButton.Value, settings.SaveShortcut))
                {
                    DebugLog.Write($"HookCallback: SaveRequested matched (button={pressedButton.Value})");
                    SetSwallowUpFlag(pressedButton.Value);
                    RaiseAsync(SaveRequested, args);
                    return (IntPtr)1;
                }

                if (IsShortcutMatch(pressedButton.Value, settings.NavigateShortcut))
                {
                    DebugLog.Write($"HookCallback: NavigateRequested matched (button={pressedButton.Value})");
                    SetSwallowUpFlag(pressedButton.Value);
                    RaiseAsync(NavigateRequested, args);
                    return (IntPtr)1;
                }

                if (IsShortcutMatch(pressedButton.Value, settings.NavigateCurrentMonitorShortcut))
                {
                    DebugLog.Write($"HookCallback: NavigateCurrentMonitorRequested matched (button={pressedButton.Value})");
                    SetSwallowUpFlag(pressedButton.Value);
                    RaiseAsync(NavigateCurrentMonitorRequested, args);
                    return (IntPtr)1;
                }

                if (IsShortcutMatch(pressedButton.Value, settings.DisplayDeleteShortcut))
                {
                    DebugLog.Write($"HookCallback: DisplayDeleteRequested matched (button={pressedButton.Value})");
                    SetSwallowUpFlag(pressedButton.Value);
                    RaiseAsync(DisplayDeleteRequested, args);
                    return (IntPtr)1;
                }

                // ── Set B（独立した第2座標セット） ──
                if (IsShortcutMatch(pressedButton.Value, settings.SaveShortcutB))
                {
                    DebugLog.Write($"HookCallback: SaveRequestedB matched (button={pressedButton.Value})");
                    SetSwallowUpFlag(pressedButton.Value);
                    RaiseAsync(SaveRequestedB, args);
                    return (IntPtr)1;
                }

                if (IsShortcutMatch(pressedButton.Value, settings.NavigateShortcutB))
                {
                    DebugLog.Write($"HookCallback: NavigateRequestedB matched (button={pressedButton.Value})");
                    SetSwallowUpFlag(pressedButton.Value);
                    RaiseAsync(NavigateRequestedB, args);
                    return (IntPtr)1;
                }
            }

            // ── ホイールトリガー ──
            // UP イベントが存在しないため SetSwallowUpFlag は不要。
            if (msg == NativeMethods.WM_MOUSEWHEEL)
            {
                var settings = _settingsService.Current;
                if (AnyShortcutUsesWheel(settings))
                {
                    short delta = (short)(hookStruct.mouseData >> 16);
                    var direction = delta > 0 ? WheelDirection.Up : WheelDirection.Down;
                    // 旧 settings.json 後方互換: WheelUp/WheelDown 個別割当に直接マッチさせる
                    var legacyWheelButton = delta > 0 ? MouseButtonType.WheelUp : MouseButtonType.WheelDown;
                    var args = new MouseHookEventArgs(hookStruct.pt.X, hookStruct.pt.Y, direction);

                    if (MatchWheelShortcut(legacyWheelButton, settings.SaveShortcut))
                    {
                        DebugLog.Write($"HookCallback: SaveRequested matched (wheel direction={direction})");
                        RaiseAsync(SaveRequested, args);
                        return (IntPtr)1;
                    }
                    if (MatchWheelShortcut(legacyWheelButton, settings.NavigateShortcut))
                    {
                        DebugLog.Write($"HookCallback: NavigateRequested matched (wheel direction={direction})");
                        RaiseAsync(NavigateRequested, args);
                        return (IntPtr)1;
                    }
                    if (MatchWheelShortcut(legacyWheelButton, settings.NavigateCurrentMonitorShortcut))
                    {
                        DebugLog.Write($"HookCallback: NavigateCurrentMonitorRequested matched (wheel direction={direction})");
                        RaiseAsync(NavigateCurrentMonitorRequested, args);
                        return (IntPtr)1;
                    }
                    if (MatchWheelShortcut(legacyWheelButton, settings.DisplayDeleteShortcut))
                    {
                        DebugLog.Write($"HookCallback: DisplayDeleteRequested matched (wheel direction={direction})");
                        RaiseAsync(DisplayDeleteRequested, args);
                        return (IntPtr)1;
                    }
                    if (MatchWheelShortcut(legacyWheelButton, settings.SaveShortcutB))
                    {
                        DebugLog.Write($"HookCallback: SaveRequestedB matched (wheel direction={direction})");
                        RaiseAsync(SaveRequestedB, args);
                        return (IntPtr)1;
                    }
                    if (MatchWheelShortcut(legacyWheelButton, settings.NavigateShortcutB))
                    {
                        DebugLog.Write($"HookCallback: NavigateRequestedB matched (wheel direction={direction})");
                        RaiseAsync(NavigateRequestedB, args);
                        return (IntPtr)1;
                    }
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
    // 拡張ボタン（Chord / 多重クリック）は基底の物理ボタンに読み替えて判定する。
    // 例: SaveShortcut=MiddleLeftChord の場合、削除モード中は左クリック単押しでマッチ。
    /// <summary>
    /// 削除モード中のホイール用マッチング。MouseWheel 割当のみマッチ（旧 WheelUp/WheelDown は無視）。
    /// </summary>
    private static bool MatchWheelInDeleteMode(Models.ActionShortcut shortcut)
    {
        if (!shortcut.EnabledTriggers.HasFlag(Models.TriggerType.Mouse)) return false;
        return shortcut.MouseButton == MouseButtonType.MouseWheel;
    }

    private static bool IsShortcutMatchForDeleteMode(MouseButtonType pressedButton, Models.ActionShortcut shortcut)
    {
        if (!shortcut.EnabledTriggers.HasFlag(Models.TriggerType.Mouse))
            return false;
        var effective = shortcut.MouseButton switch
        {
            MouseButtonType.MiddleLeftChord
            or MouseButtonType.MiddleDoubleClick => MouseButtonType.Left,
            MouseButtonType.MiddleRightChord
            or MouseButtonType.MiddleTripleClick => MouseButtonType.Right,
            _ => shortcut.MouseButton
        };
        return pressedButton == effective;
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

    // 完全一致判定: required に含まれるキーが押下されており、かつ含まれないキーが押下されていないこと。
    // Win+Alt 時に OS が VK_LMENU/VK_RMENU の async 状態をクリアする場合があるため、
    // Alt 判定は汎用 VK_MENU もフォールバックとして含める。
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
            if (!AreModifiersHeld(sc.Modifiers)) return false;

            // Chord 成立: 単押し/連打判定は打ち切り、後続 LUP/RUP も消費する。
            // 注: _middleChordHeld はここでクリアしない（MUP 到達までは true 継続）。
            // これによりホイール押下のまま L/R を連打しても連続 Chord が発火する。
            // MUP 消費用 _swallowNextMiddleUp は TryDeferMiddleDown で既に true。
            _middleClickCount = 0;
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
        bool middleReleased;
        lock (_middleLock)
        {
            if (_middleClickCount == 0) return; // Chord が先に消費した
            middleReleased = !_middleChordHeld; // MUP で false 化されていれば既に離されている
            if (!middleReleased)
            {
                // ホイール押下継続中: Chord 待機を維持する（長押し → L/R クリックで発火させたい）。
                // 連打カウントだけクリアし、_middleChordHeld は true のまま残す。
                // 次の L/R DOWN は TryHandleMiddleChord で即時発火する。
                _middleClickCount = 0;
                _middleDeferTimer = null;
                DebugLog.Write("MiddleExtended: timer elapsed while still held → keep chord armed");
                return;
            }
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
        {
            sc = FindShortcutByButton(settings, MouseButtonType.MiddleTripleClick);
            if (sc is not null && !AreModifiersHeld(sc.Modifiers)) sc = null;
        }
        if (sc is null && count >= 2)
        {
            sc = FindShortcutByButton(settings, MouseButtonType.MiddleDoubleClick);
            if (sc is not null && !AreModifiersHeld(sc.Modifiers)) sc = null;
        }
        if (sc is null && count == 1)
        {
            // Middle 単押し（従来の修飾キー付き）にフォールバック
            sc = FindMiddleSinglePressMatch(settings);
        }

        if (sc is null)
        {
            // 単押しかつどのアクションにもマッチしなかった場合、
            // 中ボタンが既に離されていればアプリへ合成 MDOWN+MUP を再送する
            // （Chrome のタブクローズ等、通常の中クリック動作を保つため）。
            // 押下継続中は autoscroll 等と区別できないので何もしない。
            if (count == 1 && middleReleased)
            {
                DebugLog.Write($"MiddleExtended: timer elapsed, count=1, no match → synthesize MDOWN+MUP");
                SynthesizeMiddleClick();
            }
            else
            {
                DebugLog.Write($"MiddleExtended: timer elapsed, count={count}, no match");
            }
            return;
        }

        DebugLog.Write($"MiddleExtended: timer elapsed, count={count}, fire {sc.MouseButton}");
        var args = new MouseHookEventArgs(x, y);
        FireShortcutOnUiThread(sc, settings, args);
    }

    private static void SynthesizeMiddleClick()
    {
        var inputs = new NativeMethods.INPUT[2];
        inputs[0].type = NativeMethods.INPUT_MOUSE;
        inputs[0].u.mi.dwFlags = NativeMethods.MOUSEEVENTF_MIDDLEDOWN;
        inputs[1].type = NativeMethods.INPUT_MOUSE;
        inputs[1].u.mi.dwFlags = NativeMethods.MOUSEEVENTF_MIDDLEUP;
        int size = Marshal.SizeOf<NativeMethods.INPUT>();
        NativeMethods.SendInput(2, inputs, size);
    }

    private static bool AnyShortcutUsesMiddleExtended(AppSettings s) =>
        UsesMiddleExtended(s.SaveShortcut) ||
        UsesMiddleExtended(s.NavigateShortcut) ||
        UsesMiddleExtended(s.NavigateCurrentMonitorShortcut) ||
        UsesMiddleExtended(s.DisplayDeleteShortcut) ||
        UsesMiddleExtended(s.SaveShortcutB) ||
        UsesMiddleExtended(s.NavigateShortcutB);

    private static bool UsesMiddleExtended(ActionShortcut sc) =>
        sc.EnabledTriggers.HasFlag(TriggerType.Mouse) &&
        sc.MouseButton is MouseButtonType.MiddleLeftChord
                       or MouseButtonType.MiddleRightChord
                       or MouseButtonType.MiddleDoubleClick
                       or MouseButtonType.MiddleTripleClick;

    private static bool AnyShortcutUsesWheel(AppSettings s) =>
        UsesWheel(s.SaveShortcut) ||
        UsesWheel(s.NavigateShortcut) ||
        UsesWheel(s.NavigateCurrentMonitorShortcut) ||
        UsesWheel(s.DisplayDeleteShortcut) ||
        UsesWheel(s.SaveShortcutB) ||
        UsesWheel(s.NavigateShortcutB);

    private static bool UsesWheel(ActionShortcut sc) =>
        sc.EnabledTriggers.HasFlag(TriggerType.Mouse) &&
        sc.MouseButton is MouseButtonType.WheelUp
                       or MouseButtonType.WheelDown
                       or MouseButtonType.MouseWheel;

    /// <summary>
    /// ホイールイベントとショートカットのマッチング判定。
    /// 旧 settings.json 互換 (WheelUp/WheelDown 個別) と新 MouseWheel (方向中立) の両方を判定する。
    /// </summary>
    private static bool MatchWheelShortcut(MouseButtonType legacyWheelButton, ActionShortcut shortcut)
    {
        if (!shortcut.EnabledTriggers.HasFlag(TriggerType.Mouse)) return false;
        if (shortcut.MouseButton == MouseButtonType.MouseWheel)
            return AreModifiersHeld(shortcut.Modifiers);
        return IsShortcutMatch(legacyWheelButton, shortcut);
    }

    private static ActionShortcut? FindShortcutByButton(AppSettings s, MouseButtonType btn)
    {
        if (s.SaveShortcut.EnabledTriggers.HasFlag(TriggerType.Mouse) && s.SaveShortcut.MouseButton == btn) return s.SaveShortcut;
        if (s.NavigateShortcut.EnabledTriggers.HasFlag(TriggerType.Mouse) && s.NavigateShortcut.MouseButton == btn) return s.NavigateShortcut;
        if (s.NavigateCurrentMonitorShortcut.EnabledTriggers.HasFlag(TriggerType.Mouse) && s.NavigateCurrentMonitorShortcut.MouseButton == btn) return s.NavigateCurrentMonitorShortcut;
        if (s.DisplayDeleteShortcut.EnabledTriggers.HasFlag(TriggerType.Mouse) && s.DisplayDeleteShortcut.MouseButton == btn) return s.DisplayDeleteShortcut;
        if (s.SaveShortcutB.EnabledTriggers.HasFlag(TriggerType.Mouse) && s.SaveShortcutB.MouseButton == btn) return s.SaveShortcutB;
        if (s.NavigateShortcutB.EnabledTriggers.HasFlag(TriggerType.Mouse) && s.NavigateShortcutB.MouseButton == btn) return s.NavigateShortcutB;
        return null;
    }

    /// <summary>Middle 単押し（MouseButton==Middle）のアクションで修飾キーもマッチするもの。</summary>
    private static ActionShortcut? FindMiddleSinglePressMatch(AppSettings s)
    {
        ActionShortcut[] all = { s.SaveShortcut, s.NavigateShortcut, s.NavigateCurrentMonitorShortcut, s.DisplayDeleteShortcut, s.SaveShortcutB, s.NavigateShortcutB };
        foreach (var sc in all)
        {
            if (!sc.EnabledTriggers.HasFlag(TriggerType.Mouse)) continue;
            if (sc.MouseButton != MouseButtonType.Middle) continue;
            if (sc.Modifiers == ModifierKeyFlags.None) continue; // Middle 単押し・修飾なしは許可しない（現行仕様）
            if (AreModifiersHeld(sc.Modifiers)) return sc;
        }
        return null;
    }

    /// <summary>
    /// 低レベルフックコールバックから重い WPF/I/O 処理を切り離すため、
    /// イベント発火を UI スレッドの Dispatcher キューに常に投函する。
    /// swallow フラグ・return 1 はフック側で同期完了させた後に呼ぶこと。
    /// </summary>
    private void RaiseAsync(EventHandler<MouseHookEventArgs>? handler, MouseHookEventArgs args)
    {
        if (handler is null) return;
        var dispatcher = Application.Current?.Dispatcher;
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
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null) { handler(this, EventArgs.Empty); return; }
        dispatcher.BeginInvoke(new Action(() => handler(this, EventArgs.Empty)));
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
            else if (ReferenceEquals(sc, s.SaveShortcutB)) SaveRequestedB?.Invoke(this, args);
            else if (ReferenceEquals(sc, s.NavigateShortcutB)) NavigateRequestedB?.Invoke(this, args);
        };
        if (dispatcher is null) fire();
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
