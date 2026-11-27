using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace CursorJump.App.Controls;

/// <summary>
/// 設定画面のヘルプ用ループアニメーション。モニタの絵の上で、カーソルが3か所を素早く保存し、
/// 軌跡を引きながら順にジャンプする様子を繰り返し見せる。
/// 色はすべて SetResourceReference でテーマ切替に追従する（ウィンドウのタイトルバー色のみ固定）。
/// ページを離れる・ウィンドウを閉じるときは <see cref="Stop"/> を呼ぶこと。
/// </summary>
internal sealed class HelpAnimation : Border
{
    // ===== タイミング定数（秒）。1周 4.5 秒：ユーザー要望「もっと速く」(2026-11-27) =====
    private const double LoopSeconds = 4.5;
    /// <summary>保存の時刻。0.6 秒間隔で3点。</summary>
    private static readonly double[] SaveAt = [0.35, 0.95, 1.55];
    /// <summary>ジャンプの時刻。0.5 秒間隔（軌跡 0.45 秒が次のジャンプと重ならない）。</summary>
    private static readonly double[] JumpAt = [2.4, 2.9, 3.4];
    private const double MoveLead = 0.35, MoveEnd = 0.05;   // 保存前にカーソルが点へ動く区間 [SAVE-0.35, SAVE-0.05]
    private const double PressSave = 0.15;                   // 保存キーを押している長さ
    private const double PressJumpBefore = 0.05, PressJumpAfter = 0.12; // ジャンプキーを押している区間
    private const double MarkerDelay = 0.03;                 // 保存からマーカー出現まで
    private const double ShrinkSeconds = 0.4;                // 縮む円の長さ
    private const double TrailSeconds = 0.45;                // 軌跡が消えきるまで
    private const double JumpMoveSeconds = 0.18;             // ジャンプでカーソルが目的地へ動く長さ（瞬間移動だと動きが見えないため）
    private const double KeysSwitchAt = 2.1;                 // 保存キー → 移動キーの切替
    private const double FadeStart = 4.0, FadeEnd = 4.25;    // マーカーの消去
    private const int TrailSegments = 12;

    // ===== 絵の大きさ（DIP） =====
    private const double StageW = 520, StageH = 262;
    private static readonly Point[] Points = [new(165, 62), new(338, 96), new(214, 142)];
    private static readonly Point StartPoint = new(262, 160);

    private readonly Canvas _canvas;
    private readonly TextBlock _capSave, _capNav;
    private readonly Grid _rowSave, _rowNav;
    private readonly List<(Border Key, Border Pressed, TranslateTransform Shift)> _saveKeys = [], _navKeys = [];
    private readonly TranslateTransform _cursorMove = new(StartPoint.X, StartPoint.Y);
    private readonly Border[] _markers = new Border[3];
    private readonly Ellipse[] _rings = new Ellipse[3];
    private readonly Line[][] _trails = new Line[3][];
    private Storyboard _story = new();
    private bool _running;

    /// <summary>絵を組み立てる。ショートカット表記は <see cref="SetShortcuts"/> で後から設定する。</summary>
    public HelpAnimation()
    {
        Width = StageW;
        HorizontalAlignment = HorizontalAlignment.Center;

        _canvas = new Canvas { Width = StageW, Height = StageH, ClipToBounds = true };
        BuildMonitor();
        BuildEffects();

        _rowSave = KeyRow();
        _rowNav = KeyRow();
        _rowNav.Opacity = 0;
        _canvas.Children.Add(_rowSave);
        _canvas.Children.Add(_rowNav);

        var captions = new Grid { Height = 24, Margin = new Thickness(0, 4, 0, 0) };
        _capSave = Caption();
        _capNav = Caption();
        _capNav.Opacity = 0;
        captions.Children.Add(_capSave);
        captions.Children.Add(_capNav);

        var stack = new StackPanel();
        stack.Children.Add(_canvas);
        stack.Children.Add(captions);
        Child = stack;

        SetShortcuts([], []);
    }

    /// <summary>
    /// キーキャップと字幕を作り直す（言語・ショートカット変更時）。動作中なら止めて再開する。
    /// </summary>
    /// <param name="saveKeys">保存操作の表記（例: Ctrl, Win, 左クリック）</param>
    /// <param name="navKeys">移動操作の表記</param>
    private enum ColorRole { Save, Trail, Marker }
    private readonly List<(DependencyObject Target, DependencyProperty Property, ColorRole Role)> _colorTargets = new();
    private readonly List<TextBlock> _markerNumbers = new();

    /// <summary>
    /// 円・線・印の色を設定（1 つ目のリストの色）に合わせる（2026-11-27 ユーザー要望「色は設定に追従」）。
    /// 呼ばれるまでは強調色（AccentBrush）のまま。印の番号は実物のマーカーと同じく白。
    /// </summary>
    public void SetColors(Color save, Color trail, Color marker)
    {
        foreach (var (target, property, role) in _colorTargets)
        {
            var c = role switch { ColorRole.Save => save, ColorRole.Trail => trail, _ => marker };
            target.SetValue(property, new SolidColorBrush(c));
        }
        foreach (var num in _markerNumbers) num.Foreground = Brushes.White;
    }

    public void SetShortcuts(IReadOnlyList<string> saveKeys, IReadOnlyList<string> navKeys)
    {
        bool wasRunning = _running;
        Stop();

        FillRow(_rowSave, _saveKeys, saveKeys);
        FillRow(_rowNav, _navKeys, navKeys);
        _capSave.Text = string.Format(Loc.Get("Str.Help.CaptionSaveFormat"), string.Join(" + ", saveKeys));
        _capNav.Text = string.Format(Loc.Get("Str.Help.CaptionNavFormat"), string.Join(" + ", navKeys));
        AutomationProperties.SetName(_canvas, _capSave.Text);

        BuildStory();
        if (wasRunning) Start();
    }

    /// <summary>ループ再生を始める（再呼び出しは無視）。アニメーション無効設定のときは静止画にする。</summary>
    public void Start()
    {
        if (_running) return;
        _running = true;
        if (!SystemParameters.ClientAreaAnimation)
        {
            ApplyStaticFrame();
            return;
        }
        _story.Begin(this, isControllable: true);
    }

    /// <summary>再生を止めて初期状態に戻す（再呼び出しは無視）。</summary>
    public void Stop()
    {
        if (!_running) return;
        _running = false;
        _story.Stop(this);
        ResetLocalState();
    }

    // ---------- 絵の組み立て ----------

    private void BuildMonitor()
    {
        var neck = new Polygon { Points = [new(244, 196), new(276, 196), new(282, 216), new(238, 216)], StrokeThickness = 1 };
        Res(neck, Shape.FillProperty, "BgInputBrush");
        Res(neck, Shape.StrokeProperty, "BorderInputBrush");
        _canvas.Children.Add(neck);

        _canvas.Children.Add(Rect(210, 216, 100, 7, 3.5, "BgInputBrush", "BorderInputBrush"));
        _canvas.Children.Add(Rect(110, 8, 300, 188, 9, "BgInputBrush", "BorderInputBrush"));
        _canvas.Children.Add(Rect(118, 16, 284, 172, 3, "BgPrimaryBrush", null));

        // タスクバー（画面下 10px）と小さな4つのアイコン
        var bar = new Border { Width = 284, Height = 10, CornerRadius = new CornerRadius(0, 0, 3, 3), BorderThickness = new Thickness(0, 1, 0, 0) };
        Res(bar, BackgroundProperty, "BgCardBrush");
        Res(bar, BorderBrushProperty, "BorderSubtleBrush");
        Canvas.SetLeft(bar, 118);
        Canvas.SetTop(bar, 178);
        _canvas.Children.Add(bar);
        for (int i = 0; i < 4; i++)
            _canvas.Children.Add(Rect(233 + i * 15, 180.5, 9, 5, 1.5, "TextMutedBrush", null));

        _canvas.Children.Add(AppWindow(126, "#7AD69E"));
        _canvas.Children.Add(AppWindow(264, "#F2B866"));
    }

    private static Border AppWindow(double left, string titleColor)
    {
        var title = new Border
        {
            Height = 13, VerticalAlignment = VerticalAlignment.Top, CornerRadius = new CornerRadius(5, 5, 0, 0),
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(titleColor)),
        };
        var win = new Border { Width = 130, Height = 146, CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), Child = title };
        Res(win, BackgroundProperty, "BgCardBrush");
        Res(win, BorderBrushProperty, "BorderSubtleBrush");
        Canvas.SetLeft(win, left);
        Canvas.SetTop(win, 24);
        return win;
    }

    /// <summary>マーカー・縮む円・軌跡・カーソル（アニメは BuildStory で付ける）。</summary>
    private void BuildEffects()
    {
        for (int i = 0; i < 3; i++)
        {
            var p = Points[i];

            var ring = new Ellipse { Width = 78, Height = 78, StrokeThickness = 2.5, Opacity = 0, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(1, 1) };
            Res(ring, Shape.StrokeProperty, "AccentBrush");
            _colorTargets.Add((ring, Shape.StrokeProperty, ColorRole.Save));
            Canvas.SetLeft(ring, p.X - 39);
            Canvas.SetTop(ring, p.Y - 39);
            Panel.SetZIndex(ring, 5);
            _canvas.Children.Add(ring);
            _rings[i] = ring;

            var num = new TextBlock { Text = (i + 1).ToString(), FontSize = 11, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            Res(num, TextBlock.ForegroundProperty, "AccentForegroundBrush");
            var marker = new Border { Width = 20, Height = 20, CornerRadius = new CornerRadius(10), Opacity = 0, Child = num };
            Res(marker, BackgroundProperty, "AccentBrush");
            _colorTargets.Add((marker, BackgroundProperty, ColorRole.Marker));
            _markerNumbers.Add(num);
            Canvas.SetLeft(marker, p.X - 10);
            Canvas.SetTop(marker, p.Y - 10);
            Panel.SetZIndex(marker, 4);
            _canvas.Children.Add(marker);
            _markers[i] = marker;

            // ジャンプ i の軌跡: 直前の点 → Points[i]。ジャンプ1 は保存後にカーソルがある P3 から
            var from = Points[(i + 2) % 3];
            _trails[i] = new Line[TrailSegments];
            for (int k = 0; k < TrailSegments; k++)
            {
                double t0 = (double)k / TrailSegments, t1 = (double)(k + 1) / TrailSegments;
                var seg = new Line
                {
                    X1 = from.X + (p.X - from.X) * t0, Y1 = from.Y + (p.Y - from.Y) * t0,
                    X2 = from.X + (p.X - from.X) * t1, Y2 = from.Y + (p.Y - from.Y) * t1,
                    StrokeThickness = 5, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, Opacity = 0,
                };
                Res(seg, Shape.StrokeProperty, "AccentBrush");
                _colorTargets.Add((seg, Shape.StrokeProperty, ColorRole.Trail));
                Panel.SetZIndex(seg, 3);
                _canvas.Children.Add(seg);
                _trails[i][k] = seg;
            }
        }

        _cursor = new Path
        {
            Data = Geometry.Parse("M0,0 L0,17 L4.5,13 L7.5,19.5 L10.2,18.3 L7.3,12 L12.5,12 Z"),
            Fill = Brushes.White, Stroke = Brushes.Black, StrokeThickness = 1, StrokeLineJoin = PenLineJoin.Round,
            RenderTransform = _cursorMove,
        };
        Panel.SetZIndex(_cursor, 10);
        _canvas.Children.Add(_cursor);
    }

    private static Grid KeyRow()
    {
        var row = new Grid { Width = StageW, Height = 28 };
        Canvas.SetTop(row, 230);
        Panel.SetZIndex(row, 11);
        return row;
    }

    private static void FillRow(Grid row, List<(Border Key, Border Pressed, TranslateTransform Shift)> keys, IReadOnlyList<string> labels)
    {
        row.Children.Clear();
        keys.Clear();
        var panel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        for (int i = 0; i < labels.Count; i++)
        {
            if (i > 0)
            {
                var plus = new TextBlock { Text = "+", FontSize = 12, Margin = new Thickness(6, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
                Res(plus, TextBlock.ForegroundProperty, "TextSecondaryBrush");
                panel.Children.Add(plus);
            }
            var pressed = new Border { CornerRadius = new CornerRadius(5), BorderThickness = new Thickness(1.5), Opacity = 0 };
            Res(pressed, BackgroundProperty, "AccentSoftBrush");
            Res(pressed, BorderBrushProperty, "AccentBrush");
            var text = new TextBlock { Text = labels[i], FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(10, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
            Res(text, TextBlock.ForegroundProperty, "TextPrimaryBrush");
            var face = new Grid();
            face.Children.Add(pressed);
            face.Children.Add(text);
            var shift = new TranslateTransform();
            var key = new Border { Height = 28, CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), Child = face, RenderTransform = shift };
            Res(key, BackgroundProperty, "BgInputBrush");
            Res(key, BorderBrushProperty, "BorderInputBrush");
            panel.Children.Add(key);
            keys.Add((key, pressed, shift));
        }
        row.Children.Add(panel);
    }

    private static TextBlock Caption()
    {
        var t = new TextBlock { FontSize = 14, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        Res(t, TextBlock.ForegroundProperty, "TextPrimaryBrush");
        return t;
    }

    // ---------- ストーリーボード ----------

    private void BuildStory()
    {
        _story = new Storyboard { Duration = TimeSpan.FromSeconds(LoopSeconds), RepeatBehavior = RepeatBehavior.Forever };

        // カーソル: 保存前に点へ動き、ジャンプでも目的地へ素早く動く
        var xs = new List<(double, double, bool)> { (0, StartPoint.X, false) };
        var ys = new List<(double, double, bool)> { (0, StartPoint.Y, false) };
        var prev = StartPoint;
        for (int i = 0; i < 3; i++)
        {
            double hold = SaveAt[i] - MoveLead;
            if (hold > 1e-6) { xs.Add((hold, prev.X, false)); ys.Add((hold, prev.Y, false)); }
            xs.Add((SaveAt[i] - MoveEnd, Points[i].X, true));
            ys.Add((SaveAt[i] - MoveEnd, Points[i].Y, true));
            prev = Points[i];
        }
        // ジャンプ: 押した瞬間に移動を始め、JumpMoveSeconds で目的地へ素早く動く（2026-11-27 ユーザー要望「カーソルが動くように」）
        for (int j = 0; j < 3; j++)
        {
            var from = Points[(j + 2) % 3];
            xs.Add((JumpAt[j], from.X, false));
            ys.Add((JumpAt[j], from.Y, false));
            xs.Add((JumpAt[j] + JumpMoveSeconds, Points[j].X, true));
            ys.Add((JumpAt[j] + JumpMoveSeconds, Points[j].Y, true));
        }
        // Transform（Freezable）を直接 SetTarget すると動かない（2026-11-27 実機で確認、カーソルが止まったままだった）。
        // 木の中の要素（_cursor）から RenderTransform をたどるパスで指定する（CircleSwitch の HelpPage と同じ）
        Key(_cursor, OnTransform(TranslateTransform.XProperty), xs);
        Key(_cursor, OnTransform(TranslateTransform.YProperty), ys);

        // 保存: マーカーと縮む円
        for (int i = 0; i < 3; i++)
        {
            Key(_markers[i], OpacityProperty, [(0, 0, false), (SaveAt[i] + MarkerDelay, 1, false), (FadeStart, 1, false), (FadeEnd, 0, true)]);
            var scale = (ScaleTransform)_rings[i].RenderTransform;
            double end = 9.0 / 39.0;
            foreach (var axis in new[] { ScaleTransform.ScaleXProperty, ScaleTransform.ScaleYProperty })
                Key(scale, axis, [(0, 1, false), (SaveAt[i], 1, false), (SaveAt[i] + ShrinkSeconds, end, true)]);
            Key(_rings[i], OpacityProperty, [(0, 0, false), (SaveAt[i], 1, false), (SaveAt[i] + ShrinkSeconds, 0, true)]);
        }

        // 軌跡: 移動元に近い区間から順に消える（各区間の消える時間は全体の半分）
        double fade = TrailSeconds / 2;
        for (int j = 0; j < 3; j++)
            for (int k = 0; k < TrailSegments; k++)
            {
                double start = JumpAt[j] + k * (TrailSeconds - fade) / (TrailSegments - 1);
                var f = new List<(double, double, bool)> { (0, 0, false), (JumpAt[j], 0.85, false) };
                if (start > JumpAt[j] + 1e-6) f.Add((start, 0.85, false));
                f.Add((start + fade, 0, true));
                Key(_trails[j][k], OpacityProperty, f);
            }

        // キーキャップ: 保存キー → 移動キーの切替と押下表現
        Key(_rowSave, OpacityProperty, [(0, 1, false), (KeysSwitchAt, 0, false)]);
        Key(_rowNav, OpacityProperty, [(0, 0, false), (KeysSwitchAt, 1, false), (FadeStart, 0, false)]);
        Press(_saveKeys, SaveAt.Select(t => (t, t + PressSave)));
        Press(_navKeys, JumpAt.Select(t => (t - PressJumpBefore, t + PressJumpAfter)));

        // 字幕
        Key(_capSave, OpacityProperty, [(0, 1, false), (KeysSwitchAt, 0, false)]);
        Key(_capNav, OpacityProperty, [(0, 0, false), (KeysSwitchAt, 1, false), (FadeStart, 0, false)]);
    }

    private void Press(List<(Border Key, Border Pressed, TranslateTransform Shift)> keys, IEnumerable<(double On, double Off)> spans)
    {
        var overlay = new List<(double, double, bool)> { (0, 0, false) };
        var shift = new List<(double, double, bool)> { (0, 0, false) };
        foreach (var (on, off) in spans)
        {
            overlay.Add((on, 1, false)); overlay.Add((off, 0, false));
            shift.Add((on, 2, false)); shift.Add((off, 0, false));
        }
        foreach (var k in keys)
        {
            Key(k.Pressed, OpacityProperty, overlay);
            Key(k.Key, OnTransform(TranslateTransform.YProperty), shift);
        }
    }

    /// <summary>1つの値のキーフレーム。Ease が true の区間はなめらかに、false は時刻で切り替える。</summary>
    private Path _cursor = null!;

    /// <summary>要素の RenderTransform の中のプロパティを指すパス。</summary>
    private static PropertyPath OnTransform(DependencyProperty property) =>
        new("(0).(1)", UIElement.RenderTransformProperty, property);

    private void Key(DependencyObject target, DependencyProperty property, IEnumerable<(double At, double Value, bool Ease)> frames) =>
        Key(target, new PropertyPath(property), frames);

    private void Key(DependencyObject target, PropertyPath path, IEnumerable<(double At, double Value, bool Ease)> frames)
    {
        var anim = new DoubleAnimationUsingKeyFrames();
        foreach (var (at, value, ease) in frames)
        {
            var time = KeyTime.FromTimeSpan(TimeSpan.FromSeconds(at));
            anim.KeyFrames.Add(ease
                ? new EasingDoubleKeyFrame(value, time, new QuadraticEase { EasingMode = EasingMode.EaseInOut })
                : new DiscreteDoubleKeyFrame(value, time));
        }
        Storyboard.SetTarget(anim, target);
        Storyboard.SetTargetProperty(anim, path);
        _story.Children.Add(anim);
    }

    // ---------- 静止・初期状態 ----------

    /// <summary>アニメーション無効設定時の静止画（P3 にカーソル、3マーカー、移動の字幕）。</summary>
    private void ApplyStaticFrame()
    {
        _cursorMove.X = Points[2].X;
        _cursorMove.Y = Points[2].Y;
        foreach (var m in _markers) m.Opacity = 1;
        _rowSave.Opacity = 0;
        _rowNav.Opacity = 1;
        _capSave.Opacity = 0;
        _capNav.Opacity = 1;
    }

    private void ResetLocalState()
    {
        _cursorMove.X = StartPoint.X;
        _cursorMove.Y = StartPoint.Y;
        foreach (var m in _markers) m.Opacity = 0;
        _rowSave.Opacity = 1;
        _rowNav.Opacity = 0;
        _capSave.Opacity = 1;
        _capNav.Opacity = 0;
    }

    // ---------- 小さな補助 ----------

    private static void Res(FrameworkElement element, DependencyProperty property, string key) => element.SetResourceReference(property, key);

    private static Border Rect(double x, double y, double w, double h, double radius, string fillKey, string? strokeKey)
    {
        var b = new Border { Width = w, Height = h, CornerRadius = new CornerRadius(radius) };
        Res(b, BackgroundProperty, fillKey);
        if (strokeKey is not null)
        {
            b.BorderThickness = new Thickness(1);
            Res(b, BorderBrushProperty, strokeKey);
        }
        Canvas.SetLeft(b, x);
        Canvas.SetTop(b, y);
        return b;
    }
}
