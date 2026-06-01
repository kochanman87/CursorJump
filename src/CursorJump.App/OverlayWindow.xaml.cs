using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace CursorJump.App;

public partial class OverlayWindow : Window
{
    private bool _clickThrough;
    private bool _displaySettingsHooked;

    public OverlayWindow(bool clickThrough = true)
    {
        InitializeComponent();
        _clickThrough = clickThrough;
        Focusable = false;
        SourceInitialized += OnSourceInitialized;
        DpiChanged += OnDpiChanged;
        Closed += OnClosed;
    }

    internal Canvas OverlayCanvasElement => (Canvas)FindName("OverlayCanvas");

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;

        int exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
        exStyle |= NativeMethods.WS_EX_TOOLWINDOW;  // Alt+Tabに表示しない
        exStyle |= NativeMethods.WS_EX_NOACTIVATE;  // Show 時のアクティベート/フォーカス同期を抑止 (砂時計対策)

        if (_clickThrough)
        {
            exStyle |= NativeMethods.WS_EX_TRANSPARENT; // クリックスルー
        }

        NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, exStyle);
        DebugLog.Write($"OverlayWindow: exStyle=0x{exStyle:X8}, clickThrough={_clickThrough}");
    }

    private static void OnDpiChanged(object sender, DpiChangedEventArgs e)
    {
        DebugLog.Write($"OverlayWindow.DpiChanged: old={e.OldDpi.PixelsPerInchX}x{e.OldDpi.PixelsPerInchY}, new={e.NewDpi.PixelsPerInchX}x{e.NewDpi.PixelsPerInchY}");
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (_displaySettingsHooked)
        {
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            _displaySettingsHooked = false;
        }
    }

    /// <summary>
    /// このウィンドウを最前面（topmost バンドの先頭）へ再昇格する。
    /// WPF の Topmost プロパティは WS_EX_TOPMOST スタイルビットを示すだけで、
    /// 起動時に 1 回 Topmost=true した常駐窓は、他の最前面窓（削除モードの marker overlay、
    /// 他アプリの全画面・通知等）が後から上に乗ると OS の重なり順では沈む。
    /// その状態でも IsVisible/Topmost は true のままなので「軌跡が突然消える」(v1.7.5 で修正)。
    /// 描画前に本メソッドで重なり順だけを最前面へ戻す。SWP_NOACTIVATE でフォーカスは奪わない
    /// （WS_EX_NOACTIVATE 方針と整合）。HWND 未生成時は何もしない。
    /// </summary>
    internal bool RaiseToTopmost()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return false;
            return NativeMethods.SetWindowPos(
                hwnd,
                NativeMethods.HWND_TOPMOST,
                0, 0, 0, 0,
                NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOMOVE
                    | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOOWNERZORDER);
        }
        catch (Exception ex)
        {
            DebugLog.Write($"OverlayWindow.RaiseToTopmost failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 物理ピクセル座標をこのウィンドウのWPF DIP座標に変換する。
    /// </summary>
    internal Point PhysicalToWpf(int physicalX, int physicalY)
    {
        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget is null)
            return new Point(physicalX, physicalY);

        var transform = source.CompositionTarget.TransformFromDevice;
        return transform.Transform(new Point(physicalX, physicalY));
    }

    /// <summary>
    /// 仮想スクリーン全体をカバーするようにウィンドウを配置する。
    /// SystemParameters.VirtualScreen* は既にDIP値のため、TransformFromDevice不要。
    /// </summary>
    internal void CoverVirtualScreen()
    {
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;
    }

    /// <summary>
    /// シングルトン用途: モニタ抜き差しで仮想デスクトップサイズが変わったら自動で再カバーする。
    /// </summary>
    internal void TrackDisplaySettingsChanges()
    {
        if (_displaySettingsHooked) return;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        _displaySettingsHooked = true;
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        try
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                CoverVirtualScreen();
                DebugLog.Write($"OverlayWindow.DisplaySettingsChanged: re-covered virtual screen ({Width}x{Height})");
            }));
        }
        catch { }
    }
}
