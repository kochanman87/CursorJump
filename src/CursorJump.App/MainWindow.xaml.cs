using System;
using System.Windows;

namespace CursorJump.App;

/// <summary>
/// 日本語コメント: 非表示のメインウィンドウでタスクトレイ管理を行う
/// </summary>
public partial class MainWindow : Window
{
    private readonly TrayIconService _trayIconService = new();

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            // 日本語コメント: トレイアイコンを初期化して表示する
            _trayIconService.Initialize();
            Loaded -= OnLoaded;
        }
        catch (Exception ex)
        {
            // 日本語コメント: 初期化失敗をユーザーに通知してアプリを終了する
            MessageBox.Show(
                ex.Message,
                "CursorJump",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            System.Windows.Application.Current.Shutdown(-1);
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        // 日本語コメント: アプリ終了時にトレイアイコンを破棄する
        _trayIconService.Dispose();
        Closed -= OnClosed;
    }
}
