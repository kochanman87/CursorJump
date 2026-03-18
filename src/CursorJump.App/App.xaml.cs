using System;
using System.Windows;
using System.Windows.Interop;

namespace CursorJump.App;

public partial class App : Application
{
    private TrayIconService? _trayIconService;
    private MainWindow? _mainWindow;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            EnsureWindowsFormsIsInitialized();

            _mainWindow = new MainWindow();
            MainWindow = _mainWindow;

            // HWND を強制生成することで SourceInitialized（= HotkeyService の初期化）を同期的に発火させる
            var helper = new WindowInteropHelper(_mainWindow);
            helper.EnsureHandle();

            string hotkeyDescription = _mainWindow.HotkeyService?.HotkeyDescription ?? "Ctrl+Alt+Home";

            _trayIconService = new TrayIconService(hotkeyDescription);
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
        base.OnExit(e);
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
