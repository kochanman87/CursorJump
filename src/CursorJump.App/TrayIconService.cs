using System;
using System.Drawing;
using System.Windows;
using System.Windows.Forms;

namespace CursorJump.App;

/// <summary>
/// タスクトレイアイコンの管理を行うサービス
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly SettingsService _settingsService;
    private readonly UpdateService _updateService;
    private ToolStripMenuItem? _settingsItem;
    private ToolStripMenuItem? _checkUpdatesItem;
    private ToolStripMenuItem? _exitItem;
    private bool _disposed;

    public TrayIconService(SettingsService settingsService, UpdateService updateService)
    {
        _settingsService = settingsService;
        _updateService = updateService;
        _notifyIcon = new NotifyIcon();
    }

    /// <summary>
    /// トレイアイコンを初期化して表示する
    /// </summary>
    public void Initialize()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(TrayIconService));
        }

        var iconUri = new Uri("pack://application:,,,/Assets/icon.ico", UriKind.Absolute);
        var iconStream = System.Windows.Application.GetResourceStream(iconUri)?.Stream;
        _notifyIcon.Icon = iconStream is not null ? new Icon(iconStream) : SystemIcons.Application;
        _notifyIcon.Text = Loc.Get("Str.AppName");

        var contextMenu = new ContextMenuStrip();

        _settingsItem = new ToolStripMenuItem(Loc.Get("Str.Tray.Settings"));
        _settingsItem.Click += HandleSettingsClick;
        contextMenu.Items.Add(_settingsItem);

        _checkUpdatesItem = new ToolStripMenuItem(Loc.Get("Str.Tray.CheckForUpdates"));
        _checkUpdatesItem.Click += HandleCheckUpdatesClick;
        contextMenu.Items.Add(_checkUpdatesItem);

        contextMenu.Items.Add(new ToolStripSeparator());

        _exitItem = new ToolStripMenuItem(Loc.Get("Str.Tray.Exit"));
        _exitItem.Click += HandleExitClick;
        contextMenu.Items.Add(_exitItem);

        _notifyIcon.ContextMenuStrip = contextMenu;
        _notifyIcon.Visible = true;

        LocalizationManager.LanguageChanged += OnLanguageChanged;
    }

    private void OnLanguageChanged()
    {
        if (_disposed) return;
        // メニュー項目のテキストを現在言語に更新
        _notifyIcon.Text = Loc.Get("Str.AppName");
        if (_settingsItem is not null) _settingsItem.Text = Loc.Get("Str.Tray.Settings");
        if (_checkUpdatesItem is not null) _checkUpdatesItem.Text = Loc.Get("Str.Tray.CheckForUpdates");
        if (_exitItem is not null) _exitItem.Text = Loc.Get("Str.Tray.Exit");
    }

    private void HandleSettingsClick(object? sender, EventArgs e)
    {
        if (_disposed) return;

        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            var window = new SettingsWindow(_settingsService, _updateService);
            window.ShowDialog();
        });
    }

    private async void HandleCheckUpdatesClick(object? sender, EventArgs e)
    {
        if (_disposed) return;

        var info = await _updateService.CheckForUpdatesAsync();

        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            if (info == null)
            {
                System.Windows.MessageBox.Show(
                    Loc.Get("Str.Update.NoUpdate"),
                    Loc.Get("Str.AppName"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var dlg = new UpdateDialog(_updateService, info);
            dlg.ShowDialog();
        });
    }

    private void HandleExitClick(object? sender, EventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        System.Windows.Application.Current?.Dispatcher.Invoke(() => System.Windows.Application.Current.Shutdown());
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        LocalizationManager.LanguageChanged -= OnLanguageChanged;

        var contextMenu = _notifyIcon.ContextMenuStrip;
        if (contextMenu is not null)
        {
            foreach (ToolStripItem item in contextMenu.Items)
            {
                item.Click -= HandleExitClick;
                item.Click -= HandleSettingsClick;
                item.Click -= HandleCheckUpdatesClick;
            }
        }

        _notifyIcon.Visible = false;
        _notifyIcon.ContextMenuStrip = null;
        contextMenu?.Dispose();
        _notifyIcon.Dispose();
    }
}
