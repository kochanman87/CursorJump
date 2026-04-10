using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace CursorJump.App;

internal sealed class OverlayService
{
    private readonly SettingsService _settingsService;
    private MouseHookService? _mouseHookService;
    private OverlayWindow? _markerOverlay;

    // 削除モード用状態
    private CoordinateStore? _deleteStore;
    private Action? _deleteOnExitMode;
    private List<(Ellipse ellipse, int index, int physX, int physY)> _markers = new();
    private Ellipse? _lastHighlighted;
    private Color _markerColor;
    private const double MarkerRadius = 15;
    private const double SnapDistancePhysical = 40; // 物理ピクセル

    public OverlayService(SettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    /// <summary>
    /// MouseHookServiceの参照を設定する。削除モードのフックイベント購読に必要。
    /// </summary>
    public void SetMouseHookService(MouseHookService hookService)
    {
        _mouseHookService = hookService;
    }

    /// <summary>
    /// 座標保存時の収縮円アニメーション表示。
    /// </summary>
    public void ShowShrinkCircle(int physicalX, int physicalY)
    {
        var color = ParseColor(_settingsService.Current.SaveCircleColor, Colors.Red);
        const double initialRadius = 30;
        const double duration = 400; // ms

        var overlay = new OverlayWindow(clickThrough: true);
        overlay.Show();
        overlay.CoverVirtualScreen();

        var pos = overlay.PhysicalToWpf(physicalX, physicalY);
        // ウィンドウ左上からの相対座標に変換
        double canvasX = pos.X - overlay.Left;
        double canvasY = pos.Y - overlay.Top;

        var ellipse = new Ellipse
        {
            Width = initialRadius * 2,
            Height = initialRadius * 2,
            Fill = new SolidColorBrush(color),
            Opacity = 1.0,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new ScaleTransform(1, 1)
        };

        Canvas.SetLeft(ellipse, canvasX - initialRadius);
        Canvas.SetTop(ellipse, canvasY - initialRadius);
        overlay.OverlayCanvasElement.Children.Add(ellipse);

        var scaleTransform = (ScaleTransform)ellipse.RenderTransform;

        var scaleXAnim = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(duration))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
        };
        var scaleYAnim = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(duration))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
        };
        var opacityAnim = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(duration))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
        };

        opacityAnim.Completed += (_, _) => overlay.Close();

        scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleXAnim);
        scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleYAnim);
        ellipse.BeginAnimation(UIElement.OpacityProperty, opacityAnim);
    }

    /// <summary>
    /// 座標ナビゲーション時の軌跡アニメーション表示。
    /// </summary>
    public void ShowTrail(int fromX, int fromY, int toX, int toY)
    {
        var color = ParseColor(_settingsService.Current.TrailColor, Colors.LimeGreen);
        const double duration = 500; // ms

        var overlay = new OverlayWindow(clickThrough: true);
        overlay.Show();
        overlay.CoverVirtualScreen();

        var fromPos = overlay.PhysicalToWpf(fromX, fromY);
        var toPos = overlay.PhysicalToWpf(toX, toY);

        double offsetX = overlay.Left;
        double offsetY = overlay.Top;

        var line = new Line
        {
            X1 = fromPos.X - offsetX,
            Y1 = fromPos.Y - offsetY,
            X2 = toPos.X - offsetX,
            Y2 = toPos.Y - offsetY,
            Stroke = new SolidColorBrush(color),
            StrokeThickness = 3,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Opacity = 1.0
        };

        overlay.OverlayCanvasElement.Children.Add(line);

        // 移動先に小さい円を表示
        const double targetRadius = 8;
        var targetCircle = new Ellipse
        {
            Width = targetRadius * 2,
            Height = targetRadius * 2,
            Fill = new SolidColorBrush(color),
            Opacity = 1.0
        };
        Canvas.SetLeft(targetCircle, toPos.X - offsetX - targetRadius);
        Canvas.SetTop(targetCircle, toPos.Y - offsetY - targetRadius);
        overlay.OverlayCanvasElement.Children.Add(targetCircle);

        var opacityAnim = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(duration))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
        };
        var circleOpacityAnim = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(duration))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
        };

        opacityAnim.Completed += (_, _) => overlay.Close();

        line.BeginAnimation(UIElement.OpacityProperty, opacityAnim);
        targetCircle.BeginAnimation(UIElement.OpacityProperty, circleOpacityAnim);
    }

    /// <summary>
    /// 座標マーカーを表示し、クリックで削除可能にする。
    /// オーバーレイは表示専用（clickThrough=true）。入力はすべて低レベルマウスフック+
    /// キーボードフックで処理するため、ウィンドウフォーカスに依存しない。
    /// </summary>
    public void ShowCoordinateMarkers(
        CoordinateStore store,
        Action? onEnterMode,
        Action? onExitMode)
    {
        if (store.Count == 0) return;

        // 既にマーカー表示中なら閉じる
        CloseMarkerOverlay();

        _deleteStore = store;
        _deleteOnExitMode = onExitMode;
        _markerColor = ParseColor(_settingsService.Current.MarkerColor, Colors.DodgerBlue);
        _lastHighlighted = null;

        // フックイベント購読
        if (_mouseHookService is not null)
        {
            _mouseHookService.DeleteModeClicked += OnDeleteModeClicked;
            _mouseHookService.DeleteModeMoved += OnDeleteModeMoved;
            _mouseHookService.DeleteModeEscPressed += OnDeleteModeEscPressed;
        }

        onEnterMode?.Invoke();

        // 表示専用オーバーレイ（clickThrough=true、フォーカス不要）
        var overlay = new OverlayWindow(clickThrough: true);
        _markerOverlay = overlay;

        DebugLog.Write($"ShowCoordinateMarkers: count={store.Count}");
        overlay.Show();
        overlay.CoverVirtualScreen();

        // 半透明の背景でモード表示を分かりやすくする
        overlay.Background = new SolidColorBrush(Color.FromArgb(40, 0, 0, 0));

        BuildMarkers(overlay);

        // ウィンドウが閉じられたときのクリーンアップ
        overlay.Closed += (_, _) =>
        {
            if (_markerOverlay == overlay)
                _markerOverlay = null;
        };
    }

    public void CloseMarkerOverlay()
    {
        // フックイベント購読解除
        if (_mouseHookService is not null)
        {
            _mouseHookService.DeleteModeClicked -= OnDeleteModeClicked;
            _mouseHookService.DeleteModeMoved -= OnDeleteModeMoved;
            _mouseHookService.DeleteModeEscPressed -= OnDeleteModeEscPressed;
        }

        _deleteStore = null;
        _lastHighlighted = null;
        _markers.Clear();

        if (_markerOverlay is not null)
        {
            _markerOverlay.Close();
            _markerOverlay = null;
        }
    }

    // ── 削除モードのフックイベントハンドラ ──

    private void OnDeleteModeClicked(object? sender, MouseHookEventArgs e)
    {
        if (_deleteStore is null || _markerOverlay is null) return;

        int closestIdx = FindNearestMarker(e.X, e.Y);
        if (closestIdx < 0) return;

        int storeIdx = _markers[closestIdx].index;
        DebugLog.Write($"Marker clicked via hook: storeIndex={storeIdx}, physical=({e.X},{e.Y})");
        _deleteStore.RemoveAt(storeIdx);

        if (_deleteStore.Count == 0)
        {
            var exitMode = _deleteOnExitMode;
            CloseMarkerOverlay();
            exitMode?.Invoke();
        }
        else
        {
            _lastHighlighted = null;
            BuildMarkers(_markerOverlay);
        }
    }

    private void OnDeleteModeMoved(object? sender, MouseHookEventArgs e)
    {
        if (_markers.Count == 0) return;

        // 前のハイライトをリセット
        if (_lastHighlighted is not null)
        {
            _lastHighlighted.StrokeThickness = 2;
            _lastHighlighted.Stroke = new SolidColorBrush(Colors.White);
            _lastHighlighted = null;
        }

        int closestIdx = FindNearestMarker(e.X, e.Y);
        if (closestIdx >= 0)
        {
            var ellipse = _markers[closestIdx].ellipse;
            ellipse.StrokeThickness = 4;
            ellipse.Stroke = new SolidColorBrush(Colors.Yellow);
            _lastHighlighted = ellipse;
        }
    }

    private void OnDeleteModeEscPressed(object? sender, EventArgs e)
    {
        DebugLog.Write("ESC detected via keyboard hook, closing overlay");
        var exitMode = _deleteOnExitMode;
        CloseMarkerOverlay();
        exitMode?.Invoke();
    }

    /// <summary>
    /// 物理ピクセル座標で最も近いマーカーを検索する。snapDistance内でなければ-1を返す。
    /// </summary>
    private int FindNearestMarker(int physX, int physY)
    {
        double closestDist = double.MaxValue;
        int closestIdx = -1;

        for (int i = 0; i < _markers.Count; i++)
        {
            var (_, _, mx, my) = _markers[i];
            double dx = physX - mx;
            double dy = physY - my;
            double dist = Math.Sqrt(dx * dx + dy * dy);
            if (dist < SnapDistancePhysical && dist < closestDist)
            {
                closestDist = dist;
                closestIdx = i;
            }
        }

        return closestIdx;
    }

    /// <summary>
    /// マーカー要素を構築してCanvasに配置する（表示専用）。
    /// 入力処理は低レベルフックで行うため、WPFイベントハンドラは不要。
    /// </summary>
    private void BuildMarkers(OverlayWindow overlay)
    {
        overlay.OverlayCanvasElement.Children.Clear();
        _markers.Clear();

        if (_deleteStore is null) return;

        var coordinates = _deleteStore.GetAll();
        for (int i = 0; i < coordinates.Count; i++)
        {
            var coord = coordinates[i];
            var pos = overlay.PhysicalToWpf(coord.X, coord.Y);
            double canvasX = pos.X - overlay.Left;
            double canvasY = pos.Y - overlay.Top;

            var ellipse = new Ellipse
            {
                Width = MarkerRadius * 2,
                Height = MarkerRadius * 2,
                Fill = new SolidColorBrush(Color.FromArgb(180, _markerColor.R, _markerColor.G, _markerColor.B)),
                Stroke = new SolidColorBrush(Colors.White),
                StrokeThickness = 2,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(ellipse, canvasX - MarkerRadius);
            Canvas.SetTop(ellipse, canvasY - MarkerRadius);
            overlay.OverlayCanvasElement.Children.Add(ellipse);

            var label = new TextBlock
            {
                Text = (i + 1).ToString(),
                Foreground = Brushes.White,
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                IsHitTestVisible = false
            };
            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(label, canvasX - label.DesiredSize.Width / 2);
            Canvas.SetTop(label, canvasY - label.DesiredSize.Height / 2);
            overlay.OverlayCanvasElement.Children.Add(label);

            _markers.Add((ellipse, i, coord.X, coord.Y));
            DebugLog.Write($"Marker[{i}]: physical=({coord.X},{coord.Y}), canvas=({canvasX:F1},{canvasY:F1})");
        }
    }

    private static Color ParseColor(string hex, Color fallback)
    {
        try
        {
            var converted = ColorConverter.ConvertFromString(hex);
            if (converted is Color c) return c;
        }
        catch { }
        return fallback;
    }
}
