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

    // 削除モード用状態（複数ストア対応）
    private List<(CoordinateStore store, Color color)> _deleteStores = new();
    private Action? _deleteOnExitMode;
    private List<(Ellipse ellipse, CoordinateStore store, int indexInStore, int physX, int physY)> _markers = new();
    private Ellipse? _lastHighlighted;
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
    public void ShowShrinkCircle(int physicalX, int physicalY, string? colorOverride = null)
    {
        if (!_settingsService.Current.SaveEffectEnabled) return;
        var color = ParseColor(colorOverride ?? _settingsService.Current.SaveCircleColor, Colors.Red);
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
    /// 軌跡を N セグメントに分割し、移動元側（遠端）から段階的にフェードアウトする。
    /// </summary>
    public void ShowTrail(int fromX, int fromY, int toX, int toY, string? colorOverride = null)
    {
        var settings = _settingsService.Current;
        if (!settings.TrailEffectEnabled) return;

        var color = ParseColor(colorOverride ?? settings.TrailColor, Colors.LimeGreen);

        // 設定値クランプ（範囲外の値が settings.json に入っていても安全に）
        double thickness = Math.Clamp(settings.TrailThickness, 1.0, 20.0);
        double duration = Math.Clamp(settings.TrailDurationMs, 100, 3000);
        double peakOpacity = Math.Clamp(settings.TrailOpacity, 0.05, 1.0);

        var overlay = new OverlayWindow(clickThrough: true);
        overlay.Show();
        overlay.CoverVirtualScreen();

        var fromPos = overlay.PhysicalToWpf(fromX, fromY);
        var toPos = overlay.PhysicalToWpf(toX, toY);

        double offsetX = overlay.Left;
        double offsetY = overlay.Top;

        double x1 = fromPos.X - offsetX;
        double y1 = fromPos.Y - offsetY;
        double x2 = toPos.X - offsetX;
        double y2 = toPos.Y - offsetY;

        // セグメント分割（遠端=移動元側=i=0 が先に消える）
        const int segmentCount = 12;
        var brush = new SolidColorBrush(color);

        // BeginTime の最大値を duration の半分まで広げ、各セグメントの実フェード時間も duration の半分。
        // 結果として全体の表示時間（最後のセグメントが消えるまで）≒ duration になる。
        double staggerSpan = duration * 0.5;
        double segDuration = duration * 0.5;

        // 最終アニメ完了でオーバーレイを閉じるためのカウンタ
        int remainingAnims = segmentCount + 1; // セグメント + 移動先円

        for (int i = 0; i < segmentCount; i++)
        {
            double t1 = (double)i / segmentCount;
            double t2 = (double)(i + 1) / segmentCount;

            var seg = new Line
            {
                X1 = x1 + (x2 - x1) * t1,
                Y1 = y1 + (y2 - y1) * t1,
                X2 = x1 + (x2 - x1) * t2,
                Y2 = y1 + (y2 - y1) * t2,
                Stroke = brush,
                StrokeThickness = thickness,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                Opacity = peakOpacity
            };
            overlay.OverlayCanvasElement.Children.Add(seg);

            // i=0（移動元=遠端）→ BeginTime=0（最初に消える）
            // i=segmentCount-1（移動先=近端）→ BeginTime=staggerSpan（最後に消える）
            double beginMs = staggerSpan * ((double)i / Math.Max(1, segmentCount - 1));

            var anim = new DoubleAnimation(peakOpacity, 0.0, TimeSpan.FromMilliseconds(segDuration))
            {
                BeginTime = TimeSpan.FromMilliseconds(beginMs),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };
            anim.Completed += (_, _) =>
            {
                remainingAnims--;
                if (remainingAnims <= 0) overlay.Close();
            };
            seg.BeginAnimation(UIElement.OpacityProperty, anim);
        }

        // 移動先に小さい円を表示（最後に消える）
        const double targetRadius = 8;
        var targetCircle = new Ellipse
        {
            Width = targetRadius * 2,
            Height = targetRadius * 2,
            Fill = brush,
            Opacity = peakOpacity
        };
        Canvas.SetLeft(targetCircle, x2 - targetRadius);
        Canvas.SetTop(targetCircle, y2 - targetRadius);
        overlay.OverlayCanvasElement.Children.Add(targetCircle);

        var circleAnim = new DoubleAnimation(peakOpacity, 0.0, TimeSpan.FromMilliseconds(segDuration))
        {
            BeginTime = TimeSpan.FromMilliseconds(staggerSpan),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
        };
        circleAnim.Completed += (_, _) =>
        {
            remainingAnims--;
            if (remainingAnims <= 0) overlay.Close();
        };
        targetCircle.BeginAnimation(UIElement.OpacityProperty, circleAnim);
    }

    /// <summary>
    /// 座標マーカーを表示し、クリックで削除可能にする。
    /// 複数のストアを色分けして同時表示する（Set A / Set B など）。
    /// オーバーレイは表示専用（clickThrough=true）。入力はすべて低レベルマウスフック+
    /// キーボードフックで処理するため、ウィンドウフォーカスに依存しない。
    /// </summary>
    /// <param name="stores">(座標ストア, マーカー色) のペア。先頭のストアが「空きエリアクリックで追加」の対象になる。</param>
    public void ShowCoordinateMarkers(
        IReadOnlyList<(CoordinateStore store, Color color)> stores,
        Action? onEnterMode,
        Action? onExitMode)
    {
        // 既にマーカー表示中なら閉じる
        CloseMarkerOverlay();

        _deleteStores = new List<(CoordinateStore, Color)>(stores);
        _deleteOnExitMode = onExitMode;
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

        int totalCount = 0;
        foreach (var (s, _) in _deleteStores) totalCount += s.Count;
        DebugLog.Write($"ShowCoordinateMarkers: stores={_deleteStores.Count}, totalCount={totalCount}");

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

        _deleteStores = new List<(CoordinateStore, Color)>();
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
        if (_deleteStores.Count == 0 || _markerOverlay is null) return;

        int closestIdx = FindNearestMarker(e.X, e.Y);
        if (closestIdx >= 0)
        {
            // マーカー上: 該当ストアから削除
            var (_, store, storeIdx, _, _) = _markers[closestIdx];
            DebugLog.Write($"Marker clicked via hook: storeIdx={storeIdx}, physical=({e.X},{e.Y}) - removing");
            store.RemoveAt(storeIdx);
        }
        else
        {
            // マーカー外: 先頭ストア（Set A）に追加
            DebugLog.Write($"Empty area clicked via hook: physical=({e.X},{e.Y}) - adding to first store");
            _deleteStores[0].store.Add(e.X, e.Y);
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
            Text = Loc.Get("Str.Overlay.DeleteMode.Title"),
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
            $"[{ShortcutFormatter.FormatForDeleteMode(settings.SaveShortcut)}]",
            Loc.Get("Str.Overlay.DeleteMode.AddDelete")));

        // DisplayDelete ショートカット行
        stack.Children.Add(BuildHelpLine(
            $"[{ShortcutFormatter.FormatForDeleteMode(settings.DisplayDeleteShortcut)}]",
            Loc.Get("Str.Overlay.DeleteMode.DeleteAll")));

        // Navigate ショートカット行
        stack.Children.Add(BuildHelpLine(
            $"[{ShortcutFormatter.FormatForDeleteMode(settings.NavigateShortcut)}]",
            Loc.Get("Str.Overlay.DeleteMode.Exit")));

        // ESC 行
        stack.Children.Add(BuildHelpLine("[ESC]", Loc.Get("Str.Overlay.DeleteMode.Exit")));

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
        if (_deleteStores.Count == 0 || _markerOverlay is null) return;

        // 全ストア合算で削除前の座標を集める（収縮円アニメ用）
        var positions = new List<(int X, int Y)>();
        int totalBefore = 0;
        foreach (var (store, _) in _deleteStores)
        {
            totalBefore += store.Count;
            foreach (var coord in store.GetAll())
                positions.Add((coord.X, coord.Y));
        }
        DebugLog.Write($"ExecuteClearAll: clearing {totalBefore} coordinates across {_deleteStores.Count} stores");

        // 全ストアを Clear
        foreach (var (store, _) in _deleteStores)
            store.Clear();

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
            var m = _markers[i];
            double dx = physX - m.physX;
            double dy = physY - m.physY;
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
    /// 全ストアのマーカー要素を構築してCanvasに配置する（表示専用、ストアごとに色分け）。
    /// ヘルプパネルは Children.Clear() 後に再追加して保持する。
    /// 入力処理は低レベルフックで行うため、WPFイベントハンドラは不要。
    /// </summary>
    private void BuildMarkers(OverlayWindow overlay)
    {
        // ヘルプパネルを退避（Children.Clear() で消えるため）
        var savedHelpPanel = _helpPanel;

        overlay.OverlayCanvasElement.Children.Clear();
        _markers.Clear();

        if (_deleteStores.Count == 0)
        {
            if (savedHelpPanel is not null)
                overlay.OverlayCanvasElement.Children.Add(savedHelpPanel);
            return;
        }

        // マーカーエフェクトが無効なら描画をスキップ（モード自体は継続 = ESC/クリック追加は動作）
        if (_settingsService.Current.MarkerEffectEnabled)
        {
            foreach (var (store, color) in _deleteStores)
            {
                var coordinates = store.GetAll();
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
                        Fill = new SolidColorBrush(Color.FromArgb(180, color.R, color.G, color.B)),
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

                    _markers.Add((ellipse, store, i, coord.X, coord.Y));
                }
            }
            DebugLog.Write($"BuildMarkers: drew {_markers.Count} markers across {_deleteStores.Count} stores");
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
