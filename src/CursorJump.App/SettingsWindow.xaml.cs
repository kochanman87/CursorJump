using System;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Navigation;
using System.Windows.Shapes;
using CursorJump.App.Models;

namespace CursorJump.App;

public partial class SettingsWindow : Window
{
    private readonly SettingsService _settingsService;
    private readonly UpdateService? _updateService;

    /// <summary>
    /// ComboBox の表示用 wrapper。enum 値を保持しつつ、ToString() で現在言語のリソース名を返すため、
    /// 言語切替時に再評価される（CollectionView の Refresh 経由）。
    /// </summary>
    public sealed class ButtonOption
    {
        public MouseButtonType Value { get; }
        public string ResourceKey { get; }
        public ButtonOption(MouseButtonType value, string key)
        {
            Value = value;
            ResourceKey = key;
        }
        public override string ToString() => Loc.Get(ResourceKey);
    }

    private static readonly ButtonOption[] ButtonOptions =
    {
        new(MouseButtonType.Left,              "Str.Button.Left"),
        new(MouseButtonType.Right,             "Str.Button.Right"),
        new(MouseButtonType.Middle,            "Str.Button.Middle"),
        new(MouseButtonType.XButton1,          "Str.Button.XButton1"),
        new(MouseButtonType.XButton2,          "Str.Button.XButton2"),
        new(MouseButtonType.MiddleLeftChord,   "Str.Button.MiddleLeftChord"),
        new(MouseButtonType.MiddleRightChord,  "Str.Button.MiddleRightChord"),
        new(MouseButtonType.MiddleDoubleClick, "Str.Button.MiddleDoubleClick"),
        new(MouseButtonType.MiddleTripleClick, "Str.Button.MiddleTripleClick"),
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
    private UiLanguage _currentLanguage = UiLanguage.Auto;

    private bool _initialized;

    public SettingsWindow(SettingsService settingsService) : this(settingsService, null) { }

    public SettingsWindow(SettingsService settingsService, UpdateService? updateService)
    {
        _settingsService = settingsService;
        _updateService = updateService;
        InitializeComponent();
        var v = Assembly.GetExecutingAssembly().GetName().Version;
        if (v is not null)
        {
            Title = string.Format(Loc.Get("Str.Settings.TitleFormat"), v.Major, v.Minor, v.Build);
        }
        PopulateComboBoxes();
        LoadCurrentSettings();
        LocalizationManager.LanguageChanged += OnLanguageChanged;
        Closed += (_, _) => LocalizationManager.LanguageChanged -= OnLanguageChanged;
        _initialized = true;
    }

    private void OnLanguageChanged()
    {
        // Title はバインド経由でなく直接設定しているため再生成する
        var v = Assembly.GetExecutingAssembly().GetName().Version;
        if (v is not null)
        {
            Title = string.Format(Loc.Get("Str.Settings.TitleFormat"), v.Major, v.Minor, v.Build);
        }
        // ButtonOption.ToString() は Loc.Get を呼ぶが、ComboBox は変更通知を受け取らないので強制リフレッシュ
        RefreshButtonComboBoxes();
    }

    private void RefreshButtonComboBoxes()
    {
        foreach (var combo in new[] { CmbSaveBtn, CmbNavBtn, CmbMonNavBtn, CmbDispBtn, CmbSaveBBtn, CmbNavBBtn })
        {
            var selected = combo.SelectedItem;
            combo.ItemsSource = null;
            combo.ItemsSource = ButtonOptions;
            combo.SelectedItem = selected;
        }
    }

    private void PopulateComboBoxes()
    {
        CmbSaveBtn.ItemsSource = ButtonOptions;
        CmbNavBtn.ItemsSource = ButtonOptions;
        CmbMonNavBtn.ItemsSource = ButtonOptions;
        CmbDispBtn.ItemsSource = ButtonOptions;
        CmbSaveBBtn.ItemsSource = ButtonOptions;
        CmbNavBBtn.ItemsSource = ButtonOptions;

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

        // 言語（Auto は実解決値で表示）
        _currentLanguage = s.UiLanguage;
        var resolved = LocalizationManager.Resolve(_currentLanguage);
        if (resolved == UiLanguage.English)
        {
            LangEn.IsChecked = true;
        }
        else
        {
            LangJa.IsChecked = true;
        }

        // 情報タブ
        LoadAboutSection(s);
    }

    private void LoadAboutSection(AppSettings s)
    {
        var v = Assembly.GetExecutingAssembly().GetName().Version;
        AboutVersionText.Text = string.Format(
            CultureInfo.CurrentCulture,
            Loc.Get("Str.About.Version"),
            v is null ? "-" : $"{v.Major}.{v.Minor}.{v.Build}");

        AutoUpdateToggle.IsChecked = s.AutoUpdateEnabled;
        UpdateLastCheckedLabel(s.LastUpdateCheckUtc);
    }

    private void UpdateLastCheckedLabel(string isoUtc)
    {
        if (string.IsNullOrEmpty(isoUtc))
        {
            AboutLastCheckedText.Text = string.Format(CultureInfo.CurrentCulture, Loc.Get("Str.About.LastChecked"), "-");
            return;
        }
        if (DateTime.TryParse(isoUtc, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var dt))
        {
            AboutLastCheckedText.Text = string.Format(
                CultureInfo.CurrentCulture,
                Loc.Get("Str.About.LastChecked"),
                dt.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture));
        }
        else
        {
            AboutLastCheckedText.Text = string.Format(CultureInfo.CurrentCulture, Loc.Get("Str.About.LastChecked"), isoUtc);
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

    private void OnTrailExpandChecked(object sender, RoutedEventArgs e)
    {
        PnlTrailDetails.Visibility = Visibility.Visible;
        TxtTrailExpandIcon.Text = ""; // ChevronUp
    }

    private void OnTrailExpandUnchecked(object sender, RoutedEventArgs e)
    {
        PnlTrailDetails.Visibility = Visibility.Collapsed;
        TxtTrailExpandIcon.Text = ""; // ChevronDown
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
        cmbBtn.SelectedItem = FindButtonOption(shortcut.MouseButton);

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
        if (PageAbout is not null) PageAbout.Visibility = Visibility.Collapsed;
    }

    private void OnTabUsageChecked(object sender, RoutedEventArgs e)
    {
        if (PageSettings is null) return;
        PageSettings.Visibility = Visibility.Collapsed;
        PageUsage.Visibility = Visibility.Visible;
        if (PageAbout is not null) PageAbout.Visibility = Visibility.Collapsed;
    }

    private void OnTabAboutChecked(object sender, RoutedEventArgs e)
    {
        if (PageSettings is null || PageAbout is null) return;
        PageSettings.Visibility = Visibility.Collapsed;
        PageUsage.Visibility = Visibility.Collapsed;
        PageAbout.Visibility = Visibility.Visible;
    }

    private async void OnCheckNowClick(object sender, RoutedEventArgs e)
    {
        if (_updateService == null) return;
        CheckNowButton.IsEnabled = false;
        try
        {
            var (info, status) = await _updateService.CheckForUpdatesAsync();
            UpdateLastCheckedLabel(_settingsService.Current.LastUpdateCheckUtc);

            if (status == UpdateCheckStatus.UpdateAvailable && info != null)
            {
                var dlg = new UpdateDialog(_updateService, info);
                dlg.ShowDialog();
                return;
            }

            string msg = status switch
            {
                UpdateCheckStatus.UpToDate     => Loc.Get("Str.Update.NoUpdate"),
                UpdateCheckStatus.NotInstalled => Loc.Get("Str.Update.NotInstalled"),
                _                              => Loc.Get("Str.Update.NetworkError"),
            };
            MessageBox.Show(msg, Loc.Get("Str.AppName"), MessageBoxButton.OK,
                status == UpdateCheckStatus.NetworkError ? MessageBoxImage.Warning : MessageBoxImage.Information);
        }
        finally
        {
            CheckNowButton.IsEnabled = true;
        }
    }

    private void OnHyperlinkRequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }
        catch (Exception ex)
        {
            DebugLog.Write($"OnHyperlinkRequestNavigate failed: {ex.GetType().Name}: {ex.Message}");
        }
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

    // ==== 言語切替 ====
    private void OnLangJaChecked(object sender, RoutedEventArgs e)
    {
        if (!_initialized) return;
        _currentLanguage = UiLanguage.Japanese;
        LocalizationManager.Apply(UiLanguage.Japanese);
    }

    private void OnLangEnChecked(object sender, RoutedEventArgs e)
    {
        if (!_initialized) return;
        _currentLanguage = UiLanguage.English;
        LocalizationManager.Apply(UiLanguage.English);
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
            MessageBox.Show(Loc.Get("Str.Settings.Validation.NoTrigger"),
                Loc.Get("Str.AppName"), MessageBoxButton.OK, MessageBoxImage.Warning);
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
            MessageBox.Show(Loc.Get("Str.Settings.Validation.NoModifier"),
                Loc.Get("Str.AppName"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var actions = new (string NameKey, ActionShortcut Shortcut)[]
        {
            ("Str.Settings.Action.Save", saveShortcut),
            ("Str.Settings.Action.NavigateAll", navShortcut),
            ("Str.Settings.Action.NavigateMonitor", monNavShortcut),
            ("Str.Settings.Action.Display", dispShortcut),
            ("Str.Settings.Action.SaveB", saveBShortcut),
            ("Str.Settings.Action.NavigateB", navBShortcut),
        };
        for (int i = 0; i < actions.Length; i++)
        {
            for (int j = i + 1; j < actions.Length; j++)
            {
                var (mouseDup, keyboardDup) = DetectShortcutConflict(actions[i].Shortcut, actions[j].Shortcut);
                if (!mouseDup && !keyboardDup) continue;
                string kind = mouseDup && keyboardDup
                    ? Loc.Get("Str.Settings.Validation.ConflictKind.Both")
                    : mouseDup
                        ? Loc.Get("Str.Settings.Validation.ConflictKind.Mouse")
                        : Loc.Get("Str.Settings.Validation.ConflictKind.Keyboard");
                MessageBox.Show(
                    string.Format(
                        Loc.Get("Str.Settings.Validation.ConflictFormat"),
                        Loc.Get(actions[i].NameKey),
                        Loc.Get(actions[j].NameKey),
                        kind),
                    Loc.Get("Str.AppName"), MessageBoxButton.OK, MessageBoxImage.Warning);
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
            UiLanguage = _currentLanguage,
            // 自動更新（情報タブのトグル）。LastUpdateCheckUtc / SkippedVersion は
            // UpdateService が書き込む内部状態なので Current から維持する。
            AutoUpdateEnabled = AutoUpdateToggle.IsChecked == true,
            LastUpdateCheckUtc = _settingsService.Current.LastUpdateCheckUtc,
            SkippedVersion = _settingsService.Current.SkippedVersion,
        };

        if (!_settingsService.Save(settings))
        {
            MessageBox.Show(
                Loc.Get("Str.MessageBox.SettingsSaveFailed"),
                Loc.Get("Str.AppName"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }
        DialogResult = true;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        // キャンセル時はテーマ・言語を元に戻す
        ThemeManager.Apply(_settingsService.Current.UiTheme);
        LocalizationManager.Apply(_settingsService.Current.UiLanguage);
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
            MouseButton = (cmbBtn.SelectedItem as ButtonOption)?.Value ?? MouseButtonType.Left,
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

    private static ButtonOption FindButtonOption(MouseButtonType type)
    {
        foreach (var opt in ButtonOptions)
        {
            if (opt.Value == type) return opt;
        }
        return ButtonOptions[0];
    }
}
