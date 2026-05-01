using System;
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
    private readonly CoordinateStore _coordinateStore = new();
    private readonly CoordinateStore _coordinateStoreB = new();
    private readonly OverlayService _overlayService;

    public MainWindow(SettingsService settingsService)
    {
        _settingsService = settingsService;
        _overlayService = new OverlayService(settingsService);

        // 永続化された座標を復元
        _coordinateStore.Load(_settingsService.Current.SavedCoordinatesA);
        _coordinateStoreB.Load(_settingsService.Current.SavedCoordinatesB);

        // 変更時に settings.json へ書き戻す
        _coordinateStore.Changed += OnCoordinateStoreAChanged;
        _coordinateStoreB.Changed += OnCoordinateStoreBChanged;

        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
        Closed += OnClosed;
    }

    private void OnCoordinateStoreAChanged()
    {
        _settingsService.Current.SavedCoordinatesA = _coordinateStore.GetAll().ToList();
        if (!_settingsService.Save(_settingsService.Current))
            DebugLog.Write("MainWindow: SavedCoordinatesA persistence failed — memory state diverged from settings.json. Coordinates may be lost on next app restart.");
    }

    private void OnCoordinateStoreBChanged()
    {
        _settingsService.Current.SavedCoordinatesB = _coordinateStoreB.GetAll().ToList();
        if (!_settingsService.Save(_settingsService.Current))
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
    }

    private void OnSaveRequested(object? sender, MouseHookEventArgs e)
    {
        _coordinateStore.Add(e.X, e.Y);
        _overlayService.ShowShrinkCircle(e.X, e.Y);
    }

    private void OnNavigateRequested(object? sender, MouseHookEventArgs e)
    {
        var target = _coordinateStore.GetNext();
        if (target is null) return;

        int fromX = e.X;
        int fromY = e.Y;

        CursorService.JumpTo(target.X, target.Y);
        _overlayService.ShowTrail(fromX, fromY, target.X, target.Y);
    }

    private void OnNavigateCurrentMonitorRequested(object? sender, MouseHookEventArgs e)
    {
        var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(e.X, e.Y));
        var target = _coordinateStore.GetNextInMonitor(screen.DeviceName);
        if (target is null) return;

        CursorService.JumpTo(target.X, target.Y);
        _overlayService.ShowTrail(e.X, e.Y, target.X, target.Y);
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
        _coordinateStoreB.Add(e.X, e.Y);
        _overlayService.ShowShrinkCircle(e.X, e.Y, _settingsService.Current.SaveCircleColorB);
    }

    private void OnNavigateRequestedB(object? sender, MouseHookEventArgs e)
    {
        var target = _coordinateStoreB.GetNext();
        if (target is null) return;

        int fromX = e.X;
        int fromY = e.Y;

        CursorService.JumpTo(target.X, target.Y);
        _overlayService.ShowTrail(fromX, fromY, target.X, target.Y, _settingsService.Current.TrailColorB);
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
    }
}
