using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace CursorJump.App;

public partial class OverlayWindow : Window
{
    private bool _clickThrough;

    public OverlayWindow(bool clickThrough = true)
    {
        InitializeComponent();
        _clickThrough = clickThrough;
        SourceInitialized += OnSourceInitialized;
    }

    internal Canvas OverlayCanvasElement => (Canvas)FindName("OverlayCanvas");

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;

        int exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
        exStyle |= NativeMethods.WS_EX_TOOLWINDOW; // Alt+Tabに表示しない

        if (_clickThrough)
        {
            exStyle |= NativeMethods.WS_EX_TRANSPARENT; // クリックスルー
        }

        NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, exStyle);
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
    /// </summary>
    internal void CoverVirtualScreen()
    {
        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget is null)
        {
            Left = SystemParameters.VirtualScreenLeft;
            Top = SystemParameters.VirtualScreenTop;
            Width = SystemParameters.VirtualScreenWidth;
            Height = SystemParameters.VirtualScreenHeight;
            return;
        }

        var transform = source.CompositionTarget.TransformFromDevice;
        var topLeft = transform.Transform(new Point(
            SystemParameters.VirtualScreenLeft,
            SystemParameters.VirtualScreenTop));
        var size = transform.Transform(new Point(
            SystemParameters.VirtualScreenWidth,
            SystemParameters.VirtualScreenHeight));

        Left = topLeft.X;
        Top = topLeft.Y;
        Width = size.X;
        Height = size.Y;
    }
}
