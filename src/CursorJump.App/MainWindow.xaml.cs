using System;
using System.ComponentModel;
using System.Windows;

namespace CursorJump.App;

/// <summary>
/// タスクトレイ常駐用の不可視ウィンドウ。マウスフックの管理を担当する。
/// </summary>
public partial class MainWindow : Window
{
    private MouseHookService? _mouseHookService;
    private readonly SettingsService _settingsService;
    private readonly CoordinateStore _coordinateStore = new();
    private readonly OverlayService _overlayService;

    public MainWindow(SettingsService settingsService)
    {
        _settingsService = settingsService;
        _overlayService = new OverlayService(settingsService);

        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
        Closed += OnClosed;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        try
        {
            _mouseHookService = new MouseHookService(_settingsService);
            _mouseHookService.SaveRequested += OnSaveRequested;
            _mouseHookService.NavigateRequested += OnNavigateRequested;
            _mouseHookService.DisplayDeleteRequested += OnDisplayDeleteRequested;
            _mouseHookService.Install();

            _overlayService.SetMouseHookService(_mouseHookService);
        }
        catch (Win32Exception ex)
        {
            MessageBox.Show(
                $"マウスフックの登録に失敗しました: {ex.Message}",
                "CursorJump",
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

    private void OnDisplayDeleteRequested(object? sender, MouseHookEventArgs e)
    {
        _overlayService.ShowCoordinateMarkers(
            _coordinateStore,
            onEnterMode: () => _mouseHookService?.EnterDeleteMode(),
            onExitMode: () => _mouseHookService?.ExitDeleteMode());
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (_mouseHookService is not null)
        {
            _mouseHookService.SaveRequested -= OnSaveRequested;
            _mouseHookService.NavigateRequested -= OnNavigateRequested;
            _mouseHookService.DisplayDeleteRequested -= OnDisplayDeleteRequested;
            _mouseHookService.Dispose();
            _mouseHookService = null;
        }
    }
}
