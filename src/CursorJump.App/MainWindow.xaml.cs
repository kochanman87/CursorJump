using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using CursorJump.App.Models;

namespace CursorJump.App;

/// <summary>
/// タスクトレイ常駐用の不可視ウィンドウ。マウスフックの管理を担当する。
/// </summary>
public partial class MainWindow : Window
{
    private MouseHookService? _mouseHookService;
    private KeyboardHookService? _keyboardHookService;
    private readonly SettingsService _settingsService;
    private readonly LicenseService _licenseService;
    private readonly CoordinateStore _coordinateStore = new();
    private readonly CoordinateStore _coordinateStoreB = new();
    private readonly OverlayService _overlayService;

    public MainWindow(SettingsService settingsService, LicenseService licenseService)
    {
        _settingsService = settingsService;
        _licenseService = licenseService;
        _overlayService = new OverlayService(settingsService, licenseService);

        // 永続化された座標を復元 (戻り値 true = マイグレーション発生 = 書き戻し必要)
        bool migratedA = _coordinateStore.Load(_settingsService.Current.SavedCoordinatesA);
        bool migratedB = _coordinateStoreB.Load(_settingsService.Current.SavedCoordinatesB);

        // 変更時に settings.json へ書き戻す
        _coordinateStore.Changed += OnCoordinateStoreAChanged;
        _coordinateStoreB.Changed += OnCoordinateStoreBChanged;

        // マイグレーション発生時は即座に Save (旧データに MonitorRelativeX/Y を埋めた結果を永続化)
        if (migratedA)
        {
            DebugLog.Write("MainWindow: SavedCoordinatesA migrated to include MonitorRelative offsets → saving");
            OnCoordinateStoreAChanged();
        }
        if (migratedB)
        {
            DebugLog.Write("MainWindow: SavedCoordinatesB migrated to include MonitorRelative offsets → saving");
            OnCoordinateStoreBChanged();
        }

        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
        Closed += OnClosed;
    }

    private void OnCoordinateStoreAChanged()
    {
        var snap = _settingsService.Current.Clone();
        snap.SavedCoordinatesA = _coordinateStore.GetAll().ToList();
        if (!_settingsService.Save(snap))
            DebugLog.Write("MainWindow: SavedCoordinatesA persistence failed — memory state diverged from settings.json. Coordinates may be lost on next app restart.");
    }

    private void OnCoordinateStoreBChanged()
    {
        var snap = _settingsService.Current.Clone();
        snap.SavedCoordinatesB = _coordinateStoreB.GetAll().ToList();
        if (!_settingsService.Save(snap))
            DebugLog.Write("MainWindow: SavedCoordinatesB persistence failed — memory state diverged from settings.json. Coordinates may be lost on next app restart.");
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        try
        {
            _mouseHookService = new MouseHookService(_settingsService);
            _mouseHookService.SaveRequested += OnSaveRequested;
            _mouseHookService.NavigateRequested += OnNavigateRequested;
            _mouseHookService.NavigateCurrentMonitorRequested += OnNavigateCurrentMonitorRequested;
            _mouseHookService.DisplayDeleteRequested += OnDisplayDeleteRequested;
            _mouseHookService.SaveRequestedB += OnSaveRequestedB;
            _mouseHookService.NavigateRequestedB += OnNavigateRequestedB;
            _mouseHookService.Install();

            _overlayService.SetMouseHookService(_mouseHookService);
        }
        catch (Win32Exception ex)
        {
            MessageBox.Show(
                string.Format(Loc.Get("Str.MessageBox.MouseHookFailedFormat"), ex.Message),
                Loc.Get("Str.AppName"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        try
        {
            _keyboardHookService = new KeyboardHookService(_settingsService);
            _keyboardHookService.SaveRequested += OnSaveRequested;
            _keyboardHookService.NavigateRequested += OnNavigateRequested;
            _keyboardHookService.NavigateCurrentMonitorRequested += OnNavigateCurrentMonitorRequested;
            _keyboardHookService.DisplayDeleteRequested += OnDisplayDeleteRequested;
            _keyboardHookService.SaveRequestedB += OnSaveRequestedB;
            _keyboardHookService.NavigateRequestedB += OnNavigateRequestedB;
            _keyboardHookService.Install();

            _overlayService.SetKeyboardHookService(_keyboardHookService);
        }
        catch (Win32Exception ex)
        {
            MessageBox.Show(
                string.Format(Loc.Get("Str.MessageBox.KeyboardHookFailedFormat"), ex.Message),
                Loc.Get("Str.AppName"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        // 軌跡/収縮円オーバーレイを起動時に 1 枚プリアロケートする (v1.6.1: HWND 生成コストを 1 回で済ませてジャンプ後のラグ/砂時計を解消)
        try { _overlayService.PreallocateTrailOverlay(); }
        catch (Exception ex) { DebugLog.Write($"MainWindow: PreallocateTrailOverlay failed: {ex.Message}"); }

        StartMemoryDiagnosticsTimer();
    }

    // 800MB メモリ成長問題の診断用一時計装。1 分毎に WorkingSet / Private / GC.GetTotalMemory /
    // _trailOverlay の Canvas 子要素数を debug.log に記録する。原因確定後に削除する想定。
    private System.Windows.Threading.DispatcherTimer? _memDiagTimer;
    private void StartMemoryDiagnosticsTimer()
    {
        try
        {
            LogMemorySnapshot("startup");
            _memDiagTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(1)
            };
            _memDiagTimer.Tick += (_, _) => LogMemorySnapshot("periodic");
            _memDiagTimer.Start();
        }
        catch (Exception ex) { DebugLog.Write($"StartMemoryDiagnosticsTimer failed: {ex.Message}"); }
    }

    // v1.7.4 計装: 前回 MemDiag tick 時点の累計 Remove 数。差分を取って「この 1 分で何個 Remove されたか」を出す。
    private long _lastTrailRemoveTotal;
    private void LogMemorySnapshot(string label)
    {
        try
        {
            using var proc = System.Diagnostics.Process.GetCurrentProcess();
            long ws = proc.WorkingSet64 / (1024 * 1024);
            long pm = proc.PrivateMemorySize64 / (1024 * 1024);
            long gc = GC.GetTotalMemory(forceFullCollection: false) / (1024 * 1024);
            int trail = _overlayService.TrailOverlayChildCount;
            long curTotal = _overlayService.TrailRemoveSuccessTotal;
            long removedSince = curTotal - _lastTrailRemoveTotal;
            _lastTrailRemoveTotal = curTotal;
            DebugLog.Write($"MemDiag[{label}]: WorkingSet={ws}MB, Private={pm}MB, GC.Total={gc}MB, TrailChildren={trail}, RemovedSinceLastTick={removedSince}, RemovedTotal={curTotal}");
        }
        catch (Exception ex) { DebugLog.Write($"LogMemorySnapshot failed: {ex.Message}"); }
    }

    private void OnSaveRequested(object? sender, MouseHookEventArgs e)
    {
        if (!_licenseService.IsPro && _coordinateStore.Count >= LicenseService.FreeMaxCoordinates)
        {
            // Free 版上限到達: 保存もエフェクトも行わず、ユーザーには静かに失敗させる
            // （頻繁にトースト/モーダルを出すと作業中断になる。設定画面の Free 表記で気付いてもらう設計）
            DebugLog.Write($"OnSaveRequested: blocked by Free edition limit ({_coordinateStore.Count}/{LicenseService.FreeMaxCoordinates})");
            return;
        }
        _coordinateStore.Add(e.X, e.Y);
        _overlayService.ShowShrinkCircle(e.X, e.Y);
    }

    private void OnNavigateRequested(object? sender, MouseHookEventArgs e)
    {
        var connected = GetConnectedMonitorNames();
        // ホイール上スクロール (MouseWheel 統合トリガー) なら逆方向循環
        var target = e.Direction == WheelDirection.Up
            ? _coordinateStore.GetPrev(connected)
            : _coordinateStore.GetNext(connected);
        if (target is null) return;

        int fromX = e.X;
        int fromY = e.Y;
        var (jumpX, jumpY, source) = ResolveJumpTarget(target);

        if (_settingsService.Current.VerboseLogging)
        {
            DebugLog.Write($"NavigateA before: stored=({target.X},{target.Y}) monitor={target.MonitorDeviceName} rel=({target.MonitorRelativeX},{target.MonitorRelativeY}) jump=({jumpX},{jumpY}) source={source} fromCursor=({fromX},{fromY})");
        }

        CursorService.JumpTo(jumpX, jumpY, _settingsService.Current.JumpStrategy);

        if (_settingsService.Current.VerboseLogging)
        {
            if (NativeMethods.GetCursorPos(out var actual))
                DebugLog.Write($"NavigateA after: actualCursor=({actual.X},{actual.Y}) delta=({actual.X - jumpX},{actual.Y - jumpY})");
        }

        if (_settingsService.Current.ActivateWindowUnderCursorOnJump)
            WindowActivator.Activate(jumpX, jumpY);

        _overlayService.ShowTrail(fromX, fromY, jumpX, jumpY);
    }

    private static IReadOnlyList<string> GetConnectedMonitorNames()
    {
        var screens = System.Windows.Forms.Screen.AllScreens;
        var names = new string[screens.Length];
        for (int i = 0; i < screens.Length; i++) names[i] = screens[i].DeviceName;
        return names;
    }

    /// <summary>
    /// 保存座標から実際にカーソルを置く絶対物理座標を解決する。
    /// MonitorRelativeX/Y が有効ならモニタの現 Bounds + 相対オフセットで再計算する
    /// (PerMonitorV2 + マルチ DPI で SetCursorPos が誤動作する問題を回避)。
    /// 旧データ・モニタ未接続のフォールバックは元の物理絶対座標を使う。
    /// </summary>
    private static (int X, int Y, string Source) ResolveJumpTarget(SavedCoordinate target)
    {
        if (target.MonitorRelativeX >= 0 && target.MonitorRelativeY >= 0
            && !string.IsNullOrEmpty(target.MonitorDeviceName))
        {
            var screen = System.Windows.Forms.Screen.AllScreens
                .FirstOrDefault(s => s.DeviceName == target.MonitorDeviceName);
            if (screen is not null)
            {
                int x = screen.Bounds.Left + target.MonitorRelativeX;
                int y = screen.Bounds.Top + target.MonitorRelativeY;
                return (x, y, "monitor-relative");
            }
        }
        return (target.X, target.Y, "absolute-fallback");
    }

    private void OnNavigateCurrentMonitorRequested(object? sender, MouseHookEventArgs e)
    {
        var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(e.X, e.Y));
        var target = e.Direction == WheelDirection.Up
            ? _coordinateStore.GetPrevInMonitor(screen.DeviceName)
            : _coordinateStore.GetNextInMonitor(screen.DeviceName);
        if (target is null) return;

        var (jumpX, jumpY, _) = ResolveJumpTarget(target);
        CursorService.JumpTo(jumpX, jumpY, _settingsService.Current.JumpStrategy);

        if (_settingsService.Current.ActivateWindowUnderCursorOnJump)
            WindowActivator.Activate(jumpX, jumpY);

        _overlayService.ShowTrail(e.X, e.Y, jumpX, jumpY);
    }

    private void OnDisplayDeleteRequested(object? sender, MouseHookEventArgs e)
    {
        // Set A と Set B を両方表示・両方削除可能にする（色で区別）
        var markerA = ParseColor(_settingsService.Current.MarkerColor, Color.FromRgb(0x00, 0x88, 0xFF));
        var markerB = ParseColor(_settingsService.Current.MarkerColorB, Color.FromRgb(0xFF, 0x88, 0x00));

        _overlayService.ShowCoordinateMarkers(
            new[]
            {
                (_coordinateStore, markerA),
                (_coordinateStoreB, markerB),
            },
            onEnterMode: () =>
            {
                _mouseHookService?.EnterDeleteMode();
                _keyboardHookService?.EnterDeleteMode();
            },
            onExitMode: () =>
            {
                _mouseHookService?.ExitDeleteMode();
                _keyboardHookService?.ExitDeleteMode();
            });
    }

    // ── Set B（独立した第2座標セット） ──

    private void OnSaveRequestedB(object? sender, MouseHookEventArgs e)
    {
        if (!_licenseService.IsPro)
        {
            DebugLog.Write("OnSaveRequestedB: blocked (Set B is Pro-only)");
            return;
        }
        _coordinateStoreB.Add(e.X, e.Y);
        _overlayService.ShowShrinkCircle(e.X, e.Y, _settingsService.Current.SaveCircleColorB);
    }

    private void OnNavigateRequestedB(object? sender, MouseHookEventArgs e)
    {
        if (!_licenseService.IsPro)
        {
            DebugLog.Write("OnNavigateRequestedB: blocked (Set B is Pro-only)");
            return;
        }
        var connected = GetConnectedMonitorNames();
        var target = e.Direction == WheelDirection.Up
            ? _coordinateStoreB.GetPrev(connected)
            : _coordinateStoreB.GetNext(connected);
        if (target is null) return;

        int fromX = e.X;
        int fromY = e.Y;
        var (jumpX, jumpY, source) = ResolveJumpTarget(target);

        if (_settingsService.Current.VerboseLogging)
        {
            DebugLog.Write($"NavigateB before: stored=({target.X},{target.Y}) monitor={target.MonitorDeviceName} rel=({target.MonitorRelativeX},{target.MonitorRelativeY}) jump=({jumpX},{jumpY}) source={source} fromCursor=({fromX},{fromY})");
        }

        CursorService.JumpTo(jumpX, jumpY, _settingsService.Current.JumpStrategy);

        if (_settingsService.Current.VerboseLogging)
        {
            if (NativeMethods.GetCursorPos(out var actual))
                DebugLog.Write($"NavigateB after: actualCursor=({actual.X},{actual.Y}) delta=({actual.X - jumpX},{actual.Y - jumpY})");
        }

        if (_settingsService.Current.ActivateWindowUnderCursorOnJump)
            WindowActivator.Activate(jumpX, jumpY);

        _overlayService.ShowTrail(fromX, fromY, jumpX, jumpY, _settingsService.Current.TrailColorB);
    }

    private static Color ParseColor(string hex, Color fallback)
    {
        try
        {
            var converted = ColorConverter.ConvertFromString(hex);
            if (converted is Color c) return c;
        }
        catch { }
        return fallback;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        try { _memDiagTimer?.Stop(); _memDiagTimer = null; } catch { }

        if (_mouseHookService is not null)
        {
            _mouseHookService.SaveRequested -= OnSaveRequested;
            _mouseHookService.NavigateRequested -= OnNavigateRequested;
            _mouseHookService.NavigateCurrentMonitorRequested -= OnNavigateCurrentMonitorRequested;
            _mouseHookService.DisplayDeleteRequested -= OnDisplayDeleteRequested;
            _mouseHookService.SaveRequestedB -= OnSaveRequestedB;
            _mouseHookService.NavigateRequestedB -= OnNavigateRequestedB;
            _mouseHookService.Dispose();
            _mouseHookService = null;
        }

        if (_keyboardHookService is not null)
        {
            _keyboardHookService.SaveRequested -= OnSaveRequested;
            _keyboardHookService.NavigateRequested -= OnNavigateRequested;
            _keyboardHookService.NavigateCurrentMonitorRequested -= OnNavigateCurrentMonitorRequested;
            _keyboardHookService.DisplayDeleteRequested -= OnDisplayDeleteRequested;
            _keyboardHookService.SaveRequestedB -= OnSaveRequestedB;
            _keyboardHookService.NavigateRequestedB -= OnNavigateRequestedB;
            _keyboardHookService.Dispose();
            _keyboardHookService = null;
        }

        _coordinateStore.Changed -= OnCoordinateStoreAChanged;
        _coordinateStoreB.Changed -= OnCoordinateStoreBChanged;

        try { _overlayService.Dispose(); } catch { }
    }
}
