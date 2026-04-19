using System;
using System.Threading;
using System.Windows;
using System.Windows.Interop;

namespace CursorJump.App;

public partial class App : Application
{
    private const string SingleInstanceMutexName = @"Local\CursorJump.App.SingleInstance.{E7B9F3A2-4C5D-4F2A-9B1E-8D4C7A6F3B21}";

    private TrayIconService? _trayIconService;
    private MainWindow? _mainWindow;
    private SettingsService? _settingsService;
    private Mutex? _singleInstanceMutex;
    private bool _ownsSingleInstanceMutex;

    public App()
    {
        InitializeComponent();
    }

    internal SettingsService? SettingsService => _settingsService;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show(
                "CursorJump はすでに起動中です。タスクトレイを確認してください。",
                "CursorJump",
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

            // 起動時にユーザーが保存したテーマを適用
            ThemeManager.Apply(_settingsService.Current.UiTheme);

            _mainWindow = new MainWindow(_settingsService);
            MainWindow = _mainWindow;

            // HWND を強制生成することで SourceInitialized（= MouseHookService の初期化）を同期的に発火させる
            var helper = new WindowInteropHelper(_mainWindow);
            helper.EnsureHandle();

            _trayIconService = new TrayIconService(_settingsService);
            _trayIconService.Initialize();
        }
        catch (Exception ex)
        {
            const string title = "CursorJump";
            MessageBox.Show(ex.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(-1);
            return;
        }

        Exit += OnApplicationExit;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Exit -= OnApplicationExit;
        DisposeTrayIcon();
        ReleaseSingleInstanceMutex();
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
