using System.Text;
using CursorJump.App.Models;

namespace CursorJump.App;

/// <summary>
/// ActionShortcut を「Ctrl+左クリック / Ctrl+F13」のような可読文字列に変換するユーティリティ。
/// </summary>
internal static class ShortcutFormatter
{
    /// <summary>
    /// ショートカット全体を「マウス側 / キーボード側」形式で返す。
    /// 片方のみ有効な場合はその側のみ返す。両方無効な場合は「（無効）」を返す。
    /// </summary>
    public static string Format(ActionShortcut shortcut)
    {
        bool hasMouse    = shortcut.EnabledTriggers.HasFlag(TriggerType.Mouse);
        bool hasKeyboard = shortcut.EnabledTriggers.HasFlag(TriggerType.Keyboard);

        if (!hasMouse && !hasKeyboard)
            return Loc.Get("Str.Shortcut.Disabled");

        if (hasMouse && hasKeyboard)
            return $"{FormatMouse(shortcut)} / {FormatKeyboard(shortcut)}";

        if (hasMouse)
            return FormatMouse(shortcut);

        return FormatKeyboard(shortcut);
    }

    /// <summary>マウス側のみのショートカット文字列を返す（例: 「Ctrl+左クリック」）。</summary>
    public static string FormatMouse(ActionShortcut shortcut)
    {
        var sb = new StringBuilder();
        string mods = FormatModifiers(shortcut.Modifiers);
        if (mods.Length > 0)
        {
            sb.Append(mods);
            sb.Append('+');
        }
        sb.Append(ButtonTypeName(shortcut.MouseButton));
        return sb.ToString();
    }

    /// <summary>キーボード側のみのショートカット文字列を返す（例: 「Ctrl+F13」）。</summary>
    public static string FormatKeyboard(ActionShortcut shortcut)
    {
        var sb = new StringBuilder();
        string mods = FormatModifiers(shortcut.Modifiers);
        if (mods.Length > 0)
        {
            sb.Append(mods);
            sb.Append('+');
        }
        sb.Append(VirtualKeyName(shortcut.VirtualKeyCode));
        return sb.ToString();
    }

    /// <summary>修飾キーフラグを「Ctrl+Alt+Shift+Win」順で連結した文字列を返す。</summary>
    public static string FormatModifiers(ModifierKeyFlags modifiers)
    {
        if (modifiers == ModifierKeyFlags.None) return string.Empty;

        var parts = new System.Collections.Generic.List<string>(4);
        if (modifiers.HasFlag(ModifierKeyFlags.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeyFlags.Alt))     parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeyFlags.Shift))   parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeyFlags.Windows)) parts.Add("Win");

        return string.Join("+", parts);
    }

    /// <summary>
    /// 削除モードヘルプ用。修飾キーなし・拡張ボタンは削除モードで実際に押す物理ボタン名を返す。
    /// </summary>
    public static string FormatForDeleteMode(ActionShortcut shortcut)
    {
        bool hasMouse    = shortcut.EnabledTriggers.HasFlag(TriggerType.Mouse);
        bool hasKeyboard = shortcut.EnabledTriggers.HasFlag(TriggerType.Keyboard);

        if (!hasMouse && !hasKeyboard) return Loc.Get("Str.Shortcut.Disabled");

        var parts = new System.Collections.Generic.List<string>(2);

        if (hasMouse)
        {
            var effective = shortcut.MouseButton switch
            {
                MouseButtonType.MiddleLeftChord
                or MouseButtonType.MiddleDoubleClick => MouseButtonType.Left,
                MouseButtonType.MiddleRightChord
                or MouseButtonType.MiddleTripleClick => MouseButtonType.Right,
                _ => shortcut.MouseButton
            };
            parts.Add(ButtonTypeNameCompact(effective));
        }

        if (hasKeyboard)
            parts.Add(VirtualKeyName(shortcut.VirtualKeyCode));

        return string.Join(" / ", parts);
    }

    private static string ButtonTypeName(MouseButtonType type) => type switch
    {
        MouseButtonType.Left              => Loc.Get("Str.Button.Left"),
        MouseButtonType.Right             => Loc.Get("Str.Button.Right"),
        MouseButtonType.Middle            => Loc.Get("Str.Button.Middle"),
        MouseButtonType.XButton1          => Loc.Get("Str.Button.XButton1"),
        MouseButtonType.XButton2          => Loc.Get("Str.Button.XButton2"),
        MouseButtonType.MiddleLeftChord   => Loc.Get("Str.Button.Compact.MiddleLeftChord"),
        MouseButtonType.MiddleRightChord  => Loc.Get("Str.Button.Compact.MiddleRightChord"),
        MouseButtonType.MiddleDoubleClick => Loc.Get("Str.Button.Compact.MiddleDoubleClick"),
        MouseButtonType.MiddleTripleClick => Loc.Get("Str.Button.Compact.MiddleTripleClick"),
        _                                 => Loc.Get("Str.Button.Left")
    };

    /// <summary>削除モードヘルプ用。物理ボタン名にマップ後の単純名（Left/Right/Middle/XButton1/XButton2）。</summary>
    private static string ButtonTypeNameCompact(MouseButtonType type) => type switch
    {
        MouseButtonType.Left     => Loc.Get("Str.Button.Left"),
        MouseButtonType.Right    => Loc.Get("Str.Button.Right"),
        MouseButtonType.Middle   => Loc.Get("Str.Button.Middle"),
        MouseButtonType.XButton1 => Loc.Get("Str.Button.XButton1"),
        MouseButtonType.XButton2 => Loc.Get("Str.Button.XButton2"),
        _                        => Loc.Get("Str.Button.Left")
    };

    private static string VirtualKeyName(int vkCode)
    {
        // F13 (0x7C) 〜 F24 (0x87)
        if (vkCode >= NativeMethods.VK_F13 && vkCode <= NativeMethods.VK_F24)
            return $"F{13 + (vkCode - NativeMethods.VK_F13)}";

        return $"0x{vkCode:X2}";
    }
}
