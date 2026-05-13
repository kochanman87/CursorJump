using System.Linq;
using CursorJump.App;
using CursorJump.App.Models;
using Xunit;

namespace CursorJump.Tests;

public class CoordinateStoreTests
{
    private static CoordinateStore BuildStoreWith(params SavedCoordinate[] coords)
    {
        var store = new CoordinateStore();
        store.Load(coords);
        return store;
    }

    [Fact]
    public void GetNext_with_connected_monitors_skips_disconnected()
    {
        var store = BuildStoreWith(
            new SavedCoordinate(10, 10, @"\\.\DISPLAY1"),
            new SavedCoordinate(20, 20, @"\\.\DISPLAY2"),
            new SavedCoordinate(30, 30, @"\\.\DISPLAY3"));

        var connected = new[] { @"\\.\DISPLAY1" };

        var first  = store.GetNext(connected);
        var second = store.GetNext(connected);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(@"\\.\DISPLAY1", first!.MonitorDeviceName);
        Assert.Equal(@"\\.\DISPLAY1", second!.MonitorDeviceName);
        Assert.Equal((10, 10), (first.X, first.Y));
        Assert.Equal((10, 10), (second.X, second.Y));  // 1個しか接続中に無いので循環
    }

    [Fact]
    public void GetNext_with_no_matching_monitor_returns_null()
    {
        var store = BuildStoreWith(
            new SavedCoordinate(10, 10, @"\\.\DISPLAY2"),
            new SavedCoordinate(20, 20, @"\\.\DISPLAY3"));

        var connected = new[] { @"\\.\DISPLAY1" };
        Assert.Null(store.GetNext(connected));
    }

    // 注: 「空 MonitorDeviceName が常に表示される」レガシー動作は MonitorFilterTests でカバー。
    // CoordinateStore.Load は空名を Screen.FromPoint で再解決するため、Store 経由では再現できない。

    [Fact]
    public void Legacy_GetNext_no_args_still_cycles_all()
    {
        // 後方互換: 引数なし版は接続状況を無視して全座標循環
        var store = BuildStoreWith(
            new SavedCoordinate(10, 10, @"\\.\DISPLAY1"),
            new SavedCoordinate(20, 20, @"\\.\DISPLAY2"));

        var first  = store.GetNext();
        var second = store.GetNext();
        var third  = store.GetNext();

        Assert.Equal((10, 10), (first!.X, first.Y));
        Assert.Equal((20, 20), (second!.X, second.Y));
        Assert.Equal((10, 10), (third!.X, third.Y));
    }

    [Fact]
    public void GetNext_cycles_through_multiple_connected()
    {
        var store = BuildStoreWith(
            new SavedCoordinate(10, 10, @"\\.\DISPLAY1"),
            new SavedCoordinate(20, 20, @"\\.\DISPLAY2"),
            new SavedCoordinate(30, 30, @"\\.\DISPLAY3"));

        var connected = new[] { @"\\.\DISPLAY1", @"\\.\DISPLAY3" };

        var first  = store.GetNext(connected);
        var second = store.GetNext(connected);
        var third  = store.GetNext(connected);

        Assert.Equal(@"\\.\DISPLAY1", first!.MonitorDeviceName);
        Assert.Equal(@"\\.\DISPLAY3", second!.MonitorDeviceName);
        Assert.Equal(@"\\.\DISPLAY1", third!.MonitorDeviceName);  // DISPLAY2 をスキップして循環
    }
}
