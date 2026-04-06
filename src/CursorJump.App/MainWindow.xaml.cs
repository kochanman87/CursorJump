using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;

namespace CursorJump.App;

/// <summary>
/// タスクトレイ常駐用の不可視ウィンドウ。グローバルホットキーの受信とマウスフックの管理を担当する。
/// </summary>
public partial class MainWindow : Window
{
    private HotkeyService? _hotkeyService;
    private HwndSource? _hwndSource;
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
        var helper = new WindowInteropHelper(this);
        _hwndSource = HwndSource.FromHwnd(helper.Handle);

        try
        {
            _hotkeyService = new HotkeyService(helper.Handle, _settingsService);
            _hotkeyService.HotkeyPressed += OnHotkeyPressed;
            _hotkeyService.Register();
            HotkeyService = _hotkeyService;

            _hwndSource?.AddHook(_hotkeyService.HandleWindowMessage);
        }
        catch (Win32Exception ex)
        {
            MessageBox.Show(
                $"ホットキーの登録に失敗しました: {ex.Message}",
                "CursorJump",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        try
        {
            _mouseHookService = new MouseHookService(_settingsService);
            _mouseHookService.SaveRequested += OnSaveRequested;
            _mouseHookService.NavigateRequested += OnNavigateRequested;
            _mouseHookService.DisplayDeleteRequested += OnDisplayDeleteRequested;
            _mouseHookService.Install();
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

    private static void OnHotkeyPressed(object? sender, EventArgs e)
    {
        CursorService.JumpToCentreOfCurrentScreen();
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
            onEnterMode: () => _mouseHookService?.Suspend(),
            onExitMode: () => _mouseHookService?.Resume());
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

        if (_hotkeyService is not null)
        {
            _hwndSource?.RemoveHook(_hotkeyService.HandleWindowMessage);
            _hotkeyService.HotkeyPressed -= OnHotkeyPressed;
            _hotkeyService.Dispose();
            _hotkeyService = null;
        }

        _hwndSource?.Dispose();
        _hwndSource = null;
    }

    /// <summary>App.xaml.cs がホットキーの説明文を読み取るために公開する。</summary>
    internal HotkeyService? HotkeyService { get; private set; }
}
