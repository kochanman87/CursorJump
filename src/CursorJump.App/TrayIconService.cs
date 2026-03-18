using System;
using System.Drawing;
using System.Windows;
using System.Windows.Forms;

namespace CursorJump.App;

/// <summary>
/// 日本語コメント: タスクトレイアイコンの管理を行うサービス
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly string _hotkeyDescription;
    private bool _disposed;

    public TrayIconService(string hotkeyDescription)
    {
        _hotkeyDescription = hotkeyDescription;
        _notifyIcon = new NotifyIcon();
    }

    /// <summary>
    /// 日本語コメント: トレイアイコンを初期化して表示する
    /// </summary>
    public void Initialize()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(TrayIconService));
        }

        // 日本語コメント: 標準アプリケーションアイコンとヒントテキストを設定
        _notifyIcon.Icon = SystemIcons.Application;
        _notifyIcon.Text = $"CursorJump ({_hotkeyDescription})";

        // 日本語コメント: コンテキストメニューを作成
        var contextMenu = new ContextMenuStrip();

        var infoItem = new ToolStripMenuItem($"Jump: {_hotkeyDescription}") { Enabled = false };
        contextMenu.Items.Add(infoItem);
        contextMenu.Items.Add(new ToolStripSeparator());

        var exitItem = new ToolStripMenuItem("終了");
        exitItem.Click += HandleExitClick;
        contextMenu.Items.Add(exitItem);

        _notifyIcon.ContextMenuStrip = contextMenu;

        // 日本語コメント: タスクトレイにアイコンを表示
        _notifyIcon.Visible = true;
    }

    private void HandleExitClick(object? sender, EventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        // 日本語コメント: メニューからの終了要求をUIスレッドで処理
        System.Windows.Application.Current?.Dispatcher.Invoke(() => System.Windows.Application.Current.Shutdown());
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // 日本語コメント: 表示中のアイコンと関連リソースを解放
        var contextMenu = _notifyIcon.ContextMenuStrip;
        if (contextMenu is not null)
        {
            foreach (ToolStripItem item in contextMenu.Items)
            {
                item.Click -= HandleExitClick;
            }
        }

        _notifyIcon.Visible = false;
        _notifyIcon.ContextMenuStrip = null;
        contextMenu?.Dispose();
        _notifyIcon.Dispose();
    }
}
