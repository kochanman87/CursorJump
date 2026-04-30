using System;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using CursorJump.App.Models;

namespace CursorJump.App;

public partial class SettingsWindow : Window
{
    private readonly SettingsService _settingsService;

    private static readonly string[] ButtonNames =
    {
        "左クリック", "右クリック", "ホイールクリック", "戻るボタン", "進むボタン",
        "ホイール＋左クリック", "ホイール＋右クリック", "ホイール2連打", "ホイール3連打"
    };

    private static readonly string[] FKeyNames =
    {
        "F13", "F14", "F15", "F16", "F17", "F18",
        "F19", "F20", "F21", "F22", "F23", "F24"
    };
    private static readonly int[] FKeyCodes =
    {
        NativeMethods.VK_F13, NativeMethods.VK_F14, NativeMethods.VK_F15, NativeMethods.VK_F16,
        NativeMethods.VK_F17, NativeMethods.VK_F18, NativeMethods.VK_F19, NativeMethods.VK_F20,
        NativeMethods.VK_F21, NativeMethods.VK_F22, NativeMethods.VK_F23, NativeMethods.VK_F24
    };

    private string _saveColor = "#FF0000";
    private string _saveColorB = "#FF8800";
    private string _trailColor = "#00FF00";
    private string _trailColorB = "#FF8800";
    private string _markerColor = "#0088FF";
    private string _markerColorB = "#FF8800";
    private UiTheme _currentTheme = UiTheme.Light;

    private bool _initialized;

    public SettingsWindow(SettingsService settingsService)
    {
        _settingsService = settingsService;
        InitializeComponent();
        var v = Assembly.GetExecutingAssembly().GetName().Version;
        if (v is not null)
        {
            Title = $"CursorJump 設定  v{v.Major}.{v.Minor}.{v.Build}";
        }
        PopulateComboBoxes();
        LoadCurrentSettings();
        _initialized = true;
    }

    private void PopulateComboBoxes()
    {
        CmbSaveBtn.ItemsSource = ButtonNames;
        CmbNavBtn.ItemsSource = ButtonNames;
        CmbMonNavBtn.ItemsSource = ButtonNames;
        CmbDispBtn.ItemsSource = ButtonNames;
        CmbSaveBBtn.ItemsSource = ButtonNames;
        CmbNavBBtn.ItemsSource = ButtonNames;

        CmbSaveKey.ItemsSource = FKeyNames;
        CmbNavKey.ItemsSource = FKeyNames;
        CmbMonNavKey.ItemsSource = FKeyNames;
        CmbDispKey.ItemsSource = FKeyNames;
        CmbSaveBKey.ItemsSource = FKeyNames;
        CmbNavBKey.ItemsSource = FKeyNames;
    }

    private void LoadCurrentSettings()
    {
        var s = _settingsService.Current;

        LoadShortcutUI(s.SaveShortcut,
            ChkSaveMouseEnabled, PnlSaveMouse,
            ChkSaveCtrl, ChkSaveAlt, ChkSaveShift, ChkSaveWin, CmbSaveBtn,
            ChkSaveKeyboardEnabled, PnlSaveKeyboard, CmbSaveKey);

        LoadShortcutUI(s.NavigateShortcut,
            ChkNavMouseEnabled, PnlNavMouse,
            ChkNavCtrl, ChkNavAlt, ChkNavShift, ChkNavWin, CmbNavBtn,
            ChkNavKeyboardEnabled, PnlNavKeyboard, CmbNavKey);

        LoadShortcutUI(s.NavigateCurrentMonitorShortcut,
            ChkMonNavMouseEnabled, PnlMonNavMouse,
            ChkMonNavCtrl, ChkMonNavAlt, ChkMonNavShift, ChkMonNavWin, CmbMonNavBtn,
            ChkMonNavKeyboardEnabled, PnlMonNavKeyboard, CmbMonNavKey);

        LoadShortcutUI(s.DisplayDeleteShortcut,
            ChkDispMouseEnabled, PnlDispMouse,
            ChkDispCtrl, ChkDispAlt, ChkDispShift, ChkDispWin, CmbDispBtn,
            ChkDispKeyboardEnabled, PnlDispKeyboard, CmbDispKey);

        LoadShortcutUI(s.SaveShortcutB,
            ChkSaveBMouseEnabled, PnlSaveBMouse,
            ChkSaveBCtrl, ChkSaveBAlt, ChkSaveBShift, ChkSaveBWin, CmbSaveBBtn,
            ChkSaveBKeyboardEnabled, PnlSaveBKeyboard, CmbSaveBKey);

        LoadShortcutUI(s.NavigateShortcutB,
            ChkNavBMouseEnabled, PnlNavBMouse,
            ChkNavBCtrl, ChkNavBAlt, ChkNavBShift, ChkNavBWin, CmbNavBBtn,
            ChkNavBKeyboardEnabled, PnlNavBKeyboard, CmbNavBKey);

        // 色
        _saveColor   = s.SaveCircleColor;
        _saveColorB  = s.SaveCircleColorB;
        _trailColor  = s.TrailColor;
        _trailColorB = s.TrailColorB;
        _markerColor  = s.MarkerColor;
        _markerColorB = s.MarkerColorB;
        SetSwatchColor(RectSaveColor,   _saveColor);
        SetSwatchColor(RectSaveColorB,  _saveColorB);
        SetSwatchColor(RectTrailColor,  _trailColor);
        SetSwatchColor(RectTrailColorB, _trailColorB);
        SetSwatchColor(RectMarkerColor,  _markerColor);
        SetSwatchColor(RectMarkerColorB, _markerColorB);
        TxtSaveColorHex.Text   = _saveColor.ToUpperInvariant();
        TxtSaveColorBHex.Text  = _saveColorB.ToUpperInvariant();
        TxtTrailColorHex.Text  = _trailColor.ToUpperInvariant();
        TxtTrailColorBHex.Text = _trailColorB.ToUpperInvariant();
        TxtMarkerColorHex.Text  = _markerColor.ToUpperInvariant();
        TxtMarkerColorBHex.Text = _markerColorB.ToUpperInvariant();

        // エフェクト ON/OFF
        ChkSaveEffectEnabled.IsChecked = s.SaveEffectEnabled;
        ChkTrailEffectEnabled.IsChecked = s.TrailEffectEnabled;
        ChkMarkerEffectEnabled.IsChecked = s.MarkerEffectEnabled;
        ChkShowDeleteModeHelp.IsChecked = s.ShowDeleteModeHelp;

        // 軌跡エフェクト詳細スライダー（クランプして反映）
        SldTrailThickness.Value = Math.Clamp(s.TrailThickness, SldTrailThickness.Minimum, SldTrailThickness.Maximum);
        SldTrailDuration.Value = Math.Clamp(s.TrailDurationMs, (int)SldTrailDuration.Minimum, (int)SldTrailDuration.Maximum);
        SldTrailOpacity.Value = Math.Clamp(s.TrailOpacity, SldTrailOpacity.Minimum, SldTrailOpacity.Maximum);
        UpdateTrailValueLabels();

        // テーマ
        _currentTheme = s.UiTheme;
        if (_currentTheme == UiTheme.Dark)
        {
            ThemeDark.IsChecked = true;
        }
        else
        {
            ThemeLight.IsChecked = true;
        }
    }

    private void UpdateTrailValueLabels()
    {
        // XAML ロード中、Slider の Maximum/Minimum 設定が ValueChanged を発火するが
        // この時点では後続の TextBlock がまだ生成されていない可能性があるため、個別に null チェックする。
        if (TxtTrailThickness is not null && SldTrailThickness is not null)
            TxtTrailThickness.Text = $"{SldTrailThickness.Value:0} dp";
        if (TxtTrailDuration is not null && SldTrailDuration is not null)
            TxtTrailDuration.Text = $"{SldTrailDuration.Value:0} ms";
        if (TxtTrailOpacity is not null && SldTrailOpacity is not null)
            TxtTrailOpacity.Text = $"{SldTrailOpacity.Value:0.00}";
    }

    private void OnTrailThicknessChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => UpdateTrailValueLabels();
    private void OnTrailDurationChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => UpdateTrailValueLabels();
    private void OnTrailOpacityChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => UpdateTrailValueLabels();

    private static void LoadShortcutUI(ActionShortcut shortcut,
        CheckBox chkMouseEnabled, System.Windows.Controls.Panel pnlMouse,
        CheckBox chkCtrl, CheckBox chkAlt, CheckBox chkShift, CheckBox chkWin, ComboBox cmbBtn,
        CheckBox chkKeyboardEnabled, System.Windows.Controls.Panel pnlKeyboard, ComboBox cmbKey)
    {
        bool mouseOn    = shortcut.EnabledTriggers.HasFlag(TriggerType.Mouse);
        bool keyboardOn = shortcut.EnabledTriggers.HasFlag(TriggerType.Keyboard);

        chkMouseEnabled.IsChecked = mouseOn;
        pnlMouse.Visibility = mouseOn ? Visibility.Visible : Visibility.Collapsed;
        chkCtrl.IsChecked = shortcut.Modifiers.HasFlag(ModifierKeyFlags.Control);
        chkAlt.IsChecked = shortcut.Modifiers.HasFlag(ModifierKeyFlags.Alt);
        chkShift.IsChecked = shortcut.Modifiers.HasFlag(ModifierKeyFlags.Shift);
        chkWin.IsChecked = shortcut.Modifiers.HasFlag(ModifierKeyFlags.Windows);
        cmbBtn.SelectedItem = ButtonTypeToName(shortcut.MouseButton);

        chkKeyboardEnabled.IsChecked = keyboardOn;
        pnlKeyboard.Visibility = keyboardOn ? Visibility.Visible : Visibility.Collapsed;
        int idx = Array.IndexOf(FKeyCodes, shortcut.VirtualKeyCode);
        cmbKey.SelectedIndex = idx >= 0 ? idx : 0;
    }

    private void OnTriggerEnabledChanged(object sender, RoutedEventArgs e)
    {
        if (PnlSaveMouse is null) return;

        UpdateTriggerPanel(ChkSaveMouseEnabled, PnlSaveMouse);
        UpdateTriggerPanel(ChkSaveKeyboardEnabled, PnlSaveKeyboard);
        UpdateTriggerPanel(ChkNavMouseEnabled, PnlNavMouse);
        UpdateTriggerPanel(ChkNavKeyboardEnabled, PnlNavKeyboard);
        UpdateTriggerPanel(ChkMonNavMouseEnabled, PnlMonNavMouse);
        UpdateTriggerPanel(ChkMonNavKeyboardEnabled, PnlMonNavKeyboard);
        UpdateTriggerPanel(ChkDispMouseEnabled, PnlDispMouse);
        UpdateTriggerPanel(ChkDispKeyboardEnabled, PnlDispKeyboard);
        UpdateTriggerPanel(ChkSaveBMouseEnabled, PnlSaveBMouse);
        UpdateTriggerPanel(ChkSaveBKeyboardEnabled, PnlSaveBKeyboard);
        UpdateTriggerPanel(ChkNavBMouseEnabled, PnlNavBMouse);
        UpdateTriggerPanel(ChkNavBKeyboardEnabled, PnlNavBKeyboard);
    }

    private static void UpdateTriggerPanel(CheckBox chk, System.Windows.Controls.Panel pnl)
    {
        pnl.Visibility = chk.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    // ==== ページ切替 ====
    private void OnTabSettingsChecked(object sender, RoutedEventArgs e)
    {
        if (PageSettings is null) return;
        PageSettings.Visibility = Visibility.Visible;
        PageUsage.Visibility = Visibility.Collapsed;
    }

    private void OnTabUsageChecked(object sender, RoutedEventArgs e)
    {
        if (PageSettings is null) return;
        PageSettings.Visibility = Visibility.Collapsed;
        PageUsage.Visibility = Visibility.Visible;
    }

    // ==== テーマ切替 ====
    private void OnThemeLightChecked(object sender, RoutedEventArgs e)
    {
        if (!_initialized) return;
        _currentTheme = UiTheme.Light;
        ThemeManager.Apply(UiTheme.Light);
    }

    private void OnThemeDarkChecked(object sender, RoutedEventArgs e)
    {
        if (!_initialized) return;
        _currentTheme = UiTheme.Dark;
        ThemeManager.Apply(UiTheme.Dark);
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        var saveShortcut = ReadShortcutUI(
            ChkSaveMouseEnabled, ChkSaveCtrl, ChkSaveAlt, ChkSaveShift, ChkSaveWin, CmbSaveBtn,
            ChkSaveKeyboardEnabled, CmbSaveKey);
        var navShortcut = ReadShortcutUI(
            ChkNavMouseEnabled, ChkNavCtrl, ChkNavAlt, ChkNavShift, ChkNavWin, CmbNavBtn,
            ChkNavKeyboardEnabled, CmbNavKey);
        var monNavShortcut = ReadShortcutUI(
            ChkMonNavMouseEnabled, ChkMonNavCtrl, ChkMonNavAlt, ChkMonNavShift, ChkMonNavWin, CmbMonNavBtn,
            ChkMonNavKeyboardEnabled, CmbMonNavKey);
        var dispShortcut = ReadShortcutUI(
            ChkDispMouseEnabled, ChkDispCtrl, ChkDispAlt, ChkDispShift, ChkDispWin, CmbDispBtn,
            ChkDispKeyboardEnabled, CmbDispKey);
        var saveBShortcut = ReadShortcutUI(
            ChkSaveBMouseEnabled, ChkSaveBCtrl, ChkSaveBAlt, ChkSaveBShift, ChkSaveBWin, CmbSaveBBtn,
            ChkSaveBKeyboardEnabled, CmbSaveBKey);
        var navBShortcut = ReadShortcutUI(
            ChkNavBMouseEnabled, ChkNavBCtrl, ChkNavBAlt, ChkNavBShift, ChkNavBWin, CmbNavBBtn,
            ChkNavBKeyboardEnabled, CmbNavBKey);

        if (saveShortcut.EnabledTriggers == TriggerType.None ||
            navShortcut.EnabledTriggers == TriggerType.None ||
            dispShortcut.EnabledTriggers == TriggerType.None)
        {
            MessageBox.Show("座標保存・ナビゲーション・座標表示/削除の各アクションにつき、マウスボタンまたはキーボードキーを少なくとも1つ有効にしてください。",
                "CursorJump", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Set B は両方無効（=未使用）も許可。片方だけ有効はOK（保存のみ・移動のみの運用も可能）

        static bool NeedsModifier(ActionShortcut s) =>
            s.EnabledTriggers.HasFlag(TriggerType.Mouse)
            && s.Modifiers == ModifierKeyFlags.None
            && s.MouseButton is not (
                MouseButtonType.XButton1 or
                MouseButtonType.XButton2 or
                MouseButtonType.MiddleLeftChord or
                MouseButtonType.MiddleRightChord or
                MouseButtonType.MiddleDoubleClick or
                MouseButtonType.MiddleTripleClick);

        if (NeedsModifier(saveShortcut) || NeedsModifier(navShortcut) ||
            NeedsModifier(monNavShortcut) || NeedsModifier(dispShortcut) ||
            NeedsModifier(saveBShortcut) || NeedsModifier(navBShortcut))
        {
            MessageBox.Show("左/右/ホイールクリック（単押し）の場合は修飾キーを1つ以上選択してください。ホイール＋L/R、ホイール連打、戻る/進むボタンは修飾キー不要で使用できます。",
                "CursorJump", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var actions = new (string Name, ActionShortcut Shortcut)[]
        {
            ("座標保存", saveShortcut),
            ("ナビゲート（全体）", navShortcut),
            ("ナビゲート（モニタ内）", monNavShortcut),
            ("座標表示/削除", dispShortcut),
            ("座標保存（Set B）", saveBShortcut),
            ("座標移動（Set B）", navBShortcut),
        };
        for (int i = 0; i < actions.Length; i++)
        {
            for (int j = i + 1; j < actions.Length; j++)
            {
                var (mouseDup, keyboardDup) = DetectShortcutConflict(actions[i].Shortcut, actions[j].Shortcut);
                if (!mouseDup && !keyboardDup) continue;
                string kind = mouseDup && keyboardDup ? "マウス/キーボード両方の"
                            : mouseDup ? "マウス" : "キーボード";
                MessageBox.Show(
                    $"「{actions[i].Name}」と「{actions[j].Name}」の{kind}ショートカットが重複しています。どちらか片方を変更してください。",
                    "CursorJump", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        var settings = new AppSettings
        {
            SaveShortcut = saveShortcut,
            NavigateShortcut = navShortcut,
            NavigateCurrentMonitorShortcut = monNavShortcut,
            DisplayDeleteShortcut = dispShortcut,
            SaveShortcutB = saveBShortcut,
            NavigateShortcutB = navBShortcut,
            SaveCircleColor  = _saveColor,
            SaveCircleColorB = _saveColorB,
            TrailColor       = _trailColor,
            TrailColorB      = _trailColorB,
            MarkerColor      = _markerColor,
            MarkerColorB     = _markerColorB,
            SaveEffectEnabled = ChkSaveEffectEnabled.IsChecked == true,
            TrailEffectEnabled = ChkTrailEffectEnabled.IsChecked == true,
            MarkerEffectEnabled = ChkMarkerEffectEnabled.IsChecked == true,
            ShowDeleteModeHelp = ChkShowDeleteModeHelp.IsChecked == true,
            TrailThickness = SldTrailThickness.Value,
            TrailDurationMs = (int)Math.Round(SldTrailDuration.Value),
            TrailOpacity = SldTrailOpacity.Value,
            // 永続化された座標は SettingsService.Current 側を維持（OnSaveClick で上書きされないように）
            SavedCoordinatesA = _settingsService.Current.SavedCoordinatesA,
            SavedCoordinatesB = _settingsService.Current.SavedCoordinatesB,
            UiTheme = _currentTheme,
        };

        _settingsService.Save(settings);
        DialogResult = true;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        // キャンセル時はテーマも元に戻す
        ThemeManager.Apply(_settingsService.Current.UiTheme);
        DialogResult = false;
        Close();
    }

    private void OnColorRectangleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Shape shape) return;

        string currentHex;
        if      (shape == RectSaveColor)    currentHex = _saveColor;
        else if (shape == RectSaveColorB)   currentHex = _saveColorB;
        else if (shape == RectTrailColor)   currentHex = _trailColor;
        else if (shape == RectTrailColorB)  currentHex = _trailColorB;
        else if (shape == RectMarkerColor)  currentHex = _markerColor;
        else if (shape == RectMarkerColorB) currentHex = _markerColorB;
        else return;

        using var dialog = new System.Windows.Forms.ColorDialog();
        dialog.FullOpen = true;

        try
        {
            var wpfColor = (Color)ColorConverter.ConvertFromString(currentHex);
            dialog.Color = System.Drawing.Color.FromArgb(wpfColor.R, wpfColor.G, wpfColor.B);
        }
        catch { }

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            var selected = dialog.Color;
            string hex = $"#{selected.R:X2}{selected.G:X2}{selected.B:X2}";

            if      (shape == RectSaveColor)    { _saveColor   = hex; TxtSaveColorHex.Text   = hex; }
            else if (shape == RectSaveColorB)   { _saveColorB  = hex; TxtSaveColorBHex.Text  = hex; }
            else if (shape == RectTrailColor)   { _trailColor  = hex; TxtTrailColorHex.Text  = hex; }
            else if (shape == RectTrailColorB)  { _trailColorB = hex; TxtTrailColorBHex.Text = hex; }
            else if (shape == RectMarkerColor)  { _markerColor  = hex; TxtMarkerColorHex.Text  = hex; }
            else if (shape == RectMarkerColorB) { _markerColorB = hex; TxtMarkerColorBHex.Text = hex; }

            shape.Fill = new SolidColorBrush(Color.FromRgb(selected.R, selected.G, selected.B));
        }
    }

    private static void SetSwatchColor(Shape shape, string hex)
    {
        try
        {
            var converted = ColorConverter.ConvertFromString(hex);
            if (converted is Color c)
            {
                shape.Fill = new SolidColorBrush(c);
                return;
            }
        }
        catch { }
        shape.Fill = Brushes.Transparent;
    }

    private static ActionShortcut ReadShortcutUI(
        CheckBox chkMouseEnabled,
        CheckBox chkCtrl, CheckBox chkAlt, CheckBox chkShift, CheckBox chkWin, ComboBox cmbBtn,
        CheckBox chkKeyboardEnabled, ComboBox cmbKey)
    {
        var triggers = TriggerType.None;
        if (chkMouseEnabled.IsChecked == true) triggers |= TriggerType.Mouse;
        if (chkKeyboardEnabled.IsChecked == true) triggers |= TriggerType.Keyboard;

        var mod = ModifierKeyFlags.None;
        if (chkCtrl.IsChecked == true) mod |= ModifierKeyFlags.Control;
        if (chkAlt.IsChecked == true) mod |= ModifierKeyFlags.Alt;
        if (chkShift.IsChecked == true) mod |= ModifierKeyFlags.Shift;
        if (chkWin.IsChecked == true) mod |= ModifierKeyFlags.Windows;

        int idx = cmbKey.SelectedIndex;
        int vkCode = idx >= 0 && idx < FKeyCodes.Length ? FKeyCodes[idx] : NativeMethods.VK_F13;

        return new ActionShortcut
        {
            EnabledTriggers = triggers,
            Modifiers = mod,
            MouseButton = NameToButtonType(cmbBtn.SelectedItem as string),
            VirtualKeyCode = vkCode
        };
    }

    private static (bool mouse, bool keyboard) DetectShortcutConflict(ActionShortcut a, ActionShortcut b)
    {
        bool mouseDup =
            a.EnabledTriggers.HasFlag(TriggerType.Mouse) && b.EnabledTriggers.HasFlag(TriggerType.Mouse)
            && a.Modifiers == b.Modifiers && a.MouseButton == b.MouseButton;

        bool keyboardDup =
            a.EnabledTriggers.HasFlag(TriggerType.Keyboard) && b.EnabledTriggers.HasFlag(TriggerType.Keyboard)
            && a.VirtualKeyCode == b.VirtualKeyCode;

        return (mouseDup, keyboardDup);
    }

    private static string ButtonTypeToName(MouseButtonType type) => type switch
    {
        MouseButtonType.Left => "左クリック",
        MouseButtonType.Right => "右クリック",
        MouseButtonType.Middle => "ホイールクリック",
        MouseButtonType.XButton1 => "戻るボタン",
        MouseButtonType.XButton2 => "進むボタン",
        MouseButtonType.MiddleLeftChord => "ホイール＋左クリック",
        MouseButtonType.MiddleRightChord => "ホイール＋右クリック",
        MouseButtonType.MiddleDoubleClick => "ホイール2連打",
        MouseButtonType.MiddleTripleClick => "ホイール3連打",
        _ => "左クリック"
    };

    private static MouseButtonType NameToButtonType(string? name) => name switch
    {
        "右クリック" => MouseButtonType.Right,
        "ホイールクリック" => MouseButtonType.Middle,
        "戻るボタン" => MouseButtonType.XButton1,
        "進むボタン" => MouseButtonType.XButton2,
        "ホイール＋左クリック" => MouseButtonType.MiddleLeftChord,
        "ホイール＋右クリック" => MouseButtonType.MiddleRightChord,
        "ホイール2連打" => MouseButtonType.MiddleDoubleClick,
        "ホイール3連打" => MouseButtonType.MiddleTripleClick,
        _ => MouseButtonType.Left
    };
}
