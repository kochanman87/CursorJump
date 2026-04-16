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
            return "（無効）";

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

    private static string ButtonTypeName(MouseButtonType type) => type switch
    {
        MouseButtonType.Left     => "左クリック",
        MouseButtonType.Right    => "右クリック",
        MouseButtonType.Middle   => "ホイールクリック",
        MouseButtonType.XButton1 => "戻るボタン",
        MouseButtonType.XButton2 => "進むボタン",
        _                        => "左クリック"
    };

    private static string VirtualKeyName(int vkCode)
    {
        // F13 (0x7C) 〜 F24 (0x87)
        if (vkCode >= NativeMethods.VK_F13 && vkCode <= NativeMethods.VK_F24)
            return $"F{13 + (vkCode - NativeMethods.VK_F13)}";

        return $"0x{vkCode:X2}";
    }
}
