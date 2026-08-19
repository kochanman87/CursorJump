using System;
using System.Collections.Generic;
using System.Drawing;
using CursorJump.App;
using CursorJump.App.Models;
using Xunit;

namespace CursorJump.Tests;

public class JumpTargetResolverTests
{
    private const int W = 1920;
    private const int H = 1080;

    private static MonitorInfo Mon(string name, string key, string friendly, int left)
        => new MonitorInfo(name, key, friendly, new Rectangle(left, 0, W, H));

    /// <summary>
    /// 保存時のモニタ構成: DISPLAY1@1920 / DISPLAY2@3840 / DISPLAY3@0
    /// （調査メモ docs/investigations/monitor-identity-jump-bug.md の再現ログと同じ並び）
    /// </summary>
    private static IReadOnlyList<MonitorInfo> SavedLayout() => new[]
    {
        Mon(@"\\.\DISPLAY1", "KEY_A", "MonA", 1920),
        Mon(@"\\.\DISPLAY2", "KEY_B", "MonB", 3840),
        Mon(@"\\.\DISPLAY3", "KEY_C", "MonC", 0),
    };

    /// <summary>
    /// ドック再接続後: Windows が \\.\DISPLAYn を振り直し、名前が 1 つずつ巡回している。
    /// 物理モニタ (= 安定キー) の位置自体は保存時と同じ。
    /// </summary>
    private static IReadOnlyList<MonitorInfo> RotatedLayout() => new[]
    {
        Mon(@"\\.\DISPLAY1", "KEY_C", "MonC", 0),
        Mon(@"\\.\DISPLAY2", "KEY_A", "MonA", 1920),
        Mon(@"\\.\DISPLAY3", "KEY_B", "MonB", 3840),
    };

    // ── 1段目: 安定キー照合 ──

    [Fact]
    public void Key_match_survives_device_name_rotation()
    {
        // 保存時 DISPLAY1 (Left=1920) 上の (2877,551) = 相対 (957,551)
        var coord = new SavedCoordinate(2877, 551, @"\\.\DISPLAY1", 957, 551, "KEY_A", "MonA|1920x1080");

        var result = JumpTargetResolver.Resolve(coord, RotatedLayout());

        // KEY_A は再接続後 DISPLAY2 (Left=1920) にいる → 物理的に同じ場所へ戻る
        Assert.Equal(JumpTargetResolver.MatchKey, result.MatchedBy);
        Assert.Equal(2877, result.X);
        Assert.Equal(551, result.Y);
    }

    [Fact]
    public void Name_match_would_have_jumped_to_the_wrong_monitor()
    {
        // 退行防止: 名前照合だと 1 枚隣 (Left=0) に飛んでいた、というバグの再現条件を固定する
        var coord = new SavedCoordinate(2877, 551, @"\\.\DISPLAY1", 957, 551, "KEY_A", "MonA|1920x1080");

        var buggy = JumpTargetResolver.Resolve(
            coord with { MonitorKey = "", MonitorFingerprint = "" }, RotatedLayout());

        Assert.Equal(JumpTargetResolver.MatchName, buggy.MatchedBy);
        Assert.Equal(957, buggy.X); // ← 旧挙動（誤り）。キーがあればこうならない
    }

    [Fact]
    public void All_three_coordinates_resolve_to_their_original_positions()
    {
        var saved = SavedLayout();
        var coords = new[]
        {
            new SavedCoordinate(2877, 551, @"\\.\DISPLAY1", 957, 551, "KEY_A", "MonA|1920x1080"),
            new SavedCoordinate(4849, 512, @"\\.\DISPLAY2", 1009, 512, "KEY_B", "MonB|1920x1080"),
            new SavedCoordinate(913, 548, @"\\.\DISPLAY3", 913, 548, "KEY_C", "MonC|1920x1080"),
        };

        foreach (var coord in coords)
        {
            // 保存時レイアウトでも再接続後レイアウトでも同じ絶対座標に解決される
            var before = JumpTargetResolver.Resolve(coord, saved);
            var after = JumpTargetResolver.Resolve(coord, RotatedLayout());

            Assert.Equal(JumpTargetResolver.MatchKey, after.MatchedBy);
            Assert.Equal((coord.X, coord.Y), (before.X, before.Y));
            Assert.Equal((before.X, before.Y), (after.X, after.Y));
        }
    }

    // ── 2段目: フレンドリ名 + 解像度のフィンガープリント照合 ──

    [Fact]
    public void Fingerprint_match_when_key_changed_by_port_swap()
    {
        // ドックのポートを変えて UID が変わり、保存済みキーがどのモニタにも一致しないケース
        var monitors = new[]
        {
            Mon(@"\\.\DISPLAY1", "KEY_C", "MonC", 0),
            Mon(@"\\.\DISPLAY2", "KEY_A_NEW_UID", "MonA", 1920),
        };
        var coord = new SavedCoordinate(2877, 551, @"\\.\DISPLAY1", 957, 551, "KEY_A_OLD_UID", "MonA|1920x1080");

        var result = JumpTargetResolver.Resolve(coord, monitors);

        Assert.Equal(JumpTargetResolver.MatchFingerprint, result.MatchedBy);
        Assert.Equal(2877, result.X);
        Assert.Equal(551, result.Y);
    }

    [Fact]
    public void Ambiguous_fingerprint_is_not_used()
    {
        // 同型番・同解像度が 2 枚 → どちらか分からないので採用しない（誤爆させない）
        var monitors = new[]
        {
            Mon(@"\\.\DISPLAY1", "KEY_X", "SameModel", 0),
            Mon(@"\\.\DISPLAY2", "KEY_Y", "SameModel", 1920),
        };
        var coord = new SavedCoordinate(2877, 551, @"\\.\DISPLAY1", 957, 551, "KEY_GONE", "SameModel|1920x1080");

        var result = JumpTargetResolver.Resolve(coord, monitors);

        Assert.Equal(JumpTargetResolver.MatchAbsolute, result.MatchedBy);
        Assert.Equal((2877, 551), (result.X, result.Y));
    }

    // ── 3段目: デバイス名照合（旧データ互換） ──

    [Fact]
    public void Legacy_coordinate_without_key_falls_back_to_name_match()
    {
        var coord = new SavedCoordinate(2877, 551, @"\\.\DISPLAY2", 957, 551);

        var result = JumpTargetResolver.Resolve(coord, RotatedLayout());

        // DISPLAY2 は Left=1920 → 1920+957
        Assert.Equal(JumpTargetResolver.MatchName, result.MatchedBy);
        Assert.Equal(2877, result.X);
    }

    [Fact]
    public void Keyed_coordinate_does_not_fall_back_to_name_when_key_is_missing()
    {
        // キーを持つ座標でキーが一致しない = そのモニタは繋がっていない、と判断する。
        // ここで名前照合に落ちると本バグ（1 枚隣に飛ぶ）を再現してしまうため。
        var coord = new SavedCoordinate(2877, 551, @"\\.\DISPLAY1", 957, 551, "KEY_DISCONNECTED", "Gone|1920x1080");

        var result = JumpTargetResolver.Resolve(coord, RotatedLayout());

        Assert.Equal(JumpTargetResolver.MatchAbsolute, result.MatchedBy);
        Assert.Equal((2877, 551), (result.X, result.Y));
    }

    [Fact]
    public void Name_fallback_still_applies_when_no_monitor_exposes_a_stable_key()
    {
        // EnumDisplayDevices が全滅する環境では従来動作に完全フォールバックする
        var monitors = new[]
        {
            Mon(@"\\.\DISPLAY1", "", "", 0),
            Mon(@"\\.\DISPLAY2", "", "", 1920),
        };
        var coord = new SavedCoordinate(2877, 551, @"\\.\DISPLAY2", 957, 551, "KEY_A", "MonA|1920x1080");

        var result = JumpTargetResolver.Resolve(coord, monitors);

        Assert.Equal(JumpTargetResolver.MatchName, result.MatchedBy);
        Assert.Equal(2877, result.X);
    }

    // ── 4段目: 絶対座標フォールバック ──

    [Fact]
    public void No_monitors_falls_back_to_absolute()
    {
        var coord = new SavedCoordinate(2877, 551, @"\\.\DISPLAY1", 957, 551, "KEY_A", "MonA|1920x1080");

        var result = JumpTargetResolver.Resolve(coord, Array.Empty<MonitorInfo>());

        Assert.Equal(JumpTargetResolver.MatchAbsolute, result.MatchedBy);
        Assert.Equal((2877, 551), (result.X, result.Y));
    }

    [Fact]
    public void Monitor_found_but_relative_missing_uses_absolute()
    {
        // v1.5.1 より前のデータ（相対座標なし）はモニタが特定できても再計算できない
        var coord = new SavedCoordinate(2877, 551, @"\\.\DISPLAY1", -1, -1, "KEY_A", "MonA|1920x1080");

        var result = JumpTargetResolver.Resolve(coord, RotatedLayout());

        Assert.Equal(JumpTargetResolver.MatchAbsolute, result.MatchedBy);
        Assert.Equal((2877, 551), (result.X, result.Y));
    }

    // ── クランプ ──

    [Fact]
    public void Result_is_clamped_into_the_matched_monitor_bounds()
    {
        // 保存時 1920x1080 だったモニタが 1280x720 に変わったケース
        var monitors = new[]
        {
            new MonitorInfo(@"\\.\DISPLAY1", "KEY_A", "MonA", new Rectangle(0, 0, 1280, 720)),
        };
        var coord = new SavedCoordinate(1800, 1000, @"\\.\DISPLAY1", 1800, 1000, "KEY_A", "MonA|1920x1080");

        var result = JumpTargetResolver.Resolve(coord, monitors);

        Assert.Equal(JumpTargetResolver.MatchKey, result.MatchedBy);
        Assert.Equal(1279, result.X);
        Assert.Equal(719, result.Y);
    }

    [Fact]
    public void Clamp_respects_non_zero_origin()
    {
        var monitors = new[]
        {
            new MonitorInfo(@"\\.\DISPLAY2", "KEY_B", "MonB", new Rectangle(1920, 0, 1280, 720)),
        };
        var coord = new SavedCoordinate(3800, 900, @"\\.\DISPLAY2", 1880, 900, "KEY_B", "MonB|1920x1080");

        var result = JumpTargetResolver.Resolve(coord, monitors);

        Assert.Equal(1920 + 1279, result.X);
        Assert.Equal(719, result.Y);
    }

    [Fact]
    public void In_bounds_result_is_not_modified_by_clamp()
    {
        var monitors = new[]
        {
            new MonitorInfo(@"\\.\DISPLAY1", "KEY_A", "MonA", new Rectangle(0, 0, 1920, 1080)),
        };
        var coord = new SavedCoordinate(500, 400, @"\\.\DISPLAY1", 500, 400, "KEY_A", "MonA|1920x1080");

        var result = JumpTargetResolver.Resolve(coord, monitors);

        Assert.Equal((500, 400), (result.X, result.Y));
    }

    // ── フィンガープリント生成 ──

    [Fact]
    public void Fingerprint_is_empty_when_friendly_name_or_size_is_missing()
    {
        Assert.Equal(string.Empty, MonitorInfo.BuildFingerprint("", 1920, 1080));
        Assert.Equal(string.Empty, MonitorInfo.BuildFingerprint("MonA", 0, 1080));
        Assert.Equal("MonA|1920x1080", MonitorInfo.BuildFingerprint("MonA", 1920, 1080));
    }

    [Fact]
    public void Empty_fingerprints_never_match_each_other()
    {
        // フレンドリ名も解像度も取れないモニタ同士が「一致」してしまわないこと
        var monitors = new[]
        {
            new MonitorInfo(@"\\.\DISPLAY1", "KEY_X", "", Rectangle.Empty),
        };
        var coord = new SavedCoordinate(100, 100, @"\\.\DISPLAY9", 50, 50, "KEY_GONE", "");

        var result = JumpTargetResolver.Resolve(coord, monitors);

        Assert.Equal(JumpTargetResolver.MatchAbsolute, result.MatchedBy);
    }

    [Fact]
    public void GroupKey_prefers_stable_key_and_falls_back_to_device_name()
    {
        Assert.Equal("KEY_A", Mon(@"\\.\DISPLAY1", "KEY_A", "MonA", 0).GroupKey);
        Assert.Equal(@"\\.\DISPLAY1", Mon(@"\\.\DISPLAY1", "", "", 0).GroupKey);
    }
}
