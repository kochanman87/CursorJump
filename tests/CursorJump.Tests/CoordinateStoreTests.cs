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
    public void Load_returns_false_when_no_migration_needed()
    {
        // 新フォーマット: MonitorRelativeX/Y がすでに 0 以上 → 補完不要
        var store = new CoordinateStore();
        bool migrated = store.Load(new[]
        {
            new SavedCoordinate(100, 200, @"\\.\DISPLAY1", 100, 200),
            new SavedCoordinate(300, 400, @"\\.\DISPLAY1", 300, 400),
        });
        Assert.False(migrated);
        var all = store.GetAll();
        Assert.Equal(100, all[0].MonitorRelativeX);
        Assert.Equal(200, all[0].MonitorRelativeY);
    }

    [Fact]
    public void Load_returns_true_when_legacy_data_needs_migration()
    {
        // 旧フォーマット: MonitorRelativeX/Y == -1 (未設定) → 補完が走る
        // ※ 実マシンの Screen 状態に依存するため、補完値そのものは検証せず "migration が走った" 事実のみ検証
        var store = new CoordinateStore();
        bool migrated = store.Load(new[]
        {
            // MonitorDeviceName あり、Relative 未設定 → 該当モニタ判定して補完
            new SavedCoordinate(100, 200, System.Windows.Forms.Screen.PrimaryScreen!.DeviceName),
        });
        Assert.True(migrated);
        var all = store.GetAll();
        Assert.True(all[0].MonitorRelativeX >= 0);
        Assert.True(all[0].MonitorRelativeY >= 0);
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
