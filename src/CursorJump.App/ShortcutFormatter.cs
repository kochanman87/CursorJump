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

    /// <summary>
    /// キーボード側のみのショートカット文字列を返す（例: 「Ctrl+Alt+Z」、「F13」）。
    /// F13-F24 は実挙動で修飾キーを無視するため、表示も修飾キーを省略する。
    /// </summary>
    public static string FormatKeyboard(ActionShortcut shortcut)
    {
        bool isViaKey = shortcut.VirtualKeyCode >= NativeMethods.VK_F13
                        && shortcut.VirtualKeyCode <= NativeMethods.VK_F24;
        var sb = new StringBuilder();
        if (!isViaKey)
        {
            string mods = FormatModifiers(shortcut.Modifiers);
            if (mods.Length > 0)
            {
                sb.Append(mods);
                sb.Append('+');
            }
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
        MouseButtonType.WheelUp           => Loc.Get("Str.Button.WheelUp"),
        MouseButtonType.WheelDown         => Loc.Get("Str.Button.WheelDown"),
        MouseButtonType.MouseWheel        => Loc.Get("Str.Button.MouseWheel"),
        _                                 => Loc.Get("Str.Button.Left")
    };

    /// <summary>削除モードヘルプ用。物理ボタン名にマップ後の単純名（Left/Right/Middle/XButton1/XButton2）。</summary>
    private static string ButtonTypeNameCompact(MouseButtonType type) => type switch
    {
        MouseButtonType.Left     => Loc.Get("Str.Button.Left"),
        MouseButtonType.Right    => Loc.Get("Str.Button.Right"),
        MouseButtonType.Middle   => Loc.Get("Str.Button.Middle"),
        MouseButtonType.XButton1  => Loc.Get("Str.Button.XButton1"),
        MouseButtonType.XButton2  => Loc.Get("Str.Button.XButton2"),
        MouseButtonType.WheelUp   => Loc.Get("Str.Button.WheelUp"),
        MouseButtonType.WheelDown => Loc.Get("Str.Button.WheelDown"),
        MouseButtonType.MouseWheel => Loc.Get("Str.Button.MouseWheel"),
        _                         => Loc.Get("Str.Button.Left")
    };

    private static string VirtualKeyName(int vkCode)
    {
        if (vkCode == 0) return string.Empty;
        // A-Z (0x41-0x5A)
        if (vkCode >= 0x41 && vkCode <= 0x5A) return ((char)('A' + (vkCode - 0x41))).ToString();
        // 0-9 (0x30-0x39)
        if (vkCode >= 0x30 && vkCode <= 0x39) return ((char)('0' + (vkCode - 0x30))).ToString();
        // F1-F12
        if (vkCode >= NativeMethods.VK_F1 && vkCode <= NativeMethods.VK_F12)
            return $"F{1 + (vkCode - NativeMethods.VK_F1)}";
        // F13-F24
        if (vkCode >= NativeMethods.VK_F13 && vkCode <= NativeMethods.VK_F24)
            return $"F{13 + (vkCode - NativeMethods.VK_F13)}";
        // Numpad 0-9
        if (vkCode >= NativeMethods.VK_NUMPAD0 && vkCode <= NativeMethods.VK_NUMPAD9)
            return $"Num{vkCode - NativeMethods.VK_NUMPAD0}";

        return vkCode switch
        {
            NativeMethods.VK_BACK     => "BackSpace",
            NativeMethods.VK_TAB      => "Tab",
            NativeMethods.VK_RETURN   => "Enter",
            NativeMethods.VK_ESCAPE   => "Esc",
            NativeMethods.VK_SPACE    => "Space",
            NativeMethods.VK_PRIOR    => "PageUp",
            NativeMethods.VK_NEXT     => "PageDown",
            NativeMethods.VK_END      => "End",
            NativeMethods.VK_HOME     => "Home",
            NativeMethods.VK_LEFT     => "←",
            NativeMethods.VK_UP       => "↑",
            NativeMethods.VK_RIGHT    => "→",
            NativeMethods.VK_DOWN     => "↓",
            NativeMethods.VK_INSERT   => "Insert",
            NativeMethods.VK_DELETE   => "Delete",
            NativeMethods.VK_MULTIPLY => "Num*",
            NativeMethods.VK_ADD      => "Num+",
            NativeMethods.VK_SUBTRACT => "Num-",
            NativeMethods.VK_DECIMAL  => "Num.",
            NativeMethods.VK_DIVIDE   => "Num/",
            _                         => $"0x{vkCode:X2}"
        };
    }
}
