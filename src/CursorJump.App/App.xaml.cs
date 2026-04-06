using System;
using System.Windows;
using System.Windows.Interop;

namespace CursorJump.App;

public partial class App : Application
{
    private TrayIconService? _trayIconService;
    private MainWindow? _mainWindow;
    private SettingsService? _settingsService;

    public App()
    {
        InitializeComponent();
    }

    internal SettingsService? SettingsService => _settingsService;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            EnsureWindowsFormsIsInitialized();

            _settingsService = new SettingsService();
            _settingsService.Load();

            _mainWindow = new MainWindow(_settingsService);
            MainWindow = _mainWindow;

            // HWND を強制生成することで SourceInitialized（= HotkeyService の初期化）を同期的に発火させる
            var helper = new WindowInteropHelper(_mainWindow);
            helper.EnsureHandle();

            string hotkeyDescription = _mainWindow.HotkeyService?.HotkeyDescription ?? "Ctrl+Alt+Home";

            _trayIconService = new TrayIconService(hotkeyDescription, _settingsService);
            _trayIconService.Initialize();

            _settingsService.SettingsChanged += OnSettingsChanged;
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
        base.OnExit(e);
    }

    private void OnApplicationExit(object? sender, ExitEventArgs e)
    {
        Exit -= OnApplicationExit;
        DisposeTrayIcon();
    }

    private void OnSettingsChanged()
    {
        // ホットキーを新しい設定で再登録
        try
        {
            _mainWindow?.HotkeyService?.Reregister();
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            MessageBox.Show(
                $"ホットキーの再登録に失敗しました: {ex.Message}",
                "CursorJump",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
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
