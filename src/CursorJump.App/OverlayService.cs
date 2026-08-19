using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace CursorJump.App;

internal sealed class OverlayService : IDisposable
{
    private readonly SettingsService _settingsService;
    private readonly LicenseService _licenseService;
    private MouseHookService? _mouseHookService;
    private KeyboardHookService? _keyboardHookService;
    private OverlayWindow? _markerOverlay;

    // 軌跡・収縮円・一括収縮円で共有するシングルトンオーバーレイ。
    // 起動時に 1 枚だけ生成し、ジャンプのたびに Canvas に要素を追加・削除して再利用する。
    // 毎回 new OverlayWindow して Show するとレイヤード HWND 作成コストで 3 画面環境で
    // ラグや砂時計カーソルが発生していたため、v1.6.1 で再利用方式に変更。
    private OverlayWindow? _trailOverlay;

    // 削除モード用状態（複数ストア対応）
    private List<(CoordinateStore store, Color color)> _deleteStores = new();
    private Action? _deleteOnExitMode;
    private List<(Ellipse ellipse, CoordinateStore store, int indexInStore, int physX, int physY)> _markers = new();
    private Ellipse? _lastHighlighted;
    private const double MarkerRadius = 15;
    private const double SnapDistancePhysical = 40; // 物理ピクセル

    // 直近の Set A 追加追跡（350ms 以内に同じマーカーを再クリックすると Set B に昇格）
    private const int PromoteToBWindowMs = 350;
    private long _recentSetAAddTick;
    private int _recentSetAAddPhysX;
    private int _recentSetAAddPhysY;

    // ヘルプパネル関連
    private Border? _helpPanel;
    private string _currentMonitorDeviceName = string.Empty;

    public OverlayService(SettingsService settingsService, LicenseService licenseService)
    {
        _settingsService = settingsService;
        _licenseService = licenseService;
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
    /// 軌跡/収縮円用のオーバーレイを起動時にプリアロケートして再利用準備する。
    /// MainWindow.OnSourceInitialized から呼ぶ想定。
    /// </summary>
    public void PreallocateTrailOverlay() => EnsureTrailOverlay();

    /// <summary>
    /// 計装用: シングルトン軌跡オーバーレイの Canvas 子要素数。
    /// アニメ完了取りこぼしによる蓄積リーク疑い (800MB 成長問題) の診断目的で一時的に追加。
    /// オーバーレイ未生成時は -1 を返す。
    /// </summary>
    internal int TrailOverlayChildCount =>
        _trailOverlay?.OverlayCanvasElement.Children.Count ?? -1;

    // v1.7.4 計装: アニメ完了で Remove に成功した子要素数の累計。
    // MainWindow の MemDiag 定期ログから差分を取って「直近 1 分で何個 Remove されたか」を出す。
    private long _trailRemoveSuccessTotal;
    internal long TrailRemoveSuccessTotal => System.Threading.Interlocked.Read(ref _trailRemoveSuccessTotal);
    private void OnTrailChildRemoved() => System.Threading.Interlocked.Increment(ref _trailRemoveSuccessTotal);

    /// <summary>
    /// シングルトンの軌跡用オーバーレイを必要に応じて生成する。
    /// 既存の WPF Window を使い回すため、HWND 生成のコストを 1 回で済ませる。
    /// </summary>
    private OverlayWindow EnsureTrailOverlay()
    {
        if (IsTrailOverlayHealthy())
        {
            // v1.7.5: 常駐窓は他の最前面窓に押されて Z オーダーが沈むことがある
            // (Topmost プロパティは true のまま)。描画前に最前面へ再昇格して「軌跡が突然消える」を防ぐ。
            bool raised = _trailOverlay!.RaiseToTopmost();
            // v1.7.4 計装: 既存再利用パスも明示的にログ (頻度抑制のため最低限の情報のみ)
            DebugLog.Write($"EnsureTrailOverlay: reused existing (IsVisible={_trailOverlay.IsVisible}, Opacity={_trailOverlay.Opacity}, Topmost={_trailOverlay.Topmost}, children={_trailOverlay.OverlayCanvasElement.Children.Count}, raisedTopmost={raised})");
            return _trailOverlay;
        }

        if (_trailOverlay is not null)
        {
            DebugLog.Write("EnsureTrailOverlay: existing overlay is unhealthy, recreating");
            try { _trailOverlay.Close(); } catch (Exception ex) { DebugLog.Write($"EnsureTrailOverlay: close failed: {ex.Message}"); }
            _trailOverlay = null;
        }

        var overlay = new OverlayWindow(clickThrough: true);
        overlay.Show();
        overlay.CoverVirtualScreen();
        overlay.TrackDisplaySettingsChanges();
        _trailOverlay = overlay;
        DebugLog.Write("EnsureTrailOverlay: created singleton trail overlay (fresh)");
        return _trailOverlay;
    }

    /// <summary>
    /// シングルトンオーバーレイが生存しているか。HwndSource 破棄や予期せぬ Close で内部状態が壊れた
    /// 場合に false を返し、EnsureTrailOverlay が再生成できるようにする。
    /// 「移動エフェクトが突然出なくなる、再起動で直る」症状の自己修復用 (v1.7.2)。
    /// </summary>
    private bool IsTrailOverlayHealthy()
    {
        if (_trailOverlay is null) return false;
        try
        {
            if (!_trailOverlay.IsLoaded) return false;
            if (PresentationSource.FromVisual(_trailOverlay) is null) return false;
            var hwnd = new WindowInteropHelper(_trailOverlay).Handle;
            if (hwnd == IntPtr.Zero) return false;
            return true;
        }
        catch (Exception ex)
        {
            DebugLog.Write($"IsTrailOverlayHealthy: exception treated as unhealthy: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 連続 Navigate 時に前回のアニメ要素が残っていれば一掃する。
    /// </summary>
    private void ClearTrailOverlayChildren()
    {
        if (_trailOverlay is null) return;
        _trailOverlay.OverlayCanvasElement.Children.Clear();
    }

    public void Dispose()
    {
        try { _trailOverlay?.Close(); } catch { }
        _trailOverlay = null;
    }

    /// <summary>
    /// 座標保存時の収縮円アニメーション表示。
    /// </summary>
    public void ShowShrinkCircle(int physicalX, int physicalY, string? colorOverride = null)
    {
        // v1.7.4 計装: 呼出ログ (Enabled 含む)
        DebugLog.Write($"ShowShrinkCircle: at=({physicalX},{physicalY}), SaveEffectEnabled={_settingsService.Current.SaveEffectEnabled}");

        if (!_settingsService.Current.SaveEffectEnabled) return;
        var color = ParseColor(colorOverride ?? _settingsService.Current.SaveCircleColor, Colors.Red);
        const double initialRadius = 30;
        const double duration = 400; // ms

        var overlay = EnsureTrailOverlay();

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

        // アニメ完了で要素のみ撤去 (オーバーレイは閉じない: 再利用)
        opacityAnim.Completed += (_, _) =>
        {
            try
            {
                if (_trailOverlay is not null)
                {
                    _trailOverlay.OverlayCanvasElement.Children.Remove(ellipse);
                    OnTrailChildRemoved();
                }
            }
            catch (Exception ex) { DebugLog.Write($"ShowShrinkCircle.Completed exception: {ex.Message}"); }
        };

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
        // v1.7.4 計装: 軌跡消失バグ調査用の入口ログ
        DebugLog.Write($"ShowTrail.begin: from=({fromX},{fromY}) to=({toX},{toY}), TrailEffectEnabled={settings.TrailEffectEnabled}");

        if (!settings.TrailEffectEnabled) return;

        var color = ParseColor(colorOverride ?? settings.TrailColor, Colors.LimeGreen);

        // 設定値クランプ（範囲外の値が settings.json に入っていても安全に）
        double thickness = Math.Clamp(settings.TrailThickness, 1.0, 20.0);
        double duration = Math.Clamp(settings.TrailDurationMs, 100, 3000);
        double peakOpacity = Math.Clamp(settings.TrailOpacity, 0.05, 1.0);

        var overlay = EnsureTrailOverlay();
        int childrenBefore = overlay.OverlayCanvasElement.Children.Count;
        // 連続 Navigate で前回アニメ要素が残っていれば一掃 (見栄え優先)
        ClearTrailOverlayChildren();

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
            var segCapture = seg;
            anim.Completed += (_, _) =>
            {
                try
                {
                    if (_trailOverlay is not null)
                    {
                        _trailOverlay.OverlayCanvasElement.Children.Remove(segCapture);
                        OnTrailChildRemoved();
                    }
                }
                catch (Exception ex) { DebugLog.Write($"ShowTrail.seg.Completed exception: {ex.Message}"); }
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
            try
            {
                if (_trailOverlay is not null)
                {
                    _trailOverlay.OverlayCanvasElement.Children.Remove(targetCircle);
                    OnTrailChildRemoved();
                }
            }
            catch (Exception ex) { DebugLog.Write($"ShowTrail.target.Completed exception: {ex.Message}"); }
        };
        targetCircle.BeginAnimation(UIElement.OpacityProperty, circleAnim);

        // v1.7.4 計装: 軌跡描画完了直後の状態 (アニメ実行中、まだ要素は Canvas 上にある)
        int childrenAfter = overlay.OverlayCanvasElement.Children.Count;
        int segmentsAdded = segmentCount + 1; // セグメント + ターゲット円
        DebugLog.Write($"ShowTrail.end: segmentsAdded={segmentsAdded}, childrenBefore={childrenBefore}, childrenAfter={childrenAfter}, IsVisible={overlay.IsVisible}, Opacity={overlay.Opacity}, Topmost={overlay.Topmost}");
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
        _recentSetAAddTick = 0;

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

        var setAStore = _deleteStores[0].store;
        int closestIdx = FindNearestMarker(e.X, e.Y);
        if (closestIdx >= 0)
        {
            var (_, store, storeIdx, markerPhysX, markerPhysY) = _markers[closestIdx];

            // 昇格チェック: Pro 版 かつ Set A のマーカー かつ直近追加した座標 かつ 350ms 以内
            if (_licenseService.IsPro
                && _deleteStores.Count >= 2
                && ReferenceEquals(store, setAStore)
                && markerPhysX == _recentSetAAddPhysX
                && markerPhysY == _recentSetAAddPhysY
                && Environment.TickCount64 - _recentSetAAddTick <= PromoteToBWindowMs)
            {
                setAStore.RemoveAt(storeIdx);
                var setBStore = _deleteStores[1].store;
                setBStore.Add(markerPhysX, markerPhysY);
                ShowShrinkCircle(markerPhysX, markerPhysY, _settingsService.Current.SaveCircleColorB);
                _recentSetAAddTick = 0;
                DebugLog.Write($"DeleteMode: promoted ({markerPhysX},{markerPhysY}) from Set A to Set B");
            }
            else
            {
                // 通常削除
                DebugLog.Write($"Marker clicked via hook: storeIdx={storeIdx}, physical=({e.X},{e.Y}) - removing");
                store.RemoveAt(storeIdx);
                _recentSetAAddTick = 0;
            }
        }
        else
        {
            // マーカー外: 先頭ストア（Set A）に追加。Free 版では上限を超えると追加せず無視（マーカー再描画もしないので視覚変化なし）
            if (!_licenseService.IsPro && setAStore.Count >= LicenseService.FreeMaxCoordinates)
            {
                DebugLog.Write($"Empty area clicked via hook: physical=({e.X},{e.Y}) - blocked by Free edition limit ({setAStore.Count}/{LicenseService.FreeMaxCoordinates})");
            }
            else
            {
                DebugLog.Write($"Empty area clicked via hook: physical=({e.X},{e.Y}) - adding to first store");
                setAStore.Add(e.X, e.Y);
                _recentSetAAddTick = Environment.TickCount64;
                _recentSetAAddPhysX = e.X;
                _recentSetAAddPhysY = e.Y;
            }
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

        // ダブルクリック昇格行（Pro 版のみ表示）
        if (_licenseService.IsPro)
            stack.Children.Add(BuildHelpLine(
                $"[{Loc.Get("Str.Overlay.DeleteMode.DoubleClick")}]",
                Loc.Get("Str.Overlay.DeleteMode.AddSetB")));

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

        var overlay = EnsureTrailOverlay();

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

            // アニメ完了で個別要素のみ撤去 (オーバーレイは閉じない)
            var ellipseCapture = ellipse;
            opacityAnim.Completed += (_, _) =>
            {
                try
                {
                    if (_trailOverlay is not null)
                    {
                        _trailOverlay.OverlayCanvasElement.Children.Remove(ellipseCapture);
                        OnTrailChildRemoved();
                    }
                }
                catch (Exception ex) { DebugLog.Write($"ShowClearAllShrinkCircles.Completed exception: {ex.Message}"); }
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
            // 接続中モニタのスナップショット（消失モニタの座標は描画対象外。バグ3対策）
            var monitors = MonitorIdentity.Snapshot();

            foreach (var (store, color) in _deleteStores)
            {
                var coordinates = store.GetAll();
                for (int i = 0; i < coordinates.Count; i++)
                {
                    var coord = coordinates[i];
                    if (!MonitorFilter.IsCoordinateOnConnectedMonitor(coord, monitors)) continue;

                    // マーカーは「保存時の絶対座標」ではなく「実際のジャンプ先」に描く。
                    // ドック着脱でデバイス名が振り直されても、見えている位置と飛ぶ位置が一致する（v1.9.3）。
                    var resolved = JumpTargetResolver.Resolve(coord, monitors);
                    var pos = overlay.PhysicalToWpf(resolved.X, resolved.Y);
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

                    // ヒットテストも描画位置（解決後の座標）で行う
                    _markers.Add((ellipse, store, i, resolved.X, resolved.Y));
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
