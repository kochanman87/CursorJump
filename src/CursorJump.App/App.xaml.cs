using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using Velopack;

namespace CursorJump.App;

public partial class App : Application
{
    private const string SingleInstanceMutexName = @"Local\CursorJump.App.SingleInstance.{E7B9F3A2-4C5D-4F2A-9B1E-8D4C7A6F3B21}";

    private TrayIconService? _trayIconService;
    private MainWindow? _mainWindow;
    private SettingsService? _settingsService;
    private UpdateService? _updateService;
    private LicenseService? _licenseService;
    private Mutex? _singleInstanceMutex;
    private bool _ownsSingleInstanceMutex;

    public App()
    {
        InitializeComponent();
    }

    internal SettingsService? SettingsService => _settingsService;
    internal UpdateService? UpdateService => _updateService;
    internal LicenseService? LicenseService => _licenseService;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Velopack: --veloapp-* 引数を伴うインストーラ/アップデータ呼び出しを処理して即終了する
        // ためのフック。Mutex 取得より前に必ず実行する（インストーラ呼び出しは別プロセスで
        // 短命に走るため、Mutex 競合させない）。
        VelopackApp.Build().Run();

        _singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out bool createdNew);
        if (!createdNew)
        {
            // 設定読込前なので OS 既定言語で表示する（言語辞書は App.xaml の初期値=日本語）
            MessageBox.Show(
                Loc.Get("Str.AlreadyRunning"),
                Loc.Get("Str.AppName"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown(0);
            return;
        }
        _ownsSingleInstanceMutex = true;

        try
        {
            EnsureWindowsFormsIsInitialized();

            DebugLog.Write($"=== CursorJump starting === [{System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName}]");
            DebugLog.WriteMonitorInfo();

            _settingsService = new SettingsService();
            _settingsService.Load();

            // 起動時にユーザーが保存したテーマと言語を適用
            ThemeManager.Apply(_settingsService.Current.UiTheme);
            LocalizationManager.Apply(_settingsService.Current.UiLanguage);

            _licenseService = new LicenseService(_settingsService);

            _mainWindow = new MainWindow(_settingsService, _licenseService);
            MainWindow = _mainWindow;

            // HWND を強制生成することで SourceInitialized（= MouseHookService の初期化）を同期的に発火させる
            var helper = new WindowInteropHelper(_mainWindow);
            helper.EnsureHandle();

            _updateService = new UpdateService(_settingsService);

            _trayIconService = new TrayIconService(_settingsService, _updateService, _licenseService);
            _trayIconService.Initialize();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, Loc.Get("Str.AppName"), MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(-1);
            return;
        }

        Exit += OnApplicationExit;

        // 起動時の更新チェック（設定 ON のときのみ）。例外は UpdateService 内で握り潰されるので
        // ここでは fire-and-forget で良い。Velopack インストーラ経由でない開発実行では
        // IsInstalled=false となり何も起きない。
        if (_settingsService != null && _settingsService.Current.AutoUpdateEnabled)
        {
            _ = Task.Run(RunStartupUpdateCheckAsync);
        }

        // 自動起動レジストリの同期。Velopack 更新で exe パスが変わったケースに自動追従するため、
        // 設定 ON ならば毎回現在の exe パスで上書きする。設定 OFF なら残留があれば削除する。
        if (_settingsService != null)
        {
            StartupService.SyncWithExePath(_settingsService.Current.AutoStartEnabled);
        }
    }

    private async Task RunStartupUpdateCheckAsync()
    {
        try
        {
            if (_updateService == null) return;
            var (info, status) = await _updateService.CheckForUpdatesAsync();
            if (status != UpdateCheckStatus.UpdateAvailable || info == null) return;

            string newVersion = info.TargetFullRelease.Version.ToString();
            if (_updateService.IsSkipped(newVersion))
            {
                DebugLog.Write($"RunStartupUpdateCheckAsync: version {newVersion} is skipped by user");
                return;
            }

            await Dispatcher.InvokeAsync(() =>
            {
                var dlg = new UpdateDialog(_updateService!, info);
                dlg.ShowDialog();
            });
        }
        catch (Exception ex)
        {
            DebugLog.Write($"RunStartupUpdateCheckAsync failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Exit -= OnApplicationExit;
        DisposeTrayIcon();
        ReleaseSingleInstanceMutex();
        // 非同期キューに残っているログを書き出してから終了する (取りこぼし防止)
        DebugLog.Flush(TimeSpan.FromMilliseconds(500));
        base.OnExit(e);
    }

    private void ReleaseSingleInstanceMutex()
    {
        if (_singleInstanceMutex == null)
        {
            return;
        }

        if (_ownsSingleInstanceMutex)
        {
            try
            {
                _singleInstanceMutex.ReleaseMutex();
            }
            catch
            {
                // 所有権がない / 既にリリース済みの場合は無視
            }
            _ownsSingleInstanceMutex = false;
        }

        _singleInstanceMutex.Dispose();
        _singleInstanceMutex = null;
    }

    private void OnApplicationExit(object? sender, ExitEventArgs e)
    {
        Exit -= OnApplicationExit;
        DisposeTrayIcon();
    }

    private void DisposeTrayIcon()
    {
        _trayIconService?.Dispose();
        _trayIconService = null;
        _mainWindow = null;
    }

    private static void EnsureWindowsFormsIsInitialized()
    {
        if (!System.Windows.Forms.Application.MessageLoop)
        {
            System.Windows.Forms.Application.EnableVisualStyles();
            System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
        }
    }
}
