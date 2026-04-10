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

    private string _saveColor = "#FF0000";
    private string _trailColor = "#00FF00";
    private string _markerColor = "#0088FF";

    public SettingsWindow(SettingsService settingsService)
    {
        _settingsService = settingsService;
        InitializeComponent();
        PopulateComboBoxes();
        LoadCurrentSettings();
    }

    private void PopulateComboBoxes()
    {
        CmbSaveBtn.ItemsSource = ButtonNames;
        CmbNavBtn.ItemsSource = ButtonNames;
        CmbDispBtn.ItemsSource = ButtonNames;
    }

    private void LoadCurrentSettings()
    {
        var s = _settingsService.Current;

        // 座標保存
        LoadShortcutUI(s.SaveShortcut, ChkSaveCtrl, ChkSaveAlt, ChkSaveShift, ChkSaveWin, CmbSaveBtn);
        // ナビゲーション
        LoadShortcutUI(s.NavigateShortcut, ChkNavCtrl, ChkNavAlt, ChkNavShift, ChkNavWin, CmbNavBtn);
        // 表示/削除
        LoadShortcutUI(s.DisplayDeleteShortcut, ChkDispCtrl, ChkDispAlt, ChkDispShift, ChkDispWin, CmbDispBtn);

        // 色
        _saveColor = s.SaveCircleColor;
        _trailColor = s.TrailColor;
        _markerColor = s.MarkerColor;
        SetRectangleColor(RectSaveColor, _saveColor);
        SetRectangleColor(RectTrailColor, _trailColor);
        SetRectangleColor(RectMarkerColor, _markerColor);
    }

    private static void LoadShortcutUI(ActionShortcut shortcut,
        CheckBox chkCtrl, CheckBox chkAlt, CheckBox chkShift, CheckBox chkWin, ComboBox cmbBtn)
    {
        chkCtrl.IsChecked = shortcut.Modifiers.HasFlag(ModifierKeyFlags.Control);
        chkAlt.IsChecked = shortcut.Modifiers.HasFlag(ModifierKeyFlags.Alt);
        chkShift.IsChecked = shortcut.Modifiers.HasFlag(ModifierKeyFlags.Shift);
        chkWin.IsChecked = shortcut.Modifiers.HasFlag(ModifierKeyFlags.Windows);
        cmbBtn.SelectedItem = ButtonTypeToName(shortcut.MouseButton);
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        // ショートカット読み取り
        var saveShortcut = ReadShortcutUI(ChkSaveCtrl, ChkSaveAlt, ChkSaveShift, ChkSaveWin, CmbSaveBtn);
        var navShortcut = ReadShortcutUI(ChkNavCtrl, ChkNavAlt, ChkNavShift, ChkNavWin, CmbNavBtn);
        var dispShortcut = ReadShortcutUI(ChkDispCtrl, ChkDispAlt, ChkDispShift, ChkDispWin, CmbDispBtn);

        // バリデーション: Left/Right/Middle は修飾キーが1つ以上必要（XButtonは不要）
        static bool NeedsModifier(ActionShortcut s) =>
            s.Modifiers == ModifierKeyFlags.None
            && s.MouseButton is not (MouseButtonType.XButton1 or MouseButtonType.XButton2);

        if (NeedsModifier(saveShortcut) || NeedsModifier(navShortcut) || NeedsModifier(dispShortcut))
        {
            MessageBox.Show("左/右/ホイールクリックの場合は修飾キーを1つ以上選択してください。", "CursorJump", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // バリデーション: ショートカットの重複チェック（修飾キー+マウスボタンの組み合わせ）
        if (ShortcutsConflict(saveShortcut, navShortcut) ||
            ShortcutsConflict(saveShortcut, dispShortcut) ||
            ShortcutsConflict(navShortcut, dispShortcut))
        {
            MessageBox.Show("ショートカットの組み合わせ（修飾キー+マウスボタン）が重複しています。",
                "CursorJump", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var settings = new AppSettings
        {
            SaveShortcut = saveShortcut,
            NavigateShortcut = navShortcut,
            DisplayDeleteShortcut = dispShortcut,
            SaveCircleColor = _saveColor,
            TrailColor = _trailColor,
            MarkerColor = _markerColor,
        };

        _settingsService.Save(settings);
        DialogResult = true;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void OnColorRectangleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Rectangle rect) return;

        string currentHex;
        if (rect == RectSaveColor) currentHex = _saveColor;
        else if (rect == RectTrailColor) currentHex = _trailColor;
        else if (rect == RectMarkerColor) currentHex = _markerColor;
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

            if (rect == RectSaveColor) _saveColor = hex;
            else if (rect == RectTrailColor) _trailColor = hex;
            else if (rect == RectMarkerColor) _markerColor = hex;

            rect.Fill = new SolidColorBrush(Color.FromRgb(selected.R, selected.G, selected.B));
        }
    }

    private static void SetRectangleColor(Rectangle rect, string hex)
    {
        try
        {
            var converted = ColorConverter.ConvertFromString(hex);
            if (converted is Color c)
            {
                rect.Fill = new SolidColorBrush(c);
                return;
            }
        }
        catch { }
        rect.Fill = Brushes.Transparent;
    }

    private static ActionShortcut ReadShortcutUI(
        CheckBox chkCtrl, CheckBox chkAlt, CheckBox chkShift, CheckBox chkWin, ComboBox cmbBtn)
    {
        var mod = ModifierKeyFlags.None;
        if (chkCtrl.IsChecked == true) mod |= ModifierKeyFlags.Control;
        if (chkAlt.IsChecked == true) mod |= ModifierKeyFlags.Alt;
        if (chkShift.IsChecked == true) mod |= ModifierKeyFlags.Shift;
        if (chkWin.IsChecked == true) mod |= ModifierKeyFlags.Windows;

        return new ActionShortcut
        {
            Modifiers = mod,
            MouseButton = NameToButtonType(cmbBtn.SelectedItem as string)
        };
    }

    private static bool ShortcutsConflict(ActionShortcut a, ActionShortcut b)
    {
        return a.Modifiers == b.Modifiers && a.MouseButton == b.MouseButton;
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
