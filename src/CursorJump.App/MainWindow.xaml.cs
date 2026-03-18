using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;

namespace CursorJump.App;

/// <summary>
/// 日本語コメント: タスクトレイ常駐用の不可視ウィンドウ。グローバルホットキーの受信も担当する。
/// </summary>
public partial class MainWindow : Window
{
    private HotkeyService? _hotkeyService;
    private HwndSource? _hwndSource;

    public MainWindow()
    {
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
            _hotkeyService = new HotkeyService(helper.Handle);
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
    }

    private static void OnHotkeyPressed(object? sender, EventArgs e)
    {
        CursorService.JumpToCentreOfCurrentScreen();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
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
