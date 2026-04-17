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
    private KeyboardHookService? _keyboardHookService;
    private OverlayWindow? _markerOverlay;

    // 削除モード用状態
    private CoordinateStore? _deleteStore;
    private Action? _deleteOnExitMode;
    private List<(Ellipse ellipse, int index, int physX, int physY)> _markers = new();
    private Ellipse? _lastHighlighted;
    private Color _markerColor;
    private const double MarkerRadius = 15;
    private const double SnapDistancePhysical = 40; // 物理ピクセル

    // ヘルプパネル関連
    private Border? _helpPanel;
    private string _currentMonitorDeviceName = string.Empty;

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
    /// KeyboardHookServiceの参照を設定する。削除モード中のキーボードトリガー購読に必要。
    /// </summary>
    public void SetKeyboardHookService(KeyboardHookService keyboardHookService)
    {
        _keyboardHookService = keyboardHookService;
    }

    /// <summary>
    /// 座標保存時の収縮円アニメーション表示。
    /// </summary>
    public void ShowShrinkCircle(int physicalX, int physicalY)
    {
        if (!_settingsService.Current.SaveEffectEnabled) return;
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
        if (!_settingsService.Current.TrailEffectEnabled) return;
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
        // 既にマーカー表示中なら閉じる
        CloseMarkerOverlay();

        _deleteStore = store;
        _deleteOnExitMode = onExitMode;
        _markerColor = ParseColor(_settingsService.Current.MarkerColor, Colors.DodgerBlue);
        _lastHighlighted = null;

        // マウスフックイベント購読
        if (_mouseHookService is not null)
        {
            _mouseHookService.DeleteModeClicked += OnDeleteModeClicked;
            _mouseHookService.DeleteModeMoved += OnDeleteModeMoved;
            _mouseHookService.DeleteModeEscPressed += OnDeleteModeEscPressed;
            _mouseHookService.DeleteAllConfirmRequested += OnDeleteAllConfirmRequested;
        }

        // キーボードフックイベント購読
        if (_keyboardHookService is not null)
        {
            _keyboardHookService.DeleteModeClicked += OnDeleteModeClicked;
            _keyboardHookService.DeleteAllConfirmRequested += OnDeleteAllConfirmRequested;
            _keyboardHookService.DeleteModeEscPressed += OnDeleteModeEscPressed;
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
        BuildHelpPanel(overlay);

        // ウィンドウが閉じられたときのクリーンアップ
        overlay.Closed += (_, _) =>
        {
            if (_markerOverlay == overlay)
                _markerOverlay = null;
        };
    }

    public void CloseMarkerOverlay()
    {
        _helpPanel = null;
        _currentMonitorDeviceName = string.Empty;

        // マウスフックイベント購読解除
        if (_mouseHookService is not null)
        {
            _mouseHookService.DeleteModeClicked -= OnDeleteModeClicked;
            _mouseHookService.DeleteModeMoved -= OnDeleteModeMoved;
            _mouseHookService.DeleteModeEscPressed -= OnDeleteModeEscPressed;
            _mouseHookService.DeleteAllConfirmRequested -= OnDeleteAllConfirmRequested;
        }

        // キーボードフックイベント購読解除
        if (_keyboardHookService is not null)
        {
            _keyboardHookService.DeleteModeClicked -= OnDeleteModeClicked;
            _keyboardHookService.DeleteAllConfirmRequested -= OnDeleteAllConfirmRequested;
            _keyboardHookService.DeleteModeEscPressed -= OnDeleteModeEscPressed;
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
        if (closestIdx >= 0)
        {
            // マーカー上: 削除
            int storeIdx = _markers[closestIdx].index;
            DebugLog.Write($"Marker clicked via hook: storeIndex={storeIdx}, physical=({e.X},{e.Y}) - removing");
            _deleteStore.RemoveAt(storeIdx);
        }
        else
        {
            // マーカー外: 追加
            DebugLog.Write($"Empty area clicked via hook: physical=({e.X},{e.Y}) - adding");
            _deleteStore.Add(e.X, e.Y);
        }

        _lastHighlighted = null;
        BuildMarkers(_markerOverlay);
    }

    private void OnDeleteModeMoved(object? sender, MouseHookEventArgs e)
    {
        // マーカーのハイライト処理
        if (_markers.Count > 0)
        {
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

        // ヘルプパネルのモニタ追従
        if (_helpPanel is not null && _markerOverlay is not null)
        {
            RepositionHelpPanelIfMonitorChanged(e.X, e.Y);
        }
    }

    private void OnDeleteModeEscPressed(object? sender, EventArgs e)
    {
        DebugLog.Write("ESC detected via keyboard hook, closing overlay");
        var exitMode = _deleteOnExitMode;
        CloseMarkerOverlay();
        exitMode?.Invoke();
    }

    private void OnDeleteAllConfirmRequested(object? sender, MouseHookEventArgs e)
    {
        DebugLog.Write("DeleteAllConfirmRequested: executing clear all immediately");
        ExecuteClearAll();
    }

    // ── ヘルプパネル構築・配置 ──

    private void BuildHelpPanel(OverlayWindow overlay)
    {
        if (!_settingsService.Current.ShowDeleteModeHelp) return;

        var settings = _settingsService.Current;

        var stack = new StackPanel { Margin = new Thickness(4) };

        // タイトル
        stack.Children.Add(new TextBlock
        {
            Text = "保存座標削除モード",
            Foreground = Brushes.White,
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            IsHitTestVisible = false
        });

        // セパレータ
        stack.Children.Add(new Border
        {
            Height = 1,
            Background = new SolidColorBrush(Color.FromArgb(100, 255, 255, 255)),
            Margin = new Thickness(0, 6, 0, 6),
            IsHitTestVisible = false
        });

        // Save ショートカット行
        stack.Children.Add(BuildHelpLine(
            $"[{ShortcutFormatter.Format(settings.SaveShortcut)}]",
            "追加/削除"));

        // DisplayDelete ショートカット行
        stack.Children.Add(BuildHelpLine(
            $"[{ShortcutFormatter.Format(settings.DisplayDeleteShortcut)}]",
            "全削除"));

        // Navigate ショートカット行
        stack.Children.Add(BuildHelpLine(
            $"[{ShortcutFormatter.Format(settings.NavigateShortcut)}]",
            "終了"));

        // ESC 行
        stack.Children.Add(BuildHelpLine("[ESC]", "終了"));

        _helpPanel = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x1A, 0x1A, 0x1A)),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            IsHitTestVisible = false,
            Child = stack
        };

        overlay.OverlayCanvasElement.Children.Add(_helpPanel);
        overlay.UpdateLayout();

        // カーソル位置から最遠の隅にパネルを配置
        NativeMethods.GetCursorPos(out var pt);
        _currentMonitorDeviceName = System.Windows.Forms.Screen
            .FromPoint(new System.Drawing.Point(pt.X, pt.Y)).DeviceName;
        PositionHelpPanel(overlay, _helpPanel, pt.X, pt.Y);
    }

    private static UIElement BuildHelpLine(string key, string description)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 2, 0, 2),
            IsHitTestVisible = false
        };
        panel.Children.Add(new TextBlock
        {
            Text = key,
            Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xCC, 0x44)),
            FontSize = 12,
            FontFamily = new FontFamily("Consolas, Courier New"),
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        });
        panel.Children.Add(new TextBlock
        {
            Text = $"  {description}",
            Foreground = new SolidColorBrush(Colors.LightGray),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        });
        return panel;
    }

    private void RepositionHelpPanelIfMonitorChanged(int physX, int physY)
    {
        if (_helpPanel is null || _markerOverlay is null) return;

        var screen = System.Windows.Forms.Screen
            .FromPoint(new System.Drawing.Point(physX, physY));
        if (screen.DeviceName == _currentMonitorDeviceName) return;

        _currentMonitorDeviceName = screen.DeviceName;
        DebugLog.Write($"HelpPanel: monitor changed to {screen.DeviceName}, repositioning");
        PositionHelpPanel(_markerOverlay, _helpPanel, physX, physY);
    }

    private static void PositionHelpPanel(OverlayWindow overlay, Border panel, int physX, int physY)
    {
        var screen = System.Windows.Forms.Screen
            .FromPoint(new System.Drawing.Point(physX, physY));
        var wa = screen.WorkingArea; // 物理ピクセル

        // 4隅（物理ピクセル）
        (int x, int y)[] corners =
        {
            (wa.Left, wa.Top),
            (wa.Right, wa.Top),
            (wa.Left, wa.Bottom),
            (wa.Right, wa.Bottom)
        };

        // カーソルから最遠の隅を選択
        int farthestIdx = 0;
        double maxDistSq = -1;
        for (int i = 0; i < corners.Length; i++)
        {
            double dx = physX - corners[i].x;
            double dy = physY - corners[i].y;
            double distSq = dx * dx + dy * dy;
            if (distSq > maxDistSq)
            {
                maxDistSq = distSq;
                farthestIdx = i;
            }
        }

        var (cx, cy) = corners[farthestIdx];
        bool isRight  = cx == wa.Right;
        bool isBottom = cy == wa.Bottom;

        const double padding = 24;
        var cornerDip = overlay.PhysicalToWpf(cx, cy);

        double panelW = panel.ActualWidth  > 0 ? panel.ActualWidth  : 200;
        double panelH = panel.ActualHeight > 0 ? panel.ActualHeight : 100;

        double left = isRight
            ? (cornerDip.X - overlay.Left) - panelW - padding
            : (cornerDip.X - overlay.Left) + padding;

        double top = isBottom
            ? (cornerDip.Y - overlay.Top) - panelH - padding
            : (cornerDip.Y - overlay.Top) + padding;

        Canvas.SetLeft(panel, left);
        Canvas.SetTop(panel, top);
    }

    private void ExecuteClearAll()
    {
        if (_deleteStore is null || _markerOverlay is null) return;

        DebugLog.Write($"ExecuteClearAll: clearing {_deleteStore.Count} coordinates");

        // 削除前の座標リストを保存（収縮円アニメ用）
        var positions = new List<(int X, int Y)>();
        foreach (var coord in _deleteStore.GetAll())
            positions.Add((coord.X, coord.Y));

        // 全削除
        _deleteStore.Clear();

        // マーカー再描画（全消去）
        _lastHighlighted = null;
        BuildMarkers(_markerOverlay);

        // 全座標の収縮円アニメーションを一括表示
        if (positions.Count > 0)
            ShowClearAllShrinkCircles(positions);
    }

    /// <summary>
    /// 複数座標の収縮円アニメーションを1枚のOverlayWindowに集約して表示する（GC負荷回避）。
    /// </summary>
    private void ShowClearAllShrinkCircles(List<(int X, int Y)> positions)
    {
        if (!_settingsService.Current.SaveEffectEnabled) return;

        var color = ParseColor(_settingsService.Current.SaveCircleColor, Colors.Red);
        const double initialRadius = 30;
        const double duration = 500; // ms

        var overlay = new OverlayWindow(clickThrough: true);
        overlay.Show();
        overlay.CoverVirtualScreen();

        int remaining = positions.Count;

        for (int i = 0; i < positions.Count; i++)
        {
            var (physX, physY) = positions[i];
            var pos = overlay.PhysicalToWpf(physX, physY);
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

            // 各アニメに少しずつ遅延を付ける（視覚的な広がり感）
            double delayMs = i * 20.0;
            var scaleXAnim = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(duration))
            {
                BeginTime = TimeSpan.FromMilliseconds(delayMs),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };
            var scaleYAnim = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(duration))
            {
                BeginTime = TimeSpan.FromMilliseconds(delayMs),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };
            var opacityAnim = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(duration))
            {
                BeginTime = TimeSpan.FromMilliseconds(delayMs),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };

            // 最後のアニメが完了したらウィンドウを閉じる
            opacityAnim.Completed += (_, _) =>
            {
                remaining--;
                if (remaining <= 0)
                    overlay.Close();
            };

            scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleXAnim);
            scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleYAnim);
            ellipse.BeginAnimation(UIElement.OpacityProperty, opacityAnim);
        }
    }

    // ── マーカー構築 ──

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
    /// ヘルプパネルは Children.Clear() 後に再追加して保持する。
    /// 入力処理は低レベルフックで行うため、WPFイベントハンドラは不要。
    /// </summary>
    private void BuildMarkers(OverlayWindow overlay)
    {
        // ヘルプパネルを退避（Children.Clear() で消えるため）
        var savedHelpPanel = _helpPanel;

        overlay.OverlayCanvasElement.Children.Clear();
        _markers.Clear();

        if (_deleteStore is null)
        {
            if (savedHelpPanel is not null)
                overlay.OverlayCanvasElement.Children.Add(savedHelpPanel);
            return;
        }

        // マーカーエフェクトが無効なら描画をスキップ（モード自体は継続 = ESC/クリック追加は動作）
        if (_settingsService.Current.MarkerEffectEnabled)
        {
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

        // ヘルプパネルをCanvasの最前面に再追加
        if (savedHelpPanel is not null)
            overlay.OverlayCanvasElement.Children.Add(savedHelpPanel);
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
