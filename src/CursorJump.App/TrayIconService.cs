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
    private bool _disposed;

    public TrayIconService(SettingsService settingsService)
    {
        _settingsService = settingsService;
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
        _notifyIcon.Text = "CursorJump";

        var contextMenu = new ContextMenuStrip();

        var settingsItem = new ToolStripMenuItem("設定");
        settingsItem.Click += HandleSettingsClick;
        contextMenu.Items.Add(settingsItem);

        contextMenu.Items.Add(new ToolStripSeparator());

        var exitItem = new ToolStripMenuItem("終了");
        exitItem.Click += HandleExitClick;
        contextMenu.Items.Add(exitItem);

        _notifyIcon.ContextMenuStrip = contextMenu;
        _notifyIcon.Visible = true;
    }

    private void HandleSettingsClick(object? sender, EventArgs e)
    {
        if (_disposed) return;

        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            var window = new SettingsWindow(_settingsService);
            window.ShowDialog();
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

        var contextMenu = _notifyIcon.ContextMenuStrip;
        if (contextMenu is not null)
        {
            foreach (ToolStripItem item in contextMenu.Items)
            {
                item.Click -= HandleExitClick;
                item.Click -= HandleSettingsClick;
            }
        }

        _notifyIcon.Visible = false;
        _notifyIcon.ContextMenuStrip = null;
        contextMenu?.Dispose();
        _notifyIcon.Dispose();
    }
}
