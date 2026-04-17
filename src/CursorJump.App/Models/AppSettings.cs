using System;

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

    public string SaveCircleColor { get; set; } = "#FF0000";
    public string TrailColor { get; set; } = "#00FF00";
    public string MarkerColor { get; set; } = "#0088FF";

    /// <summary>座標保存時の収縮円エフェクトを表示するか。false なら視覚効果のみスキップ（保存自体は動作）。</summary>
    public bool SaveEffectEnabled { get; set; } = true;
    /// <summary>ナビゲーション時の軌跡ラインを表示するか。false ならカーソル移動は通常通り。</summary>
    public bool TrailEffectEnabled { get; set; } = true;
    /// <summary>座標表示モード時の保存座標マーカーを描画するか。false なら表示モード中でも視覚マーカーなし。</summary>
    public bool MarkerEffectEnabled { get; set; } = true;
    /// <summary>座標表示/削除モード中にヘルプパネルを表示するか。false でも全削除確認バナーは表示される。旧 settings.json では不在 → true 扱い。</summary>
    public bool ShowDeleteModeHelp { get; set; } = true;

    /// <summary>UI テーマ（Light / Dark）。デフォルトは Dark。</summary>
    public UiTheme UiTheme { get; set; } = UiTheme.Dark;
}

public enum UiTheme
{
    Light,
    Dark
}

public enum MouseButtonType
{
    Left,
    Right,
    Middle,
    XButton1,  // 戻るボタン（サイドボタン1）
    XButton2   // 進むボタン（サイドボタン2）
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
