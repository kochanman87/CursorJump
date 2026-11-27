using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using CursorJump.App.Controls;
using CursorJump.App.Models;

namespace CursorJump.App;

/// <summary>
/// 設定画面（v1.12.0 で作り直し）。CircleSwitch と同じ「左ナビ 4 項目 ＋ 右カード」。
/// 保存ボタンは無く、変えた項目はその場で保存する（<see cref="ScheduleSave"/>）。
/// 組み合わせが不正（重複・修飾キーなし等）なときは保存せず、下の知らせ欄に理由を出す。閉じるのは × か Esc。
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly SettingsService _settingsService;
    private readonly UpdateService? _updateService;
    private readonly LicenseService? _licenseService;

    /// <summary>操作の行と、AppSettings 上の対応先。検証時の名前（重複の知らせ）もここで持つ。</summary>
    private sealed record RowBinding(ActionRow Row, string NameKey,
        Func<AppSettings, ActionShortcut> Get, Action<AppSettings, ActionShortcut> Set);

    private RowBinding[] _rows = Array.Empty<RowBinding>();

    private string _saveColor = "#5BA8F0";
    private string _saveColorB = "#FF7FA8";
    private string _trailColor = "#5BA8F0";
    private string _trailColorB = "#FF7FA8";
    private string _markerColor = "#5BA8F0";
    private string _markerColorB = "#FF7FA8";
    private string _clickHistoryMarkerColor = "#5BA8F0";

    private bool _initialized;
    private bool _saveScheduled;
    private bool _lastAutoStart;

    public SettingsWindow(SettingsService settingsService) : this(settingsService, null, null) { }

    public SettingsWindow(SettingsService settingsService, UpdateService? updateService) : this(settingsService, updateService, null) { }

    public SettingsWindow(SettingsService settingsService, UpdateService? updateService, LicenseService? licenseService)
    {
        _settingsService = settingsService;
        _updateService = updateService;
        _licenseService = licenseService;
        InitializeComponent();

        // 「クリックした所へ戻る」の追加設定を行の編集欄の下へ移す（XAML では UserControl の中に名前付き要素を置けないため）
        ((Panel)ClickHistoryExtra.Parent).Children.Remove(ClickHistoryExtra);
        RowClkBack.Extra = ClickHistoryExtra;

        _rows = new[]
        {
            new RowBinding(RowSave,   "Str.Settings.Action.Save",            s => s.SaveShortcut,                   (s, v) => s.SaveShortcut = v),
            new RowBinding(RowNav,    "Str.Settings.Action.NavigateAll",     s => s.NavigateShortcut,               (s, v) => s.NavigateShortcut = v),
            new RowBinding(RowDisp,   "Str.Settings.Action.Display",         s => s.DisplayDeleteShortcut,          (s, v) => s.DisplayDeleteShortcut = v),
            new RowBinding(RowMonNav, "Str.Settings.Action.NavigateMonitor", s => s.NavigateCurrentMonitorShortcut, (s, v) => s.NavigateCurrentMonitorShortcut = v),
            new RowBinding(RowSaveB,  "Str.Settings.Action.SaveB",           s => s.SaveShortcutB,                  (s, v) => s.SaveShortcutB = v),
            new RowBinding(RowNavB,   "Str.Settings.Action.NavigateB",       s => s.NavigateShortcutB,              (s, v) => s.NavigateShortcutB = v),
            new RowBinding(RowReset,  "Str.Settings.Action.ResetCycle",      s => s.ResetCycleShortcut,             (s, v) => s.ResetCycleShortcut = v),
            new RowBinding(RowActWin, "Str.Settings.Action.ActiveWindowJump", s => s.ActiveWindowJumpShortcut,      (s, v) => s.ActiveWindowJumpShortcut = v),
            new RowBinding(RowClkBack,"Str.Settings.Action.ClickHistoryBack", s => s.ClickHistoryBackShortcut,      (s, v) => s.ClickHistoryBackShortcut = v),
        };
        foreach (var b in _rows)
        {
            b.Row.Changed += (_, _) => ScheduleSave();
            b.Row.ExpandRequested += (sender, _) =>
            {
                foreach (var other in _rows)
                    if (!ReferenceEquals(other.Row, sender)) other.Row.IsExpanded = false;
            };
        }

        UpdateVersionTexts();
        LoadCurrentSettings();
        UpdateLicenseUI();
        UpdateHelpShortcuts(_settingsService.Current);

        LocalizationManager.LanguageChanged += OnLanguageChanged;
        if (_licenseService is not null) _licenseService.StatusChanged += OnLicenseStatusChanged;
        Loaded += OnSettingsWindowLoaded;
        Closing += OnWindowClosing;
        Closed += (_, _) =>
        {
            LocalizationManager.LanguageChanged -= OnLanguageChanged;
            if (_licenseService is not null) _licenseService.StatusChanged -= OnLicenseStatusChanged;
            HelpAnim.Stop();
        };
        // Esc で閉じる。キー記録中の Esc は ActionRow が先に処理済み（Handled）なので閉じない
        KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape || e.Handled) return;
            e.Handled = true;
            Close();
        };
        _initialized = true;
    }

    /// <summary>作業領域を超えないようにクランプして中央へ置き直す（13〜14" ノート PC の 150% 対策）。</summary>
    private void OnSettingsWindowLoaded(object sender, RoutedEventArgs e)
    {
        var workArea = SystemParameters.WorkArea;
        double maxH = workArea.Height * 0.95;
        double maxW = workArea.Width * 0.95;
        if (Height > maxH) Height = maxH;
        if (Width > maxW) Width = maxW;
        Left = workArea.Left + (workArea.Width - Width) / 2;
        Top  = workArea.Top  + (workArea.Height - Height) / 2;
    }

    /// <summary>まだ保存していない変更が予約されていれば、閉じる前に保存する。</summary>
    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_saveScheduled) SaveNow();
    }

    private void OnLanguageChanged()
    {
        UpdateVersionTexts();
        foreach (var b in _rows) b.Row.RefreshLanguage();
        UpdateLicenseUI();
        UpdateLastCheckedLabel(_settingsService.Current.LastUpdateCheckUtc);
        UpdateHelpShortcuts(_settingsService.Current);
        if (MessageBar.Visibility == Visibility.Visible) SaveNow(); // 知らせ欄の文言も今の言語で出し直す
    }

    private void OnLicenseStatusChanged() => Dispatcher.BeginInvoke(UpdateLicenseUI);

    private void UpdateVersionTexts()
    {
        var v = Assembly.GetExecutingAssembly().GetName().Version;
        string version = v is null ? "-" : $"{v.Major}.{v.Minor}.{v.Build}";
        if (v is not null)
            Title = string.Format(Loc.Get("Str.Settings.TitleFormat"), v.Major, v.Minor, v.Build);
        NavVersionText.Text = "v" + version;
        AboutVersionText.Text = string.Format(CultureInfo.CurrentCulture, Loc.Get("Str.About.Version"), version);
    }

    // ==== 読み込み ====

    private void LoadCurrentSettings()
    {
        var s = _settingsService.Current;
        foreach (var b in _rows) b.Row.Load(b.Get(s));

        StepClickHistoryDepth.Value = s.ClickHistoryDepth;
        ChkClickHistoryMarkersEnabled.IsChecked = s.ClickHistoryMarkersEnabled;
        _clickHistoryMarkerColor = s.ClickHistoryMarkerColor ?? "#5BA8F0";
        StepClickHistoryMarkerOpacity.Value = double.IsFinite(s.ClickHistoryMarkerOpacity) ? s.ClickHistoryMarkerOpacity : 0.35;

        _saveColor   = s.SaveCircleColor;
        _saveColorB  = s.SaveCircleColorB;
        _trailColor  = s.TrailColor;
        _trailColorB = s.TrailColorB;
        _markerColor  = s.MarkerColor;
        _markerColorB = s.MarkerColorB;
        RefreshSwatches();

        ChkActivateWindowOnJump.IsChecked = s.ActivateWindowUnderCursorOnJump;
        ChkSaveEffectEnabled.IsChecked = s.SaveEffectEnabled;
        ChkTrailEffectEnabled.IsChecked = s.TrailEffectEnabled;
        ChkMarkerEffectEnabled.IsChecked = s.MarkerEffectEnabled;
        ChkShowDeleteModeHelp.IsChecked = s.ShowDeleteModeHelp;

        StepTrailThickness.Value = s.TrailThickness;
        StepTrailDuration.Value = s.TrailDurationMs;
        StepTrailOpacity.Value = s.TrailOpacity;

        if (s.UiTheme == UiTheme.Dark) ThemeDark.IsChecked = true; else ThemeLight.IsChecked = true;
        // 言語（Auto は実際に解決された言語のラジオを選ぶ。保存値は利用者がラジオを押すまで Auto のまま）
        if (LocalizationManager.Resolve(s.UiLanguage) == UiLanguage.English) LangEn.IsChecked = true; else LangJa.IsChecked = true;

        AutoUpdateToggle.IsChecked = s.AutoUpdateEnabled;
        UpdateLastCheckedLabel(s.LastUpdateCheckUtc);
        VerboseLoggingToggle.IsChecked = s.VerboseLogging;
        AutoStartToggle.IsChecked = s.AutoStartEnabled;
        _lastAutoStart = s.AutoStartEnabled;
    }

    private void UpdateLastCheckedLabel(string isoUtc)
    {
        string shown = "-";
        if (!string.IsNullOrEmpty(isoUtc))
        {
            shown = DateTime.TryParse(isoUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt)
                ? dt.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture)
                : isoUtc;
        }
        AboutLastCheckedText.Text = string.Format(CultureInfo.CurrentCulture, Loc.Get("Str.About.LastChecked"), shown);
    }

    // ==== その場で保存 ====

    private void OnAnyChanged(object sender, RoutedEventArgs e) => ScheduleSave();

    /// <summary>
    /// 保存を手が空いてからに回す（スライダーのドラッグ等で何度来ても 1 回にまとめる）。
    /// 閉じる時に予約が残っていれば OnWindowClosing が保存する。
    /// </summary>
    private void ScheduleSave()
    {
        if (!_initialized || _saveScheduled) return;
        _saveScheduled = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(SaveNow));
    }

    /// <summary>
    /// 画面の値を最新の設定（Current の写し）に重ねて保存する。保存座標・ライセンスキー・更新確認の記録など、
    /// 画面に無い値は Current のまま残る。検証に通らなければ保存せず、知らせ欄に理由を出す。
    /// </summary>
    private void SaveNow()
    {
        _saveScheduled = false;
        var settings = _settingsService.Current.Clone();
        foreach (var b in _rows) b.Set(settings, b.Row.Read());

        settings.ClickHistoryDepth = (int)Math.Round(StepClickHistoryDepth.Value);
        settings.ClickHistoryMarkersEnabled = ChkClickHistoryMarkersEnabled.IsChecked == true;
        settings.ClickHistoryMarkerColor = _clickHistoryMarkerColor;
        settings.ClickHistoryMarkerOpacity = StepClickHistoryMarkerOpacity.Value;
        settings.SaveCircleColor  = _saveColor;
        settings.SaveCircleColorB = _saveColorB;
        settings.TrailColor       = _trailColor;
        settings.TrailColorB      = _trailColorB;
        settings.MarkerColor      = _markerColor;
        settings.MarkerColorB     = _markerColorB;
        settings.SaveEffectEnabled = ChkSaveEffectEnabled.IsChecked == true;
        settings.TrailEffectEnabled = ChkTrailEffectEnabled.IsChecked == true;
        settings.MarkerEffectEnabled = ChkMarkerEffectEnabled.IsChecked == true;
        settings.ShowDeleteModeHelp = ChkShowDeleteModeHelp.IsChecked == true;
        settings.TrailThickness = StepTrailThickness.Value;
        settings.TrailDurationMs = (int)Math.Round(StepTrailDuration.Value);
        settings.TrailOpacity = StepTrailOpacity.Value;
        settings.ActivateWindowUnderCursorOnJump = ChkActivateWindowOnJump.IsChecked == true;
        settings.AutoUpdateEnabled = AutoUpdateToggle.IsChecked == true;
        settings.VerboseLogging = VerboseLoggingToggle.IsChecked == true;
        settings.AutoStartEnabled = AutoStartToggle.IsChecked == true;
        settings.UiTheme = ThemeDark.IsChecked == true ? UiTheme.Dark : UiTheme.Light;
        // 言語は利用者がラジオを押した時だけ確定値にする（OnLanguageChecked が Current を直接は触らず、ここで反映）
        settings.UiLanguage = _pendingLanguage ?? settings.UiLanguage;

        string? problem = Validate(settings);
        if (problem is not null)
        {
            ShowMessage(string.Format(Loc.Get("Str.Settings.NotSavedFormat"), problem));
            return;
        }

        if (!_settingsService.Save(settings))
        {
            ShowMessage(Loc.Get("Str.MessageBox.SettingsSaveFailed"));
            return;
        }
        _pendingLanguage = null;
        ShowMessage(null);

        // 自動起動のレジストリ反映は値が変わった時だけ（失敗しても操作は止めない）
        if (settings.AutoStartEnabled != _lastAutoStart)
        {
            StartupService.ApplyAutoStart(settings.AutoStartEnabled);
            _lastAutoStart = settings.AutoStartEnabled;
        }
        UpdateHelpShortcuts(settings);
    }

    /// <summary>旧「保存」ボタンと同じ検証。問題があればその説明、無ければ null。</summary>
    private string? Validate(AppSettings s)
    {
        if (s.SaveShortcut.EnabledTriggers == TriggerType.None ||
            s.NavigateShortcut.EnabledTriggers == TriggerType.None ||
            s.DisplayDeleteShortcut.EnabledTriggers == TriggerType.None)
            return Loc.Get("Str.Settings.Validation.NoTrigger");

        static bool NeedsModifier(ActionShortcut sc) =>
            sc.EnabledTriggers.HasFlag(TriggerType.Mouse)
            && sc.Modifiers == ModifierKeyFlags.None
            && sc.MouseButton is not (
                MouseButtonType.XButton1 or
                MouseButtonType.XButton2 or
                MouseButtonType.MiddleLeftChord or
                MouseButtonType.MiddleRightChord or
                MouseButtonType.MiddleDoubleClick or
                MouseButtonType.MiddleTripleClick);
        if (_rows.Any(b => NeedsModifier(b.Get(s))))
            return Loc.Get("Str.Settings.Validation.NoModifier");

        // 任意キー（F13-F24 以外）は修飾キーを 1 つ以上必須（無いと通常の文字入力で発火してしまう）
        static bool KeyboardNeedsModifier(ActionShortcut sc) =>
            sc.EnabledTriggers.HasFlag(TriggerType.Keyboard)
            && sc.VirtualKeyCode != 0
            && !(sc.VirtualKeyCode >= NativeMethods.VK_F13 && sc.VirtualKeyCode <= NativeMethods.VK_F24)
            && sc.Modifiers == ModifierKeyFlags.None;
        if (_rows.Any(b => KeyboardNeedsModifier(b.Get(s))))
            return Loc.Get("Str.Settings.Validation.KeyboardNoModifier");

        for (int i = 0; i < _rows.Length; i++)
        {
            for (int j = i + 1; j < _rows.Length; j++)
            {
                var (mouseDup, keyboardDup, gestureDup) = DetectShortcutConflict(_rows[i].Get(s), _rows[j].Get(s));
                if (!mouseDup && !keyboardDup && !gestureDup) continue;
                var kinds = new List<string>();
                if (mouseDup) kinds.Add(Loc.Get("Str.Settings.Validation.ConflictKind.Mouse"));
                if (keyboardDup) kinds.Add(Loc.Get("Str.Settings.Validation.ConflictKind.Keyboard"));
                if (gestureDup) kinds.Add(Loc.Get("Str.Settings.Validation.ConflictKind.Gesture"));
                return string.Format(Loc.Get("Str.Settings.Validation.ConflictFormat"),
                    Loc.Get(_rows[i].NameKey), Loc.Get(_rows[j].NameKey), string.Join(" / ", kinds));
            }
        }
        return null;
    }

    private static (bool mouse, bool keyboard, bool gesture) DetectShortcutConflict(ActionShortcut a, ActionShortcut b)
    {
        bool mouseDup =
            a.EnabledTriggers.HasFlag(TriggerType.Mouse) && b.EnabledTriggers.HasFlag(TriggerType.Mouse)
            && a.Modifiers == b.Modifiers && a.MouseButton == b.MouseButton;
        bool keyboardDup =
            a.EnabledTriggers.HasFlag(TriggerType.Keyboard) && b.EnabledTriggers.HasFlag(TriggerType.Keyboard)
            && a.VirtualKeyCode == b.VirtualKeyCode;
        bool gestureDup =
            a.EnabledTriggers.HasFlag(TriggerType.ModifierSequence) && b.EnabledTriggers.HasFlag(TriggerType.ModifierSequence)
            && a.ModifierGesture != ModifierGesture.None && a.ModifierGesture == b.ModifierGesture;
        return (mouseDup, keyboardDup, gestureDup);
    }

    private void ShowMessage(string? text)
    {
        MessageText.Text = text ?? "";
        MessageBar.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    // ==== ページ切替 ====

    private void OnNavChecked(object sender, RoutedEventArgs e)
    {
        if (PageOps is null || PageLook is null || PageGeneral is null || PageHelp is null) return;
        PageOps.Visibility = NavOps.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PageLook.Visibility = NavLook.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PageGeneral.Visibility = NavGeneral.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PageHelp.Visibility = NavHelp.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        if (NavHelp.IsChecked == true) HelpAnim.Start(); else HelpAnim.Stop();
    }

    // ==== ヘルプ ====

    /// <summary>ヘルプの絵とタイルの文字を、いま保存されている割り当てに合わせる。</summary>
    private void UpdateHelpShortcuts(AppSettings s)
    {
        HelpAnim.SetShortcuts(ShortcutParts(s.SaveShortcut), ShortcutParts(s.NavigateShortcut));
        HelpTileDispTitle.Text = s.DisplayDeleteShortcut.EnabledTriggers.HasFlag(TriggerType.Mouse)
            ? ActionRow.ButtonName(s.DisplayDeleteShortcut.MouseButton)
            : string.Join(" + ", ShortcutParts(s.DisplayDeleteShortcut));
        var b = s.SaveShortcutB;
        HelpTileSetBTitle.Text = b.EnabledTriggers.HasFlag(TriggerType.Mouse) && b.Modifiers != ModifierKeyFlags.None
            ? ShortcutFormatter.FormatModifiers(b.Modifiers).Replace("+", " + ")
            : Loc.Get("Str.Help.Tile.SetBTitle");

        // 色は設定（リストの色）に追従させる（2026-11-27 ユーザー要望）。絵の円・線・印は 1 つ目のリストの各色
        var listA = ToColor(s.SaveCircleColor, Color.FromRgb(0x5B, 0xA8, 0xF0));
        var listB = ToColor(s.SaveCircleColorB, Color.FromRgb(0xFF, 0x7F, 0xA8));
        HelpAnim.SetColors(listA, ToColor(s.TrailColor, listA), ToColor(s.MarkerColor, listA));
        foreach (var icon in new[] { HelpIconWheel, HelpIconDisp, HelpIconMonitor })
            icon.Foreground = new SolidColorBrush(listA);
        HelpIconSetB.Foreground = new SolidColorBrush(listB);
        RowSaveB.SetIconColor(listB);
        RowNavB.SetIconColor(listB);
    }

    private static Color ToColor(string? hex, Color fallback)
    {
        try
        {
            if (!string.IsNullOrEmpty(hex) && ColorConverter.ConvertFromString(hex) is Color c) return c;
        }
        catch (FormatException) { }
        return fallback;
    }

    /// <summary>キーの絵に出す部品（例: Ctrl / Win / 左クリック）。マウスが無効ならキーボード側。</summary>
    private static IReadOnlyList<string> ShortcutParts(ActionShortcut sc)
    {
        var parts = new List<string>();
        if (sc.EnabledTriggers.HasFlag(TriggerType.Mouse))
        {
            string mods = ShortcutFormatter.FormatModifiers(sc.Modifiers);
            if (mods.Length > 0) parts.AddRange(mods.Split('+'));
            parts.Add(ActionRow.ButtonName(sc.MouseButton));
        }
        else if (sc.EnabledTriggers.HasFlag(TriggerType.Keyboard) && sc.VirtualKeyCode != 0)
        {
            parts.AddRange(ShortcutFormatter.FormatKeyboard(sc).Split('+'));
        }
        else
        {
            parts.Add(Loc.Get("Str.Settings.Off"));
        }
        return parts;
    }

    // ==== 見た目 ====

    /// <summary>数の欄（StepField）が利用者の操作で変わった。</summary>
    private void OnStepChanged(object? sender, EventArgs e) => ScheduleSave();

    private void OnLookDetailsToggled(object sender, RoutedEventArgs e)
    {
        bool open = TglLookDetails.IsChecked == true;
        PnlLookDetails.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        TxtLookDetailsChevron.Text = open ? "" : ""; // ChevronDown / ChevronRight
    }

    private void RefreshSwatches()
    {
        // リストの色は「保存の円」の色を代表として見せる（3 つとも同じ色が既定。個別に変えたい人は詳細で）
        SetSwatchColor(RectListColorA, _saveColor);
        SetSwatchColor(RectListColorB, _saveColorB);
        SetSwatchColor(RectSaveColor,   _saveColor);
        SetSwatchColor(RectSaveColorB,  _saveColorB);
        SetSwatchColor(RectTrailColor,  _trailColor);
        SetSwatchColor(RectTrailColorB, _trailColorB);
        SetSwatchColor(RectMarkerColor,  _markerColor);
        SetSwatchColor(RectMarkerColorB, _markerColorB);
        SetSwatchColor(RectClickHistoryMarkerColor, _clickHistoryMarkerColor);
    }

    private void OnColorRectangleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Shape shape) return;

        string currentHex;
        if      (shape == RectListColorA || shape == RectSaveColor) currentHex = _saveColor;
        else if (shape == RectListColorB || shape == RectSaveColorB) currentHex = _saveColorB;
        else if (shape == RectTrailColor)   currentHex = _trailColor;
        else if (shape == RectTrailColorB)  currentHex = _trailColorB;
        else if (shape == RectMarkerColor)  currentHex = _markerColor;
        else if (shape == RectMarkerColorB) currentHex = _markerColorB;
        else if (shape == RectClickHistoryMarkerColor) currentHex = _clickHistoryMarkerColor;
        else return;

        using var dialog = new System.Windows.Forms.ColorDialog { FullOpen = true };
        try
        {
            var wpfColor = (Color)ColorConverter.ConvertFromString(currentHex);
            dialog.Color = System.Drawing.Color.FromArgb(wpfColor.R, wpfColor.G, wpfColor.B);
        }
        catch { }

        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        var c = dialog.Color;
        string hex = $"#{c.R:X2}{c.G:X2}{c.B:X2}";

        // リストの色は、そのリストの「保存の円・移動の線・表示の印」をまとめて同じ色にする
        if      (shape == RectListColorA) { _saveColor = _trailColor = _markerColor = hex; }
        else if (shape == RectListColorB) { _saveColorB = _trailColorB = _markerColorB = hex; }
        else if (shape == RectSaveColor)    _saveColor = hex;
        else if (shape == RectSaveColorB)   _saveColorB = hex;
        else if (shape == RectTrailColor)   _trailColor = hex;
        else if (shape == RectTrailColorB)  _trailColorB = hex;
        else if (shape == RectMarkerColor)  _markerColor = hex;
        else if (shape == RectMarkerColorB) _markerColorB = hex;
        else if (shape == RectClickHistoryMarkerColor) _clickHistoryMarkerColor = hex;

        RefreshSwatches();
        ScheduleSave();
    }

    private static void SetSwatchColor(Shape shape, string hex)
    {
        try
        {
            if (ColorConverter.ConvertFromString(hex) is Color c)
            {
                shape.Fill = new SolidColorBrush(c);
                shape.ToolTip = hex.ToUpperInvariant();
                return;
            }
        }
        catch { }
        shape.Fill = Brushes.Transparent;
    }

    // ==== 全般 ====

    private void OnThemeChecked(object sender, RoutedEventArgs e)
    {
        if (!_initialized) return;
        ThemeManager.Apply(ThemeDark.IsChecked == true ? UiTheme.Dark : UiTheme.Light);
        ScheduleSave();
    }

    /// <summary>利用者が押した言語（保存前）。null なら保存値（Auto を含む）を変えない。</summary>
    private UiLanguage? _pendingLanguage;

    private void OnLanguageChecked(object sender, RoutedEventArgs e)
    {
        if (!_initialized) return;
        var language = LangEn.IsChecked == true ? UiLanguage.English : UiLanguage.Japanese;
        _pendingLanguage = language;
        LocalizationManager.Apply(language);
        ScheduleSave();
    }

    private void UpdateLicenseUI()
    {
        bool isPro = _licenseService?.IsPro ?? false;
        var status = _licenseService?.Status ?? LicenseStatus.NotEntered;

        LicenseStatusText.Text = status switch
        {
            LicenseStatus.Valid => Loc.Get("Str.License.StatusPro"),
            LicenseStatus.Invalid => Loc.Get("Str.License.StatusInvalid"),
            _ => Loc.Get("Str.License.StatusFree"),
        };
        LicenseFreeLimitsText.Text = string.Format(CultureInfo.CurrentCulture, Loc.Get("Str.License.FreeLimits"), LicenseService.FreeMaxCoordinates);
        LicenseFreeLimitsText.Visibility = isPro ? Visibility.Collapsed : Visibility.Visible;
        if (string.IsNullOrEmpty(LicenseKeyInput.Text))
            LicenseKeyInput.Text = _settingsService.Current.LicenseKey ?? "";

        NavEditionText.Text = Loc.Get(isPro ? "Str.Edition.Pro" : "Str.Edition.Free");
        NavEditionSub.Text = Loc.Get(isPro ? "Str.Nav.EditionProSub" : "Str.Nav.EditionFreeSub");

        // Pro 限定の行（2 つ目のリスト・追加の操作）は Free では PRO の印を出して押せなくする（v1.9.1 の仕様を引き継ぐ）
        foreach (var row in new[] { RowSaveB, RowNavB, RowReset, RowActWin, RowClkBack })
            row.IsLocked = !isPro;

        // 2 つ目のリスト（ピンク）は Pro 版のときだけ見せる（2026-11-27 ユーザー指示）
        var setB = isPro ? Visibility.Visible : Visibility.Collapsed;
        CardSetB.Visibility = setB;
        RowListColorB.Visibility = setB;
        HelpTileSetB.Visibility = setB;
        HelpTiles.Columns = isPro ? 4 : 3;
        foreach (var swatch in new[] { RectSaveColorB, RectTrailColorB, RectMarkerColorB })
            swatch.Visibility = setB;
    }

    private void OnLicenseApplyClick(object sender, RoutedEventArgs e)
    {
        if (_licenseService is null) return;
        var result = _licenseService.Apply(LicenseKeyInput.Text ?? "");
        LicenseApplyResultText.Text = result switch
        {
            LicenseStatus.Valid => Loc.Get("Str.License.StatusValidApplied"),
            LicenseStatus.Invalid => Loc.Get("Str.License.StatusInvalid"),
            _ => Loc.Get("Str.License.StatusFree"),
        };
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
                new UpdateDialog(_updateService, info).ShowDialog();
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
            if (!System.IO.File.Exists(path)) System.IO.File.WriteAllText(path, "");
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
            if (!System.IO.Directory.Exists(folder)) System.IO.Directory.CreateDirectory(folder);
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
}
