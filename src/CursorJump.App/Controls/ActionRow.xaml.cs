using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CursorJump.App.Models;

namespace CursorJump.App.Controls;

/// <summary>
/// 設定画面「操作」ページの 1 行（v1.12.0）。1 つの <see cref="ActionShortcut"/> を表示・編集する。
/// 普段は見出し（アイコン・名前・今の割り当て）だけを見せ、押すと編集欄（マウス / キーボード / 修飾キー 2 回）が開く。
/// 値が変わるたびに <see cref="Changed"/> を出す。保存は呼び出し側（SettingsWindow）がその場で行う。
/// 旧 SettingsWindow のカード 9 枚分の重複コードをこの部品 1 つにまとめた。
/// </summary>
public partial class ActionRow : UserControl
{
    /// <summary>ボタン名 ComboBox の項目。ToString が現在言語の名前を返す（言語切替時は ItemsSource を差し替えて再評価）。</summary>
    public sealed class ButtonOption
    {
        public MouseButtonType Value { get; }
        public string ResourceKey { get; }
        public ButtonOption(MouseButtonType value, string key) { Value = value; ResourceKey = key; }
        public override string ToString() => Loc.Get(ResourceKey);
    }

    /// <summary>修飾キー連打 ComboBox の項目（v1.9.0+）。</summary>
    public sealed class GestureOption
    {
        public ModifierGesture Value { get; }
        public string ResourceKey { get; }
        public GestureOption(ModifierGesture value, string key) { Value = value; ResourceKey = key; }
        public override string ToString() => Loc.Get(ResourceKey);
    }

    // 連打ジェスチャの選択肢（None は「未使用」= トグル OFF で表すため含めない）
    private static readonly GestureOption[] GestureOptions =
    {
        new(ModifierGesture.CtrlDoubleTap,  "Str.Settings.Gesture.CtrlDoubleTap"),
        new(ModifierGesture.ShiftDoubleTap, "Str.Settings.Gesture.ShiftDoubleTap"),
        new(ModifierGesture.AltDoubleTap,   "Str.Settings.Gesture.AltDoubleTap"),
    };

    // 既定のボタン選択肢（並び: 左・右・ホイールクリック・マウスホイール・戻る・進む。v1.12.1 ユーザー指示）。
    // v1.12.1 で MiddleLeftChord/MiddleRightChord（ホイール＋左/右クリック）、v1.7.0 で MiddleDoubleClick/MiddleTripleClick、
    // v1.6.1 で WheelUp/WheelDown を UI から除外（動作は残す）。
    // 旧 settings.json に除外済みの値が残っている場合だけ BuildButtonOptions で末尾に足して表示を保つ。
    private static readonly ButtonOption[] DefaultButtonOptions =
    {
        new(MouseButtonType.Left,              "Str.Button.Left"),
        new(MouseButtonType.Right,             "Str.Button.Right"),
        new(MouseButtonType.Middle,            "Str.Button.Middle"),
        new(MouseButtonType.MouseWheel,        "Str.Button.MouseWheel"),
        new(MouseButtonType.XButton1,          "Str.Button.XButton1"),
        new(MouseButtonType.XButton2,          "Str.Button.XButton2"),
    };

    private static ButtonOption[] BuildButtonOptions(MouseButtonType currentValue)
    {
        string? legacyKey = currentValue switch
        {
            MouseButtonType.WheelUp           => "Str.Button.WheelUp",
            MouseButtonType.WheelDown         => "Str.Button.WheelDown",
            MouseButtonType.MiddleDoubleClick => "Str.Button.MiddleDoubleClick",
            MouseButtonType.MiddleTripleClick => "Str.Button.MiddleTripleClick",
            MouseButtonType.MiddleLeftChord   => "Str.Button.MiddleLeftChord",
            MouseButtonType.MiddleRightChord  => "Str.Button.MiddleRightChord",
            _ => null
        };
        if (legacyKey is null) return DefaultButtonOptions;
        var extended = new ButtonOption[DefaultButtonOptions.Length + 1];
        Array.Copy(DefaultButtonOptions, extended, DefaultButtonOptions.Length);
        extended[^1] = new ButtonOption(currentValue, legacyKey);
        return extended;
    }

    /// <summary>ボタン種別の表示名（現在言語）。</summary>
    internal static string ButtonName(MouseButtonType type)
    {
        foreach (var o in BuildButtonOptions(type))
            if (o.Value == type) return o.ToString();
        return type.ToString();
    }

    /// <summary>いまキー記録中の行（同時に 1 つだけ）。</summary>
    private static ActionRow? _recording;

    private bool _loading;
    private bool _expanded;
    private bool _locked;
    private int _virtualKeyCode;
    private string _titleKey = "";

    /// <summary>値が変わった（利用者の操作のみ。Load 中は出さない）。</summary>
    public event EventHandler? Changed;

    /// <summary>見出しが押されて開こうとしている（呼び出し側が他の行を閉じる）。</summary>
    public event EventHandler? ExpandRequested;

    public ActionRow()
    {
        InitializeComponent();
        CmbGesture.ItemsSource = GestureOptions;
        Unloaded += (_, _) => { if (_recording == this) EndRecording(); };
    }

    /// <summary>行の名前の文字列リソースキー（言語切替に追従）。</summary>
    public string TitleKey
    {
        get => _titleKey;
        set
        {
            _titleKey = value;
            TitleText.SetResourceReference(TextBlock.TextProperty, value);
        }
    }

    /// <summary>アイコン（Segoe Fluent Icons の文字）。</summary>
    public string Glyph
    {
        get => IconGlyph.Text;
        set => IconGlyph.Text = value;
    }

    /// <summary>2 つ目のリストの行。アイコンを 2 つ目の色（ピンク）にして、見出しなしで区別できるようにする。</summary>
    public bool IsSetB
    {
        set
        {
            if (value) IconGlyph.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x7F, 0xA8));
            else IconGlyph.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        }
    }

    /// <summary>アイコンの色を設定のリストの色にする（2 つ目のリストの行で使う）。</summary>
    public void SetIconColor(Color color) => IconGlyph.Foreground = new SolidColorBrush(color);

    /// <summary>修飾キー 2 回（ModifierSequence）を使える行か（Pro 追加機能 3 種のみ）。</summary>
    public bool SupportsGesture
    {
        get => GestureRow.Visibility == Visibility.Visible;
        set => GestureRow.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>カードの先頭の行（上の区切り線を出さない）。</summary>
    public bool IsFirst
    {
        set => RowBorder.BorderThickness = value ? new Thickness(0) : new Thickness(0, 1, 0, 0);
    }

    /// <summary>編集欄の下に足す追加の設定（クリック履歴の数・印など）。</summary>
    public object? Extra
    {
        get => ExtraHost.Content;
        set => ExtraHost.Content = value;
    }

    /// <summary>Free 版で使えない行。PRO の印を出し、薄くして押せなくする（v1.9.1 の「カードごとグレーアウト」と同じ扱い）。</summary>
    public bool IsLocked
    {
        get => _locked;
        set
        {
            _locked = value;
            ProBadge.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            IsEnabled = !value;
            Opacity = value ? 0.45 : 1.0;
            if (value) IsExpanded = false;
        }
    }

    public bool IsExpanded
    {
        get => _expanded;
        set
        {
            _expanded = value;
            Body.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            ChevronRotate.Angle = value ? 90 : 0;
            HeaderButton.SetValue(System.Windows.Automation.AutomationProperties.ItemStatusProperty, value ? "expanded" : "collapsed");
            if (!value && _recording == this) EndRecording();
        }
    }

    /// <summary>設定値を画面へ反映する（Changed は出さない）。</summary>
    public void Load(ActionShortcut shortcut)
    {
        _loading = true;
        try
        {
            ChkMouse.IsChecked = shortcut.EnabledTriggers.HasFlag(TriggerType.Mouse);
            ChkKeyboard.IsChecked = shortcut.EnabledTriggers.HasFlag(TriggerType.Keyboard);
            ChkGesture.IsChecked = shortcut.EnabledTriggers.HasFlag(TriggerType.ModifierSequence);
            SetModifiers(shortcut.Modifiers);
            var options = BuildButtonOptions(shortcut.MouseButton);
            CmbButton.ItemsSource = options;
            CmbButton.SelectedItem = Find(options, shortcut.MouseButton);
            var g = shortcut.ModifierGesture == ModifierGesture.None ? ModifierGesture.CtrlDoubleTap : shortcut.ModifierGesture;
            CmbGesture.SelectedItem = FindGesture(g);
            _virtualKeyCode = shortcut.VirtualKeyCode;
            UpdatePanels();
            RefreshKeyLabel();
            RefreshSummary();
        }
        finally { _loading = false; }
    }

    /// <summary>画面の値から ActionShortcut を作る。</summary>
    public ActionShortcut Read()
    {
        var triggers = TriggerType.None;
        if (ChkMouse.IsChecked == true) triggers |= TriggerType.Mouse;
        if (ChkKeyboard.IsChecked == true) triggers |= TriggerType.Keyboard;
        var gesture = (CmbGesture.SelectedItem as GestureOption)?.Value ?? ModifierGesture.None;
        if (SupportsGesture && ChkGesture.IsChecked == true && gesture != ModifierGesture.None)
            triggers |= TriggerType.ModifierSequence;
        return new ActionShortcut
        {
            EnabledTriggers = triggers,
            Modifiers = GetModifiers(),
            MouseButton = (CmbButton.SelectedItem as ButtonOption)?.Value ?? MouseButtonType.Left,
            VirtualKeyCode = _virtualKeyCode,
            // トグル OFF でも選択中のジェスチャ値は保持する（再 ON 時の利便性。旧 ReadProShortcutUI と同じ）
            ModifierGesture = SupportsGesture ? gesture : ModifierGesture.None,
        };
    }

    /// <summary>言語切替時に、ComboBox・キー表示・見出しの割り当てを現在言語で描き直す。</summary>
    public void RefreshLanguage()
    {
        _loading = true;
        try
        {
            var current = (CmbButton.SelectedItem as ButtonOption)?.Value ?? MouseButtonType.Left;
            var options = BuildButtonOptions(current);
            CmbButton.ItemsSource = null;
            CmbButton.ItemsSource = options;
            CmbButton.SelectedItem = Find(options, current);
            var g = (CmbGesture.SelectedItem as GestureOption)?.Value ?? ModifierGesture.CtrlDoubleTap;
            CmbGesture.ItemsSource = null;
            CmbGesture.ItemsSource = GestureOptions;
            CmbGesture.SelectedItem = FindGesture(g);
            RefreshKeyLabel();
            RefreshSummary();
            BtnRecord.Content = Loc.Get(_recording == this ? "Str.Settings.RecordKeyWaiting" : "Str.Settings.RecordKey");
        }
        finally { _loading = false; }
    }

    // ===== 見出しの割り当て表示 =====

    /// <summary>見出し右の小さなキー表示（例: [Ctrl] + [Win] + [左クリック]）。オフなら「オフ」。</summary>
    private void RefreshSummary()
    {
        SummaryPanel.Children.Clear();
        var sc = Read();
        var parts = new List<string>();
        if (sc.EnabledTriggers.HasFlag(TriggerType.Mouse))
        {
            parts.AddRange(ModifierParts(sc.Modifiers));
            parts.Add(ButtonName(sc.MouseButton));
        }

        if (parts.Count == 0 && sc.EnabledTriggers.HasFlag(TriggerType.Keyboard) && sc.VirtualKeyCode != 0)
            parts.AddRange(ShortcutFormatter.FormatKeyboard(sc).Split('+'));

        if (parts.Count == 0 && sc.EnabledTriggers.HasFlag(TriggerType.ModifierSequence))
            parts.Add((CmbGesture.SelectedItem as GestureOption)?.ToString() ?? "");

        if (parts.Count == 0)
        {
            var off = new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            off.SetResourceReference(TextBlock.TextProperty, "Str.Settings.Off");
            off.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");
            SummaryPanel.Children.Add(off);
            return;
        }

        for (int i = 0; i < parts.Count; i++)
        {
            if (i > 0)
            {
                var plus = new TextBlock { Text = "+", FontSize = 11, Margin = new Thickness(2, 0, 2, 0), VerticalAlignment = VerticalAlignment.Center };
                plus.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");
                SummaryPanel.Children.Add(plus);
            }
            var text = new TextBlock { Text = parts[i], FontSize = 12 };
            text.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
            var cap = new Border { Child = text, Padding = new Thickness(7, 1, 7, 1) };
            cap.SetResourceReference(StyleProperty, "KbdKeyStyle");
            SummaryPanel.Children.Add(cap);
        }

        // 2 つ以上の入力手段が有効なら、ほかもあることを小さく示す
        int kinds = 0;
        if (sc.EnabledTriggers.HasFlag(TriggerType.Mouse)) kinds++;
        if (sc.EnabledTriggers.HasFlag(TriggerType.Keyboard) && sc.VirtualKeyCode != 0) kinds++;
        if (sc.EnabledTriggers.HasFlag(TriggerType.ModifierSequence)) kinds++;
        if (kinds > 1)
        {
            var more = new TextBlock { Text = $"+{kinds - 1}", FontSize = 11, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            more.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");
            SummaryPanel.Children.Add(more);
        }
    }

    /// <summary>修飾キーを編集欄のチップと同じ順（Shift, Ctrl, Win, Alt）で返す。</summary>
    private static IEnumerable<string> ModifierParts(ModifierKeyFlags m)
    {
        if (m.HasFlag(ModifierKeyFlags.Shift)) yield return "Shift";
        if (m.HasFlag(ModifierKeyFlags.Control)) yield return "Ctrl";
        if (m.HasFlag(ModifierKeyFlags.Windows)) yield return "Win";
        if (m.HasFlag(ModifierKeyFlags.Alt)) yield return "Alt";
    }

    // ===== 操作 =====

    private void OnHeaderClick(object sender, RoutedEventArgs e)
    {
        if (_locked) return;
        if (!_expanded) ExpandRequested?.Invoke(this, EventArgs.Empty);
        IsExpanded = !_expanded;
    }

    private void OnTriggerToggleClick(object sender, RoutedEventArgs e)
    {
        UpdatePanels();
        NotifyChanged();
    }

    private void OnPartChanged(object sender, RoutedEventArgs e)
    {
        RefreshKeyLabel(); // 修飾キーはキーボード側の表示にも効く
        NotifyChanged();
    }

    private void OnComboChanged(object sender, SelectionChangedEventArgs e) => NotifyChanged();

    private void NotifyChanged()
    {
        if (_loading) return;
        RefreshSummary();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void UpdatePanels()
    {
        PnlMouse.Visibility = ChkMouse.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PnlKeyboard.Visibility = ChkKeyboard.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        CmbGesture.Visibility = ChkGesture.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private ModifierKeyFlags GetModifiers()
    {
        var m = ModifierKeyFlags.None;
        if (ChkCtrl.IsChecked == true)  m |= ModifierKeyFlags.Control;
        if (ChkAlt.IsChecked == true)   m |= ModifierKeyFlags.Alt;
        if (ChkShift.IsChecked == true) m |= ModifierKeyFlags.Shift;
        if (ChkWin.IsChecked == true)   m |= ModifierKeyFlags.Windows;
        return m;
    }

    private void SetModifiers(ModifierKeyFlags m)
    {
        ChkCtrl.IsChecked  = m.HasFlag(ModifierKeyFlags.Control);
        ChkAlt.IsChecked   = m.HasFlag(ModifierKeyFlags.Alt);
        ChkShift.IsChecked = m.HasFlag(ModifierKeyFlags.Shift);
        ChkWin.IsChecked   = m.HasFlag(ModifierKeyFlags.Windows);
    }

    private void RefreshKeyLabel()
    {
        if (_virtualKeyCode == 0)
        {
            LblKey.Text = Loc.Get("Str.Settings.KeyUnassigned");
            return;
        }
        LblKey.Text = ShortcutFormatter.FormatKeyboard(new ActionShortcut
        {
            EnabledTriggers = TriggerType.Keyboard,
            VirtualKeyCode = _virtualKeyCode,
            Modifiers = GetModifiers(),
        });
    }

    private static ButtonOption Find(ButtonOption[] options, MouseButtonType type)
    {
        foreach (var o in options) if (o.Value == type) return o;
        return options[0];
    }

    private static GestureOption FindGesture(ModifierGesture g)
    {
        foreach (var o in GestureOptions) if (o.Value == g) return o;
        return GestureOptions[0];
    }

    // ===== キーの記録（旧 SettingsWindow から移設。挙動は同じ） =====

    private void OnRecordClick(object sender, RoutedEventArgs e)
    {
        if (_recording == this) { EndRecording(); return; }
        _recording?.EndRecording();
        var window = Window.GetWindow(this);
        if (window is null) return;
        _recording = this;
        BtnRecord.Content = Loc.Get("Str.Settings.RecordKeyWaiting");
        window.PreviewKeyDown += OnRecordingKeyDown;
        window.Focus();
    }

    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        if (_recording == this) EndRecording();
        _virtualKeyCode = 0;
        RefreshKeyLabel();
        NotifyChanged();
    }

    private void OnRecordingKeyDown(object sender, KeyEventArgs e)
    {
        if (_recording != this) return;

        // Alt 押下時は e.Key が Key.System、e.SystemKey に実キーが入る
        var actualKey = e.Key == Key.System ? e.SystemKey : e.Key;
        int vk = KeyInterop.VirtualKeyFromKey(actualKey);
        e.Handled = true;

        if (IsModifierVk(vk)) return; // 修飾キー単独は無視して本キーを待つ
        if (vk == NativeMethods.VK_ESCAPE) { EndRecording(); return; }

        _virtualKeyCode = vk;
        var mods = ModifierKeyFlags.None;
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) mods |= ModifierKeyFlags.Control;
        if ((Keyboard.Modifiers & ModifierKeys.Alt)     != 0) mods |= ModifierKeyFlags.Alt;
        if ((Keyboard.Modifiers & ModifierKeys.Shift)   != 0) mods |= ModifierKeyFlags.Shift;
        // Win キーは WPF Keyboard.Modifiers に乗らないことが多いので GetAsyncKeyState で補完
        if ((NativeMethods.GetAsyncKeyState(NativeMethods.VK_LWIN) & 0x8000) != 0
            || (NativeMethods.GetAsyncKeyState(NativeMethods.VK_RWIN) & 0x8000) != 0)
            mods |= ModifierKeyFlags.Windows;
        SetModifiers(mods);
        ChkKeyboard.IsChecked = true; // 記録したらキーボードを自動で ON
        UpdatePanels();
        RefreshKeyLabel();
        EndRecording();
        NotifyChanged();
    }

    private void EndRecording()
    {
        var window = Window.GetWindow(this);
        if (window is not null) window.PreviewKeyDown -= OnRecordingKeyDown;
        BtnRecord.Content = Loc.Get("Str.Settings.RecordKey");
        if (_recording == this) _recording = null;
    }

    private static bool IsModifierVk(int vk) =>
        vk == NativeMethods.VK_LCONTROL || vk == NativeMethods.VK_RCONTROL || vk == NativeMethods.VK_CONTROL ||
        vk == NativeMethods.VK_LMENU    || vk == NativeMethods.VK_RMENU    || vk == NativeMethods.VK_MENU ||
        vk == NativeMethods.VK_LSHIFT   || vk == NativeMethods.VK_RSHIFT   || vk == NativeMethods.VK_SHIFT ||
        vk == NativeMethods.VK_LWIN     || vk == NativeMethods.VK_RWIN;
}
