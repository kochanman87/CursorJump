using System;
using System.Collections.Generic;
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
    private readonly LicenseService? _licenseService;

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

    // 既定の ButtonOption セット。v1.6.1 で WheelUp/WheelDown を UI から除外し、MouseWheel に統合。
    // 旧 settings.json に WheelUp/WheelDown が残っているケースだけ BuildButtonOptions で動的に追加表示する。
    private static readonly ButtonOption[] DefaultButtonOptions =
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
        new(MouseButtonType.MouseWheel,        "Str.Button.MouseWheel"),
    };

    /// <summary>
    /// 各 ComboBox 用のオプション配列を構築する。
    /// 現在値が旧 WheelUp/WheelDown のときだけ、その選択肢を動的に末尾に追加して表示を維持する
    /// (移行は行わず保存形式は維持。新規割当は「マウスホイール」しか選べない)。
    /// </summary>
    private static ButtonOption[] BuildButtonOptions(MouseButtonType currentValue)
    {
        if (currentValue == MouseButtonType.WheelUp || currentValue == MouseButtonType.WheelDown)
        {
            string key = currentValue == MouseButtonType.WheelUp ? "Str.Button.WheelUp" : "Str.Button.WheelDown";
            var extended = new ButtonOption[DefaultButtonOptions.Length + 1];
            Array.Copy(DefaultButtonOptions, extended, DefaultButtonOptions.Length);
            extended[^1] = new ButtonOption(currentValue, key);
            return extended;
        }
        return DefaultButtonOptions;
    }

    // ===== キーボードトリガー（任意キー+修飾キー）の状態管理 =====
    // 各 ActionShortcut の VirtualKeyCode はキーキャプチャ UI で更新される。
    // 修飾キー (ChkXxxCtrl/Alt/Shift/Win) は既存のマウス側と共通なので、
    // KeyboardSlotUI はラベル再描画と VK 保持のみ担当する。
    private readonly Dictionary<string, KeyboardSlotUI> _keyboardSlots = new();
    private string? _recordingTarget;  // 記録中のターゲット ("Save"/"Nav"/etc)

    private sealed class KeyboardSlotUI
    {
        public string Tag { get; }
        public TextBlock Label { get; }
        public Button RecordButton { get; }
        public CheckBox ChkCtrl { get; }
        public CheckBox ChkAlt { get; }
        public CheckBox ChkShift { get; }
        public CheckBox ChkWin { get; }
        public CheckBox ChkKeyboardEnabled { get; }
        public int VirtualKeyCode;
        public string OriginalRecordButtonText = "";

        public KeyboardSlotUI(string tag, TextBlock label, Button recordBtn,
            CheckBox c, CheckBox a, CheckBox s, CheckBox w, CheckBox chkKeyboardEnabled)
        {
            Tag = tag; Label = label; RecordButton = recordBtn;
            ChkCtrl = c; ChkAlt = a; ChkShift = s; ChkWin = w;
            ChkKeyboardEnabled = chkKeyboardEnabled;
        }

        public ModifierKeyFlags GetModifiers()
        {
            var m = ModifierKeyFlags.None;
            if (ChkCtrl.IsChecked == true)  m |= ModifierKeyFlags.Control;
            if (ChkAlt.IsChecked == true)   m |= ModifierKeyFlags.Alt;
            if (ChkShift.IsChecked == true) m |= ModifierKeyFlags.Shift;
            if (ChkWin.IsChecked == true)   m |= ModifierKeyFlags.Windows;
            return m;
        }

        public void SetModifiers(ModifierKeyFlags m)
        {
            ChkCtrl.IsChecked  = m.HasFlag(ModifierKeyFlags.Control);
            ChkAlt.IsChecked   = m.HasFlag(ModifierKeyFlags.Alt);
            ChkShift.IsChecked = m.HasFlag(ModifierKeyFlags.Shift);
            ChkWin.IsChecked   = m.HasFlag(ModifierKeyFlags.Windows);
        }

        public void RefreshLabel()
        {
            if (VirtualKeyCode == 0)
            {
                Label.Text = Loc.Get("Str.Settings.KeyUnassigned");
                return;
            }
            var sc = new ActionShortcut
            {
                EnabledTriggers = TriggerType.Keyboard,
                VirtualKeyCode = VirtualKeyCode,
                Modifiers = GetModifiers()
            };
            Label.Text = ShortcutFormatter.FormatKeyboard(sc);
        }
    }

    private string _saveColor = "#FF0000";
    private string _saveColorB = "#FF8800";
    private string _trailColor = "#00FF00";
    private string _trailColorB = "#FF8800";
    private string _markerColor = "#0088FF";
    private string _markerColorB = "#FF8800";
    private UiTheme _currentTheme = UiTheme.Light;
    private UiLanguage _currentLanguage = UiLanguage.Auto;

    private bool _initialized;

    public SettingsWindow(SettingsService settingsService) : this(settingsService, null, null) { }

    public SettingsWindow(SettingsService settingsService, UpdateService? updateService) : this(settingsService, updateService, null) { }

    public SettingsWindow(SettingsService settingsService, UpdateService? updateService, LicenseService? licenseService)
    {
        _settingsService = settingsService;
        _updateService = updateService;
        _licenseService = licenseService;
        InitializeComponent();
        var v = Assembly.GetExecutingAssembly().GetName().Version;
        if (v is not null)
        {
            Title = string.Format(Loc.Get("Str.Settings.TitleFormat"), v.Major, v.Minor, v.Build);
        }
        PopulateComboBoxes();
        InitKeyboardSlots();
        LoadCurrentSettings();
        UpdateLicenseUI();
        LocalizationManager.LanguageChanged += OnLanguageChanged;
        Closed += (_, _) => LocalizationManager.LanguageChanged -= OnLanguageChanged;
        Loaded += OnSettingsWindowLoaded;
        _initialized = true;
    }

    /// <summary>
    /// 起動時に作業領域を超えないようにウィンドウサイズをクランプする。
    /// 13" / 14" ノート PC (1920x1080 / 150% DPI) では既定 820dp の高さが画面に収まらないため。
    /// </summary>
    private void OnSettingsWindowLoaded(object sender, RoutedEventArgs e)
    {
        var workArea = SystemParameters.WorkArea;
        double maxH = workArea.Height * 0.95;
        double maxW = workArea.Width * 0.95;
        if (Height > maxH) Height = maxH;
        if (Width > maxW) Width = maxW;
        // 中央寄せ直し（CenterScreen でも初期 Height で配置済みなのでクランプ後にずれる）
        Left = workArea.Left + (workArea.Width - Width) / 2;
        Top  = workArea.Top  + (workArea.Height - Height) / 2;
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
        // キーボードラベル（"（未割当）" など）も Loc.Get 経由なので再描画
        foreach (var slot in _keyboardSlots.Values) slot.RefreshLabel();
        // ライセンスステータステキストも DynamicResource ではなく Loc.Get で設定しているため再描画する
        UpdateLicenseUI();
    }

    private void RefreshButtonComboBoxes()
    {
        foreach (var combo in new[] { CmbSaveBtn, CmbNavBtn, CmbMonNavBtn, CmbDispBtn, CmbSaveBBtn, CmbNavBBtn })
        {
            var selected = combo.SelectedItem as ButtonOption;
            var current = selected?.Value ?? MouseButtonType.Left;
            combo.ItemsSource = null;
            combo.ItemsSource = BuildButtonOptions(current);
            if (selected is not null)
                combo.SelectedItem = FindButtonOptionIn((ButtonOption[])combo.ItemsSource, current);
        }
    }

    private void PopulateComboBoxes()
    {
        CmbSaveBtn.ItemsSource = DefaultButtonOptions;
        CmbNavBtn.ItemsSource = DefaultButtonOptions;
        CmbMonNavBtn.ItemsSource = DefaultButtonOptions;
        CmbDispBtn.ItemsSource = DefaultButtonOptions;
        CmbSaveBBtn.ItemsSource = DefaultButtonOptions;
        CmbNavBBtn.ItemsSource = DefaultButtonOptions;
    }

    private void InitKeyboardSlots()
    {
        _keyboardSlots["Save"]   = new("Save",   LblSaveKey,   BtnSaveKeyRecord,   ChkSaveCtrl,   ChkSaveAlt,   ChkSaveShift,   ChkSaveWin,   ChkSaveKeyboardEnabled);
        _keyboardSlots["Nav"]    = new("Nav",    LblNavKey,    BtnNavKeyRecord,    ChkNavCtrl,    ChkNavAlt,    ChkNavShift,    ChkNavWin,    ChkNavKeyboardEnabled);
        _keyboardSlots["MonNav"] = new("MonNav", LblMonNavKey, BtnMonNavKeyRecord, ChkMonNavCtrl, ChkMonNavAlt, ChkMonNavShift, ChkMonNavWin, ChkMonNavKeyboardEnabled);
        _keyboardSlots["Disp"]   = new("Disp",   LblDispKey,   BtnDispKeyRecord,   ChkDispCtrl,   ChkDispAlt,   ChkDispShift,   ChkDispWin,   ChkDispKeyboardEnabled);
        _keyboardSlots["SaveB"]  = new("SaveB",  LblSaveBKey,  BtnSaveBKeyRecord,  ChkSaveBCtrl,  ChkSaveBAlt,  ChkSaveBShift,  ChkSaveBWin,  ChkSaveBKeyboardEnabled);
        _keyboardSlots["NavB"]   = new("NavB",   LblNavBKey,   BtnNavBKeyRecord,   ChkNavBCtrl,   ChkNavBAlt,   ChkNavBShift,   ChkNavBWin,   ChkNavBKeyboardEnabled);

        // 修飾キー変更時にキーボードラベルを再描画（修飾キー checkbox はマウス/キーボード共通）
        foreach (var slot in _keyboardSlots.Values)
        {
            slot.ChkCtrl.Click  += (_, _) => slot.RefreshLabel();
            slot.ChkAlt.Click   += (_, _) => slot.RefreshLabel();
            slot.ChkShift.Click += (_, _) => slot.RefreshLabel();
            slot.ChkWin.Click   += (_, _) => slot.RefreshLabel();
        }
    }

    private void LoadCurrentSettings()
    {
        var s = _settingsService.Current;

        LoadShortcutUI(s.SaveShortcut, "Save",
            ChkSaveMouseEnabled, PnlSaveMouse,
            ChkSaveCtrl, ChkSaveAlt, ChkSaveShift, ChkSaveWin, CmbSaveBtn,
            ChkSaveKeyboardEnabled, PnlSaveKeyboard);

        LoadShortcutUI(s.NavigateShortcut, "Nav",
            ChkNavMouseEnabled, PnlNavMouse,
            ChkNavCtrl, ChkNavAlt, ChkNavShift, ChkNavWin, CmbNavBtn,
            ChkNavKeyboardEnabled, PnlNavKeyboard);

        LoadShortcutUI(s.NavigateCurrentMonitorShortcut, "MonNav",
            ChkMonNavMouseEnabled, PnlMonNavMouse,
            ChkMonNavCtrl, ChkMonNavAlt, ChkMonNavShift, ChkMonNavWin, CmbMonNavBtn,
            ChkMonNavKeyboardEnabled, PnlMonNavKeyboard);

        LoadShortcutUI(s.DisplayDeleteShortcut, "Disp",
            ChkDispMouseEnabled, PnlDispMouse,
            ChkDispCtrl, ChkDispAlt, ChkDispShift, ChkDispWin, CmbDispBtn,
            ChkDispKeyboardEnabled, PnlDispKeyboard);

        LoadShortcutUI(s.SaveShortcutB, "SaveB",
            ChkSaveBMouseEnabled, PnlSaveBMouse,
            ChkSaveBCtrl, ChkSaveBAlt, ChkSaveBShift, ChkSaveBWin, CmbSaveBBtn,
            ChkSaveBKeyboardEnabled, PnlSaveBKeyboard);

        LoadShortcutUI(s.NavigateShortcutB, "NavB",
            ChkNavBMouseEnabled, PnlNavBMouse,
            ChkNavBCtrl, ChkNavBAlt, ChkNavBShift, ChkNavBWin, CmbNavBBtn,
            ChkNavBKeyboardEnabled, PnlNavBKeyboard);

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
        VerboseLoggingToggle.IsChecked = s.VerboseLogging;
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

    private void LoadShortcutUI(ActionShortcut shortcut, string tag,
        CheckBox chkMouseEnabled, System.Windows.Controls.Panel pnlMouse,
        CheckBox chkCtrl, CheckBox chkAlt, CheckBox chkShift, CheckBox chkWin, ComboBox cmbBtn,
        CheckBox chkKeyboardEnabled, System.Windows.Controls.Panel pnlKeyboard)
    {
        bool mouseOn    = shortcut.EnabledTriggers.HasFlag(TriggerType.Mouse);
        bool keyboardOn = shortcut.EnabledTriggers.HasFlag(TriggerType.Keyboard);

        chkMouseEnabled.IsChecked = mouseOn;
        pnlMouse.Visibility = mouseOn ? Visibility.Visible : Visibility.Collapsed;
        chkCtrl.IsChecked = shortcut.Modifiers.HasFlag(ModifierKeyFlags.Control);
        chkAlt.IsChecked = shortcut.Modifiers.HasFlag(ModifierKeyFlags.Alt);
        chkShift.IsChecked = shortcut.Modifiers.HasFlag(ModifierKeyFlags.Shift);
        chkWin.IsChecked = shortcut.Modifiers.HasFlag(ModifierKeyFlags.Windows);
        // 旧 settings.json 互換: 現在値が WheelUp/WheelDown のときだけ動的に選択肢を拡張する
        var options = BuildButtonOptions(shortcut.MouseButton);
        cmbBtn.ItemsSource = options;
        cmbBtn.SelectedItem = FindButtonOptionIn(options, shortcut.MouseButton);

        chkKeyboardEnabled.IsChecked = keyboardOn;
        pnlKeyboard.Visibility = keyboardOn ? Visibility.Visible : Visibility.Collapsed;

        // キーボードラベル用 slot は InitKeyboardSlots で初期化済み
        var slot = _keyboardSlots[tag];
        slot.VirtualKeyCode = shortcut.VirtualKeyCode;
        slot.RefreshLabel();
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
        if (PageLicense is not null) PageLicense.Visibility = Visibility.Collapsed;
    }

    private void OnTabUsageChecked(object sender, RoutedEventArgs e)
    {
        if (PageSettings is null) return;
        PageSettings.Visibility = Visibility.Collapsed;
        PageUsage.Visibility = Visibility.Visible;
        if (PageAbout is not null) PageAbout.Visibility = Visibility.Collapsed;
        if (PageLicense is not null) PageLicense.Visibility = Visibility.Collapsed;
    }

    private void OnTabAboutChecked(object sender, RoutedEventArgs e)
    {
        if (PageSettings is null || PageAbout is null) return;
        PageSettings.Visibility = Visibility.Collapsed;
        PageUsage.Visibility = Visibility.Collapsed;
        PageAbout.Visibility = Visibility.Visible;
        if (PageLicense is not null) PageLicense.Visibility = Visibility.Collapsed;
    }

    private void OnTabLicenseChecked(object sender, RoutedEventArgs e)
    {
        if (PageSettings is null || PageLicense is null) return;
        PageSettings.Visibility = Visibility.Collapsed;
        PageUsage.Visibility = Visibility.Collapsed;
        if (PageAbout is not null) PageAbout.Visibility = Visibility.Collapsed;
        PageLicense.Visibility = Visibility.Visible;
    }

    private void UpdateLicenseUI()
    {
        bool isPro = _licenseService?.IsPro ?? false;
        var status = _licenseService?.Status ?? LicenseStatus.NotEntered;

        // ステータステキスト
        if (LicenseStatusText is not null)
        {
            LicenseStatusText.Text = status switch
            {
                LicenseStatus.Valid => Loc.Get("Str.License.StatusPro"),
                LicenseStatus.Invalid => Loc.Get("Str.License.StatusInvalid"),
                _ => Loc.Get("Str.License.StatusFree"),
            };
        }
        if (LicenseFreeLimitsText is not null)
        {
            LicenseFreeLimitsText.Text = string.Format(
                CultureInfo.CurrentCulture,
                Loc.Get("Str.License.FreeLimits"),
                LicenseService.FreeMaxCoordinates);
            LicenseFreeLimitsText.Visibility = isPro ? Visibility.Collapsed : Visibility.Visible;
        }
        if (LicenseKeyInput is not null)
        {
            LicenseKeyInput.Text = _settingsService.Current.LicenseKey ?? "";
        }

        // Set B PRO バッジ
        if (SetBProBadge is not null)
            SetBProBadge.Visibility = isPro ? Visibility.Collapsed : Visibility.Visible;
        if (SetBProLockedNotice is not null)
            SetBProLockedNotice.Visibility = isPro ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnLicenseApplyClick(object sender, RoutedEventArgs e)
    {
        if (_licenseService is null) return;
        string input = LicenseKeyInput?.Text ?? "";
        var result = _licenseService.Apply(input);
        if (LicenseApplyResultText is not null)
        {
            LicenseApplyResultText.Text = result switch
            {
                LicenseStatus.Valid => Loc.Get("Str.License.StatusValidApplied"),
                LicenseStatus.Invalid => Loc.Get("Str.License.StatusInvalid"),
                _ => Loc.Get("Str.License.StatusFree"),
            };
        }
        UpdateLicenseUI();
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

    private void OnOpenLogClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = DebugLog.LogFilePath;
            if (!System.IO.File.Exists(path))
            {
                System.IO.File.WriteAllText(path, "");
            }
            Process.Start(new ProcessStartInfo("notepad.exe", $"\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            DebugLog.Write($"OnOpenLogClick failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void OnOpenLogFolderClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var folder = System.IO.Path.GetDirectoryName(DebugLog.LogFilePath) ?? "";
            if (!System.IO.Directory.Exists(folder))
                System.IO.Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo("explorer.exe", folder) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            DebugLog.Write($"OnOpenLogFolderClick failed: {ex.GetType().Name}: {ex.Message}");
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
            ChkSaveKeyboardEnabled, _keyboardSlots["Save"].VirtualKeyCode);
        var navShortcut = ReadShortcutUI(
            ChkNavMouseEnabled, ChkNavCtrl, ChkNavAlt, ChkNavShift, ChkNavWin, CmbNavBtn,
            ChkNavKeyboardEnabled, _keyboardSlots["Nav"].VirtualKeyCode);
        var monNavShortcut = ReadShortcutUI(
            ChkMonNavMouseEnabled, ChkMonNavCtrl, ChkMonNavAlt, ChkMonNavShift, ChkMonNavWin, CmbMonNavBtn,
            ChkMonNavKeyboardEnabled, _keyboardSlots["MonNav"].VirtualKeyCode);
        var dispShortcut = ReadShortcutUI(
            ChkDispMouseEnabled, ChkDispCtrl, ChkDispAlt, ChkDispShift, ChkDispWin, CmbDispBtn,
            ChkDispKeyboardEnabled, _keyboardSlots["Disp"].VirtualKeyCode);
        var saveBShortcut = ReadShortcutUI(
            ChkSaveBMouseEnabled, ChkSaveBCtrl, ChkSaveBAlt, ChkSaveBShift, ChkSaveBWin, CmbSaveBBtn,
            ChkSaveBKeyboardEnabled, _keyboardSlots["SaveB"].VirtualKeyCode);
        var navBShortcut = ReadShortcutUI(
            ChkNavBMouseEnabled, ChkNavBCtrl, ChkNavBAlt, ChkNavBShift, ChkNavBWin, CmbNavBBtn,
            ChkNavBKeyboardEnabled, _keyboardSlots["NavB"].VirtualKeyCode);

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

        // 任意キー（F13-F24 以外）を割り当てた場合は修飾キーを 1 つ以上必須にする。
        // 修飾キー無しだと通常の文字入力が常にトリガー発火してしまうため。
        static bool KeyboardNeedsModifier(ActionShortcut s) =>
            s.EnabledTriggers.HasFlag(TriggerType.Keyboard)
            && s.VirtualKeyCode != 0
            && !(s.VirtualKeyCode >= NativeMethods.VK_F13 && s.VirtualKeyCode <= NativeMethods.VK_F24)
            && s.Modifiers == ModifierKeyFlags.None;

        if (KeyboardNeedsModifier(saveShortcut) || KeyboardNeedsModifier(navShortcut) ||
            KeyboardNeedsModifier(monNavShortcut) || KeyboardNeedsModifier(dispShortcut) ||
            KeyboardNeedsModifier(saveBShortcut) || KeyboardNeedsModifier(navBShortcut))
        {
            MessageBox.Show(Loc.Get("Str.Settings.Validation.KeyboardNoModifier"),
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
            // ライセンスキーは LicenseService.Apply 経由で書き込まれる。設定保存時は現在値を維持して上書きしない。
            LicenseKey = _settingsService.Current.LicenseKey,
            // 診断ログ
            VerboseLogging = VerboseLoggingToggle.IsChecked == true,
            // バグ1 回避経路の切替は UI 露出していないので Current から維持
            // UseSendInputForJump は v1.5.0 時代の非推奨フィールド (後方互換のため保持)
            UseSendInputForJump = _settingsService.Current.UseSendInputForJump,
            JumpStrategy = _settingsService.Current.JumpStrategy,
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
        CheckBox chkKeyboardEnabled, int virtualKeyCode)
    {
        var triggers = TriggerType.None;
        if (chkMouseEnabled.IsChecked == true) triggers |= TriggerType.Mouse;
        if (chkKeyboardEnabled.IsChecked == true) triggers |= TriggerType.Keyboard;

        var mod = ModifierKeyFlags.None;
        if (chkCtrl.IsChecked == true) mod |= ModifierKeyFlags.Control;
        if (chkAlt.IsChecked == true) mod |= ModifierKeyFlags.Alt;
        if (chkShift.IsChecked == true) mod |= ModifierKeyFlags.Shift;
        if (chkWin.IsChecked == true) mod |= ModifierKeyFlags.Windows;

        return new ActionShortcut
        {
            EnabledTriggers = triggers,
            Modifiers = mod,
            MouseButton = (cmbBtn.SelectedItem as ButtonOption)?.Value ?? MouseButtonType.Left,
            VirtualKeyCode = virtualKeyCode
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

    private static ButtonOption FindButtonOption(MouseButtonType type) =>
        FindButtonOptionIn(DefaultButtonOptions, type);

    private static ButtonOption FindButtonOptionIn(ButtonOption[] options, MouseButtonType type)
    {
        foreach (var opt in options)
        {
            if (opt.Value == type) return opt;
        }
        return options[0];
    }

    // ===== キー記録 UI ハンドラ =====

    private void OnRecordKeyClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        if (btn.Tag is not string tag) return;
        if (!_keyboardSlots.TryGetValue(tag, out var slot)) return;

        // 別スロットの記録が走っていれば先にキャンセル
        if (_recordingTarget is not null && _recordingTarget != tag)
            EndRecording();

        if (_recordingTarget == tag)
        {
            // 同じボタンの再クリックでキャンセル
            EndRecording();
            return;
        }

        _recordingTarget = tag;
        slot.OriginalRecordButtonText = btn.Content?.ToString() ?? string.Empty;
        btn.Content = Loc.Get("Str.Settings.RecordKeyWaiting");
        PreviewKeyDown += OnRecordingKeyDown;
        Focus();
    }

    private void OnClearKeyClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        if (btn.Tag is not string tag) return;
        if (!_keyboardSlots.TryGetValue(tag, out var slot)) return;

        // 記録中ならキャンセル
        if (_recordingTarget == tag) EndRecording();

        slot.VirtualKeyCode = 0;
        slot.RefreshLabel();
    }

    private void OnRecordingKeyDown(object sender, KeyEventArgs e)
    {
        if (_recordingTarget is null) return;
        if (!_keyboardSlots.TryGetValue(_recordingTarget, out var slot)) return;

        // Alt 押下時は e.Key が Key.System、e.SystemKey に実キーが入る
        var actualKey = e.Key == Key.System ? e.SystemKey : e.Key;
        int vk = KeyInterop.VirtualKeyFromKey(actualKey);
        e.Handled = true;

        // 修飾キー単独は無視（修飾キーだけ押された状態 — 続けて本キーを待つ）
        if (IsModifierVk(vk)) return;

        // ESC でキャンセル
        if (vk == NativeMethods.VK_ESCAPE)
        {
            EndRecording();
            return;
        }

        slot.VirtualKeyCode = vk;

        // 修飾キー状態を読み取って checkbox に反映
        var mods = ModifierKeyFlags.None;
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) mods |= ModifierKeyFlags.Control;
        if ((Keyboard.Modifiers & ModifierKeys.Alt)     != 0) mods |= ModifierKeyFlags.Alt;
        if ((Keyboard.Modifiers & ModifierKeys.Shift)   != 0) mods |= ModifierKeyFlags.Shift;
        // Win キーは WPF Keyboard.Modifiers に乗らないことが多いので GetAsyncKeyState で補完
        if ((NativeMethods.GetAsyncKeyState(NativeMethods.VK_LWIN) & 0x8000) != 0
            || (NativeMethods.GetAsyncKeyState(NativeMethods.VK_RWIN) & 0x8000) != 0)
            mods |= ModifierKeyFlags.Windows;
        slot.SetModifiers(mods);

        // キーボード有効化を自動 ON（パネルが見えていなければここで開く）
        slot.ChkKeyboardEnabled.IsChecked = true;
        OnTriggerEnabledChanged(slot.ChkKeyboardEnabled, new RoutedEventArgs());

        slot.RefreshLabel();
        EndRecording();
    }

    private void EndRecording()
    {
        PreviewKeyDown -= OnRecordingKeyDown;
        if (_recordingTarget is not null
            && _keyboardSlots.TryGetValue(_recordingTarget, out var slot))
        {
            slot.RecordButton.Content = string.IsNullOrEmpty(slot.OriginalRecordButtonText)
                ? Loc.Get("Str.Settings.RecordKey")
                : slot.OriginalRecordButtonText;
        }
        _recordingTarget = null;
    }

    private static bool IsModifierVk(int vk) =>
        vk == NativeMethods.VK_LCONTROL || vk == NativeMethods.VK_RCONTROL || vk == NativeMethods.VK_CONTROL ||
        vk == NativeMethods.VK_LMENU    || vk == NativeMethods.VK_RMENU    || vk == NativeMethods.VK_MENU ||
        vk == NativeMethods.VK_LSHIFT   || vk == NativeMethods.VK_RSHIFT   || vk == NativeMethods.VK_SHIFT ||
        vk == NativeMethods.VK_LWIN     || vk == NativeMethods.VK_RWIN;
}
