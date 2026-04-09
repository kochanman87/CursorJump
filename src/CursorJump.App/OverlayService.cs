using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using CursorJump.App.Models;

namespace CursorJump.App;

internal sealed class OverlayService
{
    private readonly SettingsService _settingsService;
    private OverlayWindow? _markerOverlay;

    public OverlayService(SettingsService settingsService)
    {
        _settingsService = settingsService;
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
    /// </summary>
    public void ShowCoordinateMarkers(
        CoordinateStore store,
        Action? onEnterMode,
        Action? onExitMode)
    {
        if (store.Count == 0) return;

        // 既にマーカー表示中なら閉じる
        CloseMarkerOverlay();

        onEnterMode?.Invoke();

        var color = ParseColor(_settingsService.Current.MarkerColor, Colors.DodgerBlue);
        const double markerRadius = 15;
        const double snapDistance = 40;

        var overlay = new OverlayWindow(clickThrough: false);
        // クリック受信のためShowActivatedをtrueに
        overlay.ShowActivated = true;
        overlay.Focusable = true;
        _markerOverlay = overlay;

        DebugLog.Write($"ShowCoordinateMarkers: count={store.Count}");
        overlay.Show();
        DebugLog.Write($"overlay.Show() done, IsActive={overlay.IsActive}");
        overlay.CoverVirtualScreen();
        // Activate/FocusをDispatcher経由で遅延実行（フックコールバック内からのSetForegroundWindow制約を回避）
        overlay.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, () =>
        {
            overlay.Activate();
            DebugLog.Write($"overlay.Activate() done, IsActive={overlay.IsActive}");
            overlay.Focus();
            System.Windows.Input.Keyboard.Focus(overlay);
            var focusedElement = System.Windows.Input.Keyboard.FocusedElement;
            DebugLog.Write($"overlay.Focus() done, IsFocused={overlay.IsFocused}, IsKeyboardFocusWithin={overlay.IsKeyboardFocusWithin}, FocusedElement={focusedElement?.GetType().Name}");
        });

        // 半透明の背景でモード表示を分かりやすくする
        overlay.Background = new SolidColorBrush(Color.FromArgb(40, 0, 0, 0));

        var markers = new List<(Ellipse ellipse, int index, double canvasX, double canvasY)>();

        var coordinates = store.GetAll();
        for (int i = 0; i < coordinates.Count; i++)
        {
            var coord = coordinates[i];
            var pos = overlay.PhysicalToWpf(coord.X, coord.Y);
            double canvasX = pos.X - overlay.Left;
            double canvasY = pos.Y - overlay.Top;

            var ellipse = new Ellipse
            {
                Width = markerRadius * 2,
                Height = markerRadius * 2,
                Fill = new SolidColorBrush(Color.FromArgb(180, color.R, color.G, color.B)),
                Stroke = new SolidColorBrush(Colors.White),
                StrokeThickness = 2,
                Cursor = Cursors.Hand
            };

            Canvas.SetLeft(ellipse, canvasX - markerRadius);
            Canvas.SetTop(ellipse, canvasY - markerRadius);
            overlay.OverlayCanvasElement.Children.Add(ellipse);

            // 番号ラベル
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

            markers.Add((ellipse, i, canvasX, canvasY));
            DebugLog.Write($"Marker[{i}]: physical=({coord.X},{coord.Y}), wpf=({pos.X:F1},{pos.Y:F1}), canvas=({canvasX:F1},{canvasY:F1}), overlayLeft={overlay.Left:F1}, overlayTop={overlay.Top:F1}");
        }

        // ESCキーで終了
        overlay.KeyDown += (_, e) =>
        {
            DebugLog.Write($"KeyDown: Key={e.Key}");
            if (e.Key == Key.Escape)
            {
                DebugLog.Write("ESC detected, closing overlay");
                CloseMarkerOverlay();
                onExitMode?.Invoke();
            }
        };

        // マウス移動時の吸いつき効果
        Ellipse? lastHighlighted = null;
        overlay.MouseMove += (_, e) =>
        {
            var mousePos = e.GetPosition(overlay.OverlayCanvasElement);

            // 前のハイライトをリセット
            if (lastHighlighted is not null)
            {
                lastHighlighted.StrokeThickness = 2;
                lastHighlighted.Stroke = new SolidColorBrush(Colors.White);
                lastHighlighted = null;
            }

            // 最も近いマーカーを探す
            double closestDist = double.MaxValue;
            Ellipse? closestEllipse = null;

            foreach (var (ellipse, _, cx, cy) in markers)
            {
                double dx = mousePos.X - cx;
                double dy = mousePos.Y - cy;
                double dist = Math.Sqrt(dx * dx + dy * dy);
                if (dist < snapDistance && dist < closestDist)
                {
                    closestDist = dist;
                    closestEllipse = ellipse;
                }
            }

            if (closestEllipse is not null)
            {
                closestEllipse.StrokeThickness = 4;
                closestEllipse.Stroke = new SolidColorBrush(Colors.Yellow);
                lastHighlighted = closestEllipse;
            }
        };

        // クリックで削除
        overlay.MouseLeftButtonDown += (_, e) =>
        {
            var mousePos = e.GetPosition(overlay.OverlayCanvasElement);
            DebugLog.Write($"MouseLeftButtonDown: pos=({mousePos.X:F0},{mousePos.Y:F0})");

            // 吸いつき範囲内で最も近いマーカーを探す
            double closestDist = double.MaxValue;
            int closestMarkerIdx = -1;
            int closestListIdx = -1;

            for (int mi = 0; mi < markers.Count; mi++)
            {
                var (_, storeIdx, cx, cy) = markers[mi];
                double dx = mousePos.X - cx;
                double dy = mousePos.Y - cy;
                double dist = Math.Sqrt(dx * dx + dy * dy);
                if (dist < snapDistance && dist < closestDist)
                {
                    closestDist = dist;
                    closestMarkerIdx = mi;
                    closestListIdx = storeIdx;
                }
            }

            DebugLog.Write($"MouseLeftButtonDown: closestDist={closestDist:F1}, closestMarkerIdx={closestMarkerIdx}");
            if (closestMarkerIdx >= 0)
            {
                DebugLog.Write($"Deleting marker index={closestListIdx}");
                // ストアから削除
                store.RemoveAt(closestListIdx);

                // マーカーの表示をリフレッシュ
                RefreshMarkers(overlay, store, markers, color, markerRadius);

                if (store.Count == 0)
                {
                    CloseMarkerOverlay();
                    onExitMode?.Invoke();
                }
            }
        };

        // ウィンドウが閉じられたときのクリーンアップ
        overlay.Closed += (_, _) =>
        {
            if (_markerOverlay == overlay)
                _markerOverlay = null;
        };
    }

    public void CloseMarkerOverlay()
    {
        if (_markerOverlay is not null)
        {
            _markerOverlay.Close();
            _markerOverlay = null;
        }
    }

    private static void RefreshMarkers(
        OverlayWindow overlay,
        CoordinateStore store,
        List<(Ellipse ellipse, int index, double canvasX, double canvasY)> markers,
        Color color,
        double markerRadius)
    {
        overlay.OverlayCanvasElement.Children.Clear();
        markers.Clear();

        var coordinates = store.GetAll();
        for (int i = 0; i < coordinates.Count; i++)
        {
            var coord = coordinates[i];
            var pos = overlay.PhysicalToWpf(coord.X, coord.Y);
            double canvasX = pos.X - overlay.Left;
            double canvasY = pos.Y - overlay.Top;

            var ellipse = new Ellipse
            {
                Width = markerRadius * 2,
                Height = markerRadius * 2,
                Fill = new SolidColorBrush(Color.FromArgb(180, color.R, color.G, color.B)),
                Stroke = new SolidColorBrush(Colors.White),
                StrokeThickness = 2,
                Cursor = Cursors.Hand
            };

            Canvas.SetLeft(ellipse, canvasX - markerRadius);
            Canvas.SetTop(ellipse, canvasY - markerRadius);
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

            markers.Add((ellipse, i, canvasX, canvasY));
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
