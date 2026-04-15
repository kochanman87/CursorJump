using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using CursorJump.App.Models;

namespace CursorJump.App;

public partial class SettingsWindow : Window
{
    private readonly SettingsService _settingsService;

    private static readonly string[] ButtonNames = { "左クリック", "右クリック", "ホイールクリック", "戻るボタン", "進むボタン" };

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
    private string _trailColor = "#00FF00";
    private string _markerColor = "#0088FF";
    private UiTheme _currentTheme = UiTheme.Light;

    private bool _initialized;

    public SettingsWindow(SettingsService settingsService)
    {
        _settingsService = settingsService;
        InitializeComponent();
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

        CmbSaveKey.ItemsSource = FKeyNames;
        CmbNavKey.ItemsSource = FKeyNames;
        CmbMonNavKey.ItemsSource = FKeyNames;
        CmbDispKey.ItemsSource = FKeyNames;
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

        // 色
        _saveColor = s.SaveCircleColor;
        _trailColor = s.TrailColor;
        _markerColor = s.MarkerColor;
        SetSwatchColor(RectSaveColor, _saveColor);
        SetSwatchColor(RectTrailColor, _trailColor);
        SetSwatchColor(RectMarkerColor, _markerColor);
        TxtSaveColorHex.Text = _saveColor.ToUpperInvariant();
        TxtTrailColorHex.Text = _trailColor.ToUpperInvariant();
        TxtMarkerColorHex.Text = _markerColor.ToUpperInvariant();

        // エフェクト ON/OFF
        ChkSaveEffectEnabled.IsChecked = s.SaveEffectEnabled;
        ChkTrailEffectEnabled.IsChecked = s.TrailEffectEnabled;
        ChkMarkerEffectEnabled.IsChecked = s.MarkerEffectEnabled;

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

        if (saveShortcut.EnabledTriggers == TriggerType.None ||
            navShortcut.EnabledTriggers == TriggerType.None ||
            dispShortcut.EnabledTriggers == TriggerType.None)
        {
            MessageBox.Show("座標保存・ナビゲーション・座標表示/削除の各アクションにつき、マウスボタンまたはキーボードキーを少なくとも1つ有効にしてください。",
                "CursorJump", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        static bool NeedsModifier(ActionShortcut s) =>
            s.EnabledTriggers.HasFlag(TriggerType.Mouse)
            && s.Modifiers == ModifierKeyFlags.None
            && s.MouseButton is not (MouseButtonType.XButton1 or MouseButtonType.XButton2);

        if (NeedsModifier(saveShortcut) || NeedsModifier(navShortcut) ||
            NeedsModifier(monNavShortcut) || NeedsModifier(dispShortcut))
        {
            MessageBox.Show("左/右/ホイールクリックの場合は修飾キーを1つ以上選択してください。",
                "CursorJump", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (ShortcutsConflict(saveShortcut, navShortcut) ||
            ShortcutsConflict(saveShortcut, monNavShortcut) ||
            ShortcutsConflict(saveShortcut, dispShortcut) ||
            ShortcutsConflict(navShortcut, monNavShortcut) ||
            ShortcutsConflict(navShortcut, dispShortcut) ||
            ShortcutsConflict(monNavShortcut, dispShortcut))
        {
            MessageBox.Show("ショートカットの組み合わせが重複しています。",
                "CursorJump", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var settings = new AppSettings
        {
            SaveShortcut = saveShortcut,
            NavigateShortcut = navShortcut,
            NavigateCurrentMonitorShortcut = monNavShortcut,
            DisplayDeleteShortcut = dispShortcut,
            SaveCircleColor = _saveColor,
            TrailColor = _trailColor,
            MarkerColor = _markerColor,
            SaveEffectEnabled = ChkSaveEffectEnabled.IsChecked == true,
            TrailEffectEnabled = ChkTrailEffectEnabled.IsChecked == true,
            MarkerEffectEnabled = ChkMarkerEffectEnabled.IsChecked == true,
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
        if (shape == RectSaveColor) currentHex = _saveColor;
        else if (shape == RectTrailColor) currentHex = _trailColor;
        else if (shape == RectMarkerColor) currentHex = _markerColor;
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

            if (shape == RectSaveColor)
            {
                _saveColor = hex;
                TxtSaveColorHex.Text = hex;
            }
            else if (shape == RectTrailColor)
            {
                _trailColor = hex;
                TxtTrailColorHex.Text = hex;
            }
            else if (shape == RectMarkerColor)
            {
                _markerColor = hex;
                TxtMarkerColorHex.Text = hex;
            }

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

    private static bool ShortcutsConflict(ActionShortcut a, ActionShortcut b)
    {
        if (a.EnabledTriggers.HasFlag(TriggerType.Mouse) && b.EnabledTriggers.HasFlag(TriggerType.Mouse)
            && a.Modifiers == b.Modifiers && a.MouseButton == b.MouseButton)
            return true;

        if (a.EnabledTriggers.HasFlag(TriggerType.Keyboard) && b.EnabledTriggers.HasFlag(TriggerType.Keyboard)
            && a.VirtualKeyCode == b.VirtualKeyCode)
            return true;

        return false;
    }

    private static string ButtonTypeToName(MouseButtonType type) => type switch
    {
        MouseButtonType.Left => "左クリック",
        MouseButtonType.Right => "右クリック",
        MouseButtonType.Middle => "ホイールクリック",
        MouseButtonType.XButton1 => "戻るボタン",
        MouseButtonType.XButton2 => "進むボタン",
        _ => "左クリック"
    };

    private static MouseButtonType NameToButtonType(string? name) => name switch
    {
        "右クリック" => MouseButtonType.Right,
        "ホイールクリック" => MouseButtonType.Middle,
        "戻るボタン" => MouseButtonType.XButton1,
        "進むボタン" => MouseButtonType.XButton2,
        _ => MouseButtonType.Left
    };
}
