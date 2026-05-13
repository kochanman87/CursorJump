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
}
