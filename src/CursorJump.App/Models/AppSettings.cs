using System;
using System.Collections.Generic;

namespace CursorJump.App.Models;

[Flags]
public enum TriggerType
{
    None     = 0,
    Mouse    = 1,
    Keyboard = 2,
}

public sealed class ActionShortcut
{
    /// <summary>有効なトリガーの組み合わせ。Mouse | Keyboard のように複数指定可能。</summary>
    public TriggerType EnabledTriggers { get; set; } = TriggerType.Mouse;
    public ModifierKeyFlags Modifiers { get; set; } = ModifierKeyFlags.Control | ModifierKeyFlags.Windows;
    public MouseButtonType MouseButton { get; set; } = MouseButtonType.Left;
    /// <summary>キーボードトリガーの仮想キーコード（例: VK_F13=0x7C）。Keyboard フラグが有効な時に使用。</summary>
    public int VirtualKeyCode { get; set; } = 0;
}

public sealed class AppSettings
{
    public ActionShortcut SaveShortcut { get; set; } = new()
    {
        Modifiers = ModifierKeyFlags.Control | ModifierKeyFlags.Windows,
        MouseButton = MouseButtonType.Left
    };

    public ActionShortcut NavigateShortcut { get; set; } = new()
    {
        Modifiers = ModifierKeyFlags.Control | ModifierKeyFlags.Windows,
        MouseButton = MouseButtonType.Right
    };

    public ActionShortcut DisplayDeleteShortcut { get; set; } = new()
    {
        Modifiers = ModifierKeyFlags.Control | ModifierKeyFlags.Windows,
        MouseButton = MouseButtonType.Middle
    };

    public ActionShortcut NavigateCurrentMonitorShortcut { get; set; } = new()
    {
        EnabledTriggers = TriggerType.None,
        Modifiers = ModifierKeyFlags.Control | ModifierKeyFlags.Windows | ModifierKeyFlags.Shift,
        MouseButton = MouseButtonType.Right,
        VirtualKeyCode = 0
    };

    /// <summary>第2座標セット（Set B）の座標保存ショートカット。デフォルト Win+Shift+左。
    /// 旧デフォルト Win+Alt+左 は Xbox Game Bar（Win+Alt プレフィックス）との干渉で
    /// GetAsyncKeyState(VK_LMENU) が 0 を返す問題があるため Win+Shift に変更。</summary>
    public ActionShortcut SaveShortcutB { get; set; } = new()
    {
        EnabledTriggers = TriggerType.Mouse,
        Modifiers = ModifierKeyFlags.Shift | ModifierKeyFlags.Windows,
        MouseButton = MouseButtonType.Left,
        VirtualKeyCode = 0
    };

    /// <summary>第2座標セット（Set B）の座標移動ショートカット。デフォルト Win+Shift+右。</summary>
    public ActionShortcut NavigateShortcutB { get; set; } = new()
    {
        EnabledTriggers = TriggerType.Mouse,
        Modifiers = ModifierKeyFlags.Shift | ModifierKeyFlags.Windows,
        MouseButton = MouseButtonType.Right,
        VirtualKeyCode = 0
    };

    public string SaveCircleColor { get; set; } = "#FF0000";
    public string SaveCircleColorB { get; set; } = "#FF0000";
    public string TrailColor { get; set; } = "#00FF00";
    public string TrailColorB { get; set; } = "#00FF00";
    public string MarkerColor { get; set; } = "#0088FF";
    public string MarkerColorB { get; set; } = "#0088FF";

    /// <summary>座標保存時の収縮円エフェクトを表示するか。false なら視覚効果のみスキップ（保存自体は動作）。</summary>
    public bool SaveEffectEnabled { get; set; } = true;
    /// <summary>ナビゲーション時の軌跡ラインを表示するか。false ならカーソル移動は通常通り。</summary>
    public bool TrailEffectEnabled { get; set; } = true;
    /// <summary>座標表示モード時の保存座標マーカーを描画するか。false なら表示モード中でも視覚マーカーなし。</summary>
    public bool MarkerEffectEnabled { get; set; } = true;
    /// <summary>座標表示/削除モード中にヘルプパネルを表示するか。false でも全削除確認バナーは表示される。旧 settings.json では不在 → true 扱い。</summary>
    public bool ShowDeleteModeHelp { get; set; } = true;

    // ── 軌跡エフェクト詳細設定 ──
    /// <summary>軌跡ラインの太さ（dp）。デフォルト 3.0。範囲: 1.0–20.0。</summary>
    public double TrailThickness { get; set; } = 3.0;
    /// <summary>軌跡フェードアウトの総時間（ms）。デフォルト 500。範囲: 100–3000。</summary>
    public int TrailDurationMs { get; set; } = 500;
    /// <summary>軌跡のピーク不透明度。デフォルト 1.0。範囲: 0.1–1.0。</summary>
    public double TrailOpacity { get; set; } = 1.0;

    // ── 永続化された座標 ──
    /// <summary>Set A の保存座標。アプリ終了後も保持。</summary>
    public List<SavedCoordinate> SavedCoordinatesA { get; set; } = new();
    /// <summary>Set B の保存座標。アプリ終了後も保持。</summary>
    public List<SavedCoordinate> SavedCoordinatesB { get; set; } = new();

    /// <summary>UI テーマ（Light / Dark）。デフォルトは Dark。</summary>
    public UiTheme UiTheme { get; set; } = UiTheme.Dark;

    /// <summary>UI 言語。Auto は OS の UI 言語から自動判定する。</summary>
    public UiLanguage UiLanguage { get; set; } = UiLanguage.Auto;

    // ── 自動更新設定 ──
    /// <summary>起動時に GitHub Releases へ更新確認を行うか。旧 settings.json では未定義 → true 扱い。</summary>
    public bool AutoUpdateEnabled { get; set; } = true;
    /// <summary>最後に更新確認を行った UTC 時刻（ISO 8601）。空文字は未確認。デバッグ・UI 表示用。</summary>
    public string LastUpdateCheckUtc { get; set; } = "";
    /// <summary>「このバージョンをスキップ」で記録された対象バージョン文字列。一致する版は通知しない。</summary>
    public string SkippedVersion { get; set; } = "";

    public AppSettings Clone()
    {
        var c = (AppSettings)MemberwiseClone();
        c.SavedCoordinatesA = new List<SavedCoordinate>(SavedCoordinatesA);
        c.SavedCoordinatesB = new List<SavedCoordinate>(SavedCoordinatesB);
        return c;
    }
}

public enum UiTheme
{
    Light,
    Dark
}

public enum UiLanguage
{
    Auto,
    Japanese,
    English
}

public enum MouseButtonType
{
    Left,
    Right,
    Middle,
    XButton1,           // 戻るボタン（サイドボタン1）
    XButton2,           // 進むボタン（サイドボタン2）
    MiddleLeftChord,    // ホイール押下 + 左クリック
    MiddleRightChord,   // ホイール押下 + 右クリック
    MiddleDoubleClick,  // ホイール2連打
    MiddleTripleClick   // ホイール3連打
}

[Flags]
public enum ModifierKeyFlags
{
    None = 0,
    Control = 1,
    Alt = 2,
    Shift = 4,
    Windows = 8
}
