using System.Windows;

namespace CursorJump.App;

public partial class App : Application
{
    private TrayIconService? _trayIconService;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 日本語コメント: トレイアイコンを初期化して常駐させる
        _trayIconService = new TrayIconService();
        _trayIconService.Initialize();

        // 日本語コメント: アプリケーション終了時にトレイアイコンを確実に破棄する
        Exit += OnApplicationExit;
    }

    private void OnApplicationExit(object? sender, ExitEventArgs e)
    {
        _trayIconService?.Dispose();
        Exit -= OnApplicationExit;
    }
}
