using System;

namespace CursorJump.App.Models;

public sealed class ActionShortcut
{
    public ModifierKeyFlags Modifiers { get; set; } = ModifierKeyFlags.Control | ModifierKeyFlags.Windows;
    public MouseButtonType MouseButton { get; set; } = MouseButtonType.Left;
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

    public int CenterJumpModifiers { get; set; } = 0x0002 | 0x0001; // Ctrl+Alt
    public int CenterJumpKey { get; set; } = 0x24; // VK_HOME
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
