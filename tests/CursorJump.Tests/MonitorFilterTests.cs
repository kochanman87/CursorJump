using System.Drawing;
using CursorJump.App;
using CursorJump.App.Models;
using Xunit;

namespace CursorJump.Tests;

public class MonitorFilterTests
{
    [Fact]
    public void Connected_monitor_returns_true()
    {
        var coord = new SavedCoordinate(100, 200, @"\\.\DISPLAY3");
        var connected = new[] { @"\\.\DISPLAY1", @"\\.\DISPLAY2", @"\\.\DISPLAY3" };
        Assert.True(MonitorFilter.IsCoordinateOnConnectedMonitor(coord, connected));
    }

    [Fact]
    public void Disconnected_monitor_returns_false()
    {
        var coord = new SavedCoordinate(100, 200, @"\\.\DISPLAY3");
        var connected = new[] { @"\\.\DISPLAY1" };
        Assert.False(MonitorFilter.IsCoordinateOnConnectedMonitor(coord, connected));
    }

    [Fact]
    public void Empty_monitor_name_is_legacy_and_returns_true()
    {
        // 旧 settings.json 互換: MonitorDeviceName 未設定の座標は常に表示・ナビゲート対象
        var coord = new SavedCoordinate(100, 200, "");
        var connected = new[] { @"\\.\DISPLAY1" };
        Assert.True(MonitorFilter.IsCoordinateOnConnectedMonitor(coord, connected));
    }

    [Fact]
    public void Empty_connected_list_returns_false_for_named_coord()
    {
        var coord = new SavedCoordinate(100, 200, @"\\.\DISPLAY1");
        Assert.False(MonitorFilter.IsCoordinateOnConnectedMonitor(coord, System.Array.Empty<string>()));
    }

    // ── v1.9.3: 安定キーベースの判定 ──

    private static MonitorInfo Mon(string name, string key, string friendly, int left)
        => new MonitorInfo(name, key, friendly, new Rectangle(left, 0, 1920, 1080));

    [Fact]
    public void Key_is_connected_even_when_device_name_was_reassigned()
    {
        // 保存時 DISPLAY1 だったモニタが、ドック再接続後は DISPLAY2 という名前になっている
        var coord = new SavedCoordinate(2877, 551, @"\\.\DISPLAY1", 957, 551, "KEY_A", "MonA|1920x1080");
        var monitors = new[]
        {
            Mon(@"\\.\DISPLAY1", "KEY_C", "MonC", 0),
            Mon(@"\\.\DISPLAY2", "KEY_A", "MonA", 1920),
        };

        Assert.True(MonitorFilter.IsCoordinateOnConnectedMonitor(coord, monitors));
    }

    [Fact]
    public void Key_not_present_is_treated_as_disconnected()
    {
        // 名前 (DISPLAY1) は存在するがキーが無い = そのモニタは外れている
        var coord = new SavedCoordinate(2877, 551, @"\\.\DISPLAY1", 957, 551, "KEY_GONE", "Gone|1920x1080");
        var monitors = new[]
        {
            Mon(@"\\.\DISPLAY1", "KEY_C", "MonC", 0),
        };

        Assert.False(MonitorFilter.IsCoordinateOnConnectedMonitor(coord, monitors));
    }

    [Fact]
    public void Legacy_coordinate_without_any_monitor_info_is_always_connected()
    {
        var coord = new SavedCoordinate(100, 200);
        var monitors = new[] { Mon(@"\\.\DISPLAY1", "KEY_C", "MonC", 0) };

        Assert.True(MonitorFilter.IsCoordinateOnConnectedMonitor(coord, monitors));
    }

    [Fact]
    public void Legacy_named_coordinate_matches_by_name_against_snapshot()
    {
        var coord = new SavedCoordinate(100, 200, @"\\.\DISPLAY1", 100, 200);
        var monitors = new[] { Mon(@"\\.\DISPLAY1", "KEY_C", "MonC", 0) };

        Assert.True(MonitorFilter.IsCoordinateOnConnectedMonitor(coord, monitors));
        Assert.False(MonitorFilter.IsCoordinateOnConnectedMonitor(
            coord with { MonitorDeviceName = @"\\.\DISPLAY9" }, monitors));
    }
}
