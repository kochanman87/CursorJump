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

    public string SaveCircleColor { get; set; } = "#FF0000";
    public string TrailColor { get; set; } = "#00FF00";
    public string MarkerColor { get; set; } = "#0088FF";
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
