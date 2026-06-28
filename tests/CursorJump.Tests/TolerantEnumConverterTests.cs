using System.Text.Json;
using CursorJump.App;
using CursorJump.App.Models;
using Xunit;

namespace CursorJump.Tests;

public class TolerantEnumConverterTests
{
    private static readonly JsonSerializerOptions Opt = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new TolerantEnumConverterFactory() }
    };

    [Fact]
    public void Unknown_enum_value_falls_back_to_default_without_throwing()
    {
        // 削除済みの ModifierGesture 値 "CtrlShiftAlt" が残っていても例外にならず None になる
        string json = """
        { "EnabledTriggers": "ModifierSequence", "Modifiers": "Control, Alt", "MouseButton": "Left", "VirtualKeyCode": 0, "ModifierGesture": "CtrlShiftAlt" }
        """;
        var sc = JsonSerializer.Deserialize<ActionShortcut>(json, Opt);
        Assert.NotNull(sc);
        Assert.Equal(ModifierGesture.None, sc!.ModifierGesture);
        Assert.Equal(MouseButtonType.Left, sc.MouseButton);
        Assert.Equal(TriggerType.ModifierSequence, sc.EnabledTriggers);
        Assert.Equal(ModifierKeyFlags.Control | ModifierKeyFlags.Alt, sc.Modifiers);
    }

    [Fact]
    public void Whole_settings_survive_unknown_enum_value()
    {
        // settings.json の 1 フィールドが未知 enum でも LicenseKey 等が失われないこと（v1.9.1 不具合の回帰防止）
        string json = """
        { "ClickHistoryBackShortcut": { "EnabledTriggers": "ModifierSequence", "ModifierGesture": "AltCtrlShift" }, "LicenseKey": "TEST-PLACEHOLDER", "ClickHistoryDepth": 5 }
        """;
        var s = JsonSerializer.Deserialize<AppSettings>(json, Opt);
        Assert.NotNull(s);
        Assert.Equal("TEST-PLACEHOLDER", s!.LicenseKey);
        Assert.Equal(5, s.ClickHistoryDepth);
        Assert.Equal(ModifierGesture.None, s.ClickHistoryBackShortcut.ModifierGesture);
    }

    [Fact]
    public void Flags_enum_roundtrips_as_string()
    {
        string json = """{ "EnabledTriggers": "Mouse, Keyboard", "ModifierGesture": "CtrlDoubleTap" }""";
        var sc = JsonSerializer.Deserialize<ActionShortcut>(json, Opt)!;
        Assert.True(sc.EnabledTriggers.HasFlag(TriggerType.Mouse));
        Assert.True(sc.EnabledTriggers.HasFlag(TriggerType.Keyboard));
        Assert.Equal(ModifierGesture.CtrlDoubleTap, sc.ModifierGesture);

        string back = JsonSerializer.Serialize(sc, Opt);
        Assert.Contains("Mouse, Keyboard", back); // Flags はカンマ区切り文字列で書き出す
        Assert.Contains("CtrlDoubleTap", back);
    }
}
