using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using CursorJump.App.Models;

namespace CursorJump.App;

/// <summary>短命なクリック履歴の表示専用オーバーレイ。HWND は再利用し、変更時だけ描画する。</summary>
internal sealed class ClickHistoryOverlayService : IDisposable
{
    private readonly SettingsService _settings;
    private readonly LicenseService _license;
    private readonly ClickHistory _history;
    private readonly DispatcherTimer _timer;
    private OverlayWindow? _overlay;
    private ClickHistoryMarker[] _lastMarkers = Array.Empty<ClickHistoryMarker>();
    private string? _lastColor;
    private double _lastOpacity;
    private bool _layoutDirty = true;
    private bool _suppressed;
    private bool _disposed;

    public ClickHistoryOverlayService(SettingsService settings, LicenseService license, ClickHistory history)
    {
        _settings = settings;
        _license = license;
        _history = history;
        // 通常のカーソル移動でも「今いる位置をスキップ」の予告を更新する。
        // フック内に描画処理を加えず、UI スレッドで最大 20 回/秒確認する。
        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(50)
        };
        _timer.Tick += OnTick;
    }

    public void Start() => _timer.Start();

    public void SetSuppressed(bool suppressed)
    {
        _suppressed = suppressed;
        Refresh();
    }

    private void OnTick(object? sender, EventArgs e) => Refresh();

    public void Refresh()
    {
        if (_disposed) return;
        try
        {
            var settings = _settings.Current;
            if (_suppressed || !_license.IsPro || !settings.ClickHistoryMarkersEnabled
                || settings.ClickHistoryBackShortcut.EnabledTriggers == TriggerType.None)
            {
                Hide();
                return;
            }

            if (!NativeMethods.GetCursorPos(out var cursor)) return;
            var markers = _history.GetMarkers(settings.ClickHistoryDepth, cursor.X, cursor.Y, ClickHistory.SkipRadiusPx);
            if (markers.Length == 0)
            {
                Hide();
                return;
            }

            var overlay = EnsureOverlay();
            double opacity = double.IsFinite(settings.ClickHistoryMarkerOpacity)
                ? Math.Clamp(settings.ClickHistoryMarkerOpacity, 0.1, 1.0) : 0.35;
            if (!_layoutDirty && _lastColor == settings.ClickHistoryMarkerColor
                && _lastOpacity == opacity && _lastMarkers.SequenceEqual(markers)) return;

            Draw(overlay, markers, ParseColor(settings.ClickHistoryMarkerColor), opacity);
            _lastMarkers = markers;
            _lastColor = settings.ClickHistoryMarkerColor;
            _lastOpacity = opacity;
            _layoutDirty = false;
            overlay.RaiseToTopmost();
        }
        catch (Exception ex)
        {
            // 表示の失敗でフックやカーソル移動を停止させない。
            DebugLog.Write($"ClickHistoryOverlay.Refresh failed: {ex.Message}");
            Hide();
        }
    }

    private OverlayWindow EnsureOverlay()
    {
        if (_overlay is null)
        {
            _overlay = new OverlayWindow(clickThrough: true);
            _overlay.SizeChanged += (_, _) => _layoutDirty = true;
            _overlay.LocationChanged += (_, _) => _layoutDirty = true;
            _overlay.DpiChanged += (_, _) => _layoutDirty = true;
            _overlay.Closed += (_, _) => { _overlay = null; _layoutDirty = true; };
            _overlay.Show();
            _overlay.CoverVirtualScreen();
            _overlay.TrackDisplaySettingsChanges();
            DebugLog.Write("ClickHistoryOverlay: created reusable click-through overlay");
        }
        else if (!_overlay.IsVisible)
        {
            _overlay.Show();
            _layoutDirty = true;
        }
        return _overlay;
    }

    private static void Draw(OverlayWindow overlay, ClickHistoryMarker[] markers, Color color, double opacity)
    {
        var canvas = overlay.OverlayCanvasElement;
        canvas.Children.Clear();
        var fill = new SolidColorBrush(color);
        fill.Freeze();
        var labelBrush = color.R * 0.299 + color.G * 0.587 + color.B * 0.114 > 160
            ? Brushes.Black : Brushes.White;

        // 古い点から描いて、重なった場合は最新の番号を手前にする。次の移動先は最前面。
        foreach (var marker in markers.Reverse().OrderBy(m => m.IsNext))
        {
            const double size = 34;
            var visual = new Grid
            {
                Width = size,
                Height = size,
                Opacity = opacity,
                IsHitTestVisible = false
            };
            visual.Children.Add(new Ellipse
            {
                Margin = new Thickness(4),
                Fill = fill,
                Stroke = Brushes.White,
                StrokeThickness = 1
            });
            if (marker.IsNext)
            {
                visual.Children.Add(new Ellipse
                {
                    Stroke = fill,
                    StrokeThickness = 2.5
                });
            }
            visual.Children.Add(new TextBlock
            {
                Text = marker.Number.ToString(),
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = labelBrush,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            });

            // 画面の物理ピクセル → 実 HWND 原点からの DIP。負座標・混在 DPI でも二重変換しない。
            var point = overlay.PointFromScreen(new Point(marker.X, marker.Y));
            Canvas.SetLeft(visual, point.X - size / 2);
            Canvas.SetTop(visual, point.Y - size / 2);
            canvas.Children.Add(visual);
        }
    }

    private void Hide()
    {
        if (_overlay?.IsVisible == true) _overlay.Hide();
        _layoutDirty = true;
    }

    private static Color ParseColor(string? value)
    {
        try
        {
            if (ColorConverter.ConvertFromString(value ?? "") is Color color)
                return Color.FromRgb(color.R, color.G, color.B);
        }
        catch { }
        return Color.FromRgb(0x5B, 0xA8, 0xF0);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
        _timer.Tick -= OnTick;
        _overlay?.Close();
    }
}
