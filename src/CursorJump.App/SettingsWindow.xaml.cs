using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using CursorJump.App.Models;

namespace CursorJump.App;

public partial class SettingsWindow : Window
{
    private readonly SettingsService _settingsService;

    private static readonly string[] ButtonNames = { "左クリック", "右クリック", "ホイールクリック" };

    private static readonly Dictionary<string, int> KeyMap = new()
    {
        { "Home", 0x24 }, { "End", 0x23 }, { "Insert", 0x2D }, { "Delete", 0x2E },
        { "F1", 0x70 }, { "F2", 0x71 }, { "F3", 0x72 }, { "F4", 0x73 },
        { "F5", 0x74 }, { "F6", 0x75 }, { "F7", 0x76 }, { "F8", 0x77 },
        { "F9", 0x78 }, { "F10", 0x79 }, { "F11", 0x7A }, { "F12", 0x7B }
    };

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
        CmbHkKey.ItemsSource = KeyMap.Keys;
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
        TxtSaveColor.Text = s.SaveCircleColor;
        TxtTrailColor.Text = s.TrailColor;
        TxtMarkerColor.Text = s.MarkerColor;

        // ホットキー
        ChkHkCtrl.IsChecked = (s.CenterJumpModifiers & NativeMethods.MOD_CONTROL) != 0;
        ChkHkAlt.IsChecked = (s.CenterJumpModifiers & NativeMethods.MOD_ALT) != 0;
        ChkHkShift.IsChecked = (s.CenterJumpModifiers & NativeMethods.MOD_SHIFT) != 0;
        ChkHkWin.IsChecked = (s.CenterJumpModifiers & NativeMethods.MOD_WIN) != 0;

        string? keyName = null;
        foreach (var kv in KeyMap)
        {
            if (kv.Value == s.CenterJumpKey) { keyName = kv.Key; break; }
        }
        CmbHkKey.SelectedItem = keyName ?? "Home";
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

        // バリデーション: 修飾キーが1つ以上
        if (saveShortcut.Modifiers == ModifierKeyFlags.None ||
            navShortcut.Modifiers == ModifierKeyFlags.None ||
            dispShortcut.Modifiers == ModifierKeyFlags.None)
        {
            MessageBox.Show("各アクションの修飾キーを1つ以上選択してください。", "CursorJump", MessageBoxButton.OK, MessageBoxImage.Warning);
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

        // ホットキー修飾キー
        int hkMod = 0;
        if (ChkHkCtrl.IsChecked == true) hkMod |= NativeMethods.MOD_CONTROL;
        if (ChkHkAlt.IsChecked == true) hkMod |= NativeMethods.MOD_ALT;
        if (ChkHkShift.IsChecked == true) hkMod |= NativeMethods.MOD_SHIFT;
        if (ChkHkWin.IsChecked == true) hkMod |= NativeMethods.MOD_WIN;

        if (hkMod == 0)
        {
            MessageBox.Show("中央ジャンプの修飾キーを1つ以上選択してください。", "CursorJump", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        int hkKey = 0x24;
        if (CmbHkKey.SelectedItem is string selectedKey && KeyMap.TryGetValue(selectedKey, out int vk))
        {
            hkKey = vk;
        }

        var settings = new AppSettings
        {
            SaveShortcut = saveShortcut,
            NavigateShortcut = navShortcut,
            DisplayDeleteShortcut = dispShortcut,
            SaveCircleColor = TxtSaveColor.Text,
            TrailColor = TxtTrailColor.Text,
            MarkerColor = TxtMarkerColor.Text,
            CenterJumpModifiers = hkMod,
            CenterJumpKey = hkKey
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

    private void OnColorTextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox textBox) return;

        Rectangle? preview = null;
        if (textBox == TxtSaveColor) preview = RectSaveColor;
        else if (textBox == TxtTrailColor) preview = RectTrailColor;
        else if (textBox == TxtMarkerColor) preview = RectMarkerColor;

        if (preview is null) return;

        try
        {
            var converted = ColorConverter.ConvertFromString(textBox.Text);
            if (converted is Color c)
            {
                preview.Fill = new SolidColorBrush(c);
                return;
            }
        }
        catch { }

        preview.Fill = Brushes.Transparent;
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
        _ => "左クリック"
    };

    private static MouseButtonType NameToButtonType(string? name) => name switch
    {
        "右クリック" => MouseButtonType.Right,
        "ホイールクリック" => MouseButtonType.Middle,
        _ => MouseButtonType.Left
    };
}
