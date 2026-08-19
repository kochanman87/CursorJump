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
        // 新フォーマット (v1.9.3): MonitorRelativeX/Y に加えて MonitorKey / MonitorFingerprint も
        // 埋まっている → 補完不要
        var store = new CoordinateStore();
        bool migrated = store.Load(new[]
        {
            new SavedCoordinate(100, 200, @"\\.\DISPLAY1", 100, 200, "KEY_A", "MonA|1920x1080"),
            new SavedCoordinate(300, 400, @"\\.\DISPLAY1", 300, 400, "KEY_A", "MonA|1920x1080"),
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

    // ── v1.6.1: GetPrev 系（ホイール上スクロールでの逆方向ナビゲーション用） ──

    [Fact]
    public void GetPrev_cycles_in_reverse()
    {
        var store = BuildStoreWith(
            new SavedCoordinate(10, 10, @"\\.\DISPLAY1"),
            new SavedCoordinate(20, 20, @"\\.\DISPLAY1"),
            new SavedCoordinate(30, 30, @"\\.\DISPLAY1"));

        // 初回 GetPrev: _currentIndex == -1 → 0 に補正後 -1 で循環 = 末尾 (index 2)
        var first  = store.GetPrev();
        var second = store.GetPrev();
        var third  = store.GetPrev();
        var fourth = store.GetPrev();

        Assert.Equal((30, 30), (first!.X, first.Y));
        Assert.Equal((20, 20), (second!.X, second.Y));
        Assert.Equal((10, 10), (third!.X, third.Y));
        Assert.Equal((30, 30), (fourth!.X, fourth.Y));  // 循環
    }

    [Fact]
    public void GetPrev_with_connected_monitors_skips_disconnected()
    {
        var store = BuildStoreWith(
            new SavedCoordinate(10, 10, @"\\.\DISPLAY1"),
            new SavedCoordinate(20, 20, @"\\.\DISPLAY2"),
            new SavedCoordinate(30, 30, @"\\.\DISPLAY3"));

        var connected = new[] { @"\\.\DISPLAY1", @"\\.\DISPLAY3" };

        var first  = store.GetPrev(connected);
        var second = store.GetPrev(connected);
        var third  = store.GetPrev(connected);

        Assert.Equal(@"\\.\DISPLAY3", first!.MonitorDeviceName);
        Assert.Equal(@"\\.\DISPLAY1", second!.MonitorDeviceName);
        Assert.Equal(@"\\.\DISPLAY3", third!.MonitorDeviceName);  // DISPLAY2 をスキップして循環
    }

    [Fact]
    public void GetPrev_after_GetNext_returns_same_position()
    {
        var store = BuildStoreWith(
            new SavedCoordinate(10, 10, @"\\.\DISPLAY1"),
            new SavedCoordinate(20, 20, @"\\.\DISPLAY1"),
            new SavedCoordinate(30, 30, @"\\.\DISPLAY1"));

        var connected = new[] { @"\\.\DISPLAY1" };

        // GetNext を 2 回 → index 1
        store.GetNext(connected);
        var afterNext = store.GetNext(connected);
        Assert.Equal((20, 20), (afterNext!.X, afterNext.Y));

        // GetPrev でひとつ戻れば最初の座標
        var backward = store.GetPrev(connected);
        Assert.Equal((10, 10), (backward!.X, backward.Y));
    }

    [Fact]
    public void GetPrev_with_no_matching_monitor_returns_null()
    {
        var store = BuildStoreWith(
            new SavedCoordinate(10, 10, @"\\.\DISPLAY2"),
            new SavedCoordinate(20, 20, @"\\.\DISPLAY3"));

        var connected = new[] { @"\\.\DISPLAY1" };
        Assert.Null(store.GetPrev(connected));
    }

    [Fact]
    public void GetPrevInMonitor_cycles_in_reverse()
    {
        var store = BuildStoreWith(
            new SavedCoordinate(10, 10, @"\\.\DISPLAY1"),
            new SavedCoordinate(20, 20, @"\\.\DISPLAY1"),
            new SavedCoordinate(30, 30, @"\\.\DISPLAY1"));

        // 初回 GetPrevInMonitor: lastRawIndex 未登録 → 末尾 (index 2)
        var first  = store.GetPrevInMonitor(@"\\.\DISPLAY1");
        var second = store.GetPrevInMonitor(@"\\.\DISPLAY1");
        var third  = store.GetPrevInMonitor(@"\\.\DISPLAY1");
        var fourth = store.GetPrevInMonitor(@"\\.\DISPLAY1");

        Assert.Equal((30, 30), (first!.X, first.Y));
        Assert.Equal((20, 20), (second!.X, second.Y));
        Assert.Equal((10, 10), (third!.X, third.Y));
        Assert.Equal((30, 30), (fourth!.X, fourth.Y));
    }

    [Fact]
    public void GetPrevInMonitor_returns_null_when_no_coords_for_monitor()
    {
        var store = BuildStoreWith(
            new SavedCoordinate(10, 10, @"\\.\DISPLAY1"));
        Assert.Null(store.GetPrevInMonitor(@"\\.\DISPLAY2"));
    }

    // ── v1.9.0: ResetCursor（循環リセット） ──

    [Fact]
    public void ResetCursor_makes_next_return_first_again()
    {
        var store = BuildStoreWith(
            new SavedCoordinate(10, 10, @"\\.\DISPLAY1"),
            new SavedCoordinate(20, 20, @"\\.\DISPLAY1"),
            new SavedCoordinate(30, 30, @"\\.\DISPLAY1"));

        var connected = new[] { @"\\.\DISPLAY1" };

        // 2,3 番目まで進める
        store.GetNext(connected);
        var second = store.GetNext(connected);
        Assert.Equal((20, 20), (second!.X, second.Y));

        store.ResetCursor();

        // リセット後は先頭から再開し、座標数は不変
        var afterReset = store.GetNext(connected);
        Assert.Equal((10, 10), (afterReset!.X, afterReset.Y));
        Assert.Equal(3, store.Count);
    }

    [Fact]
    public void ResetCursor_no_args_getnext_restarts_from_first()
    {
        var store = BuildStoreWith(
            new SavedCoordinate(10, 10, @"\\.\DISPLAY1"),
            new SavedCoordinate(20, 20, @"\\.\DISPLAY1"));

        store.GetNext();
        store.GetNext(); // index 1
        store.ResetCursor();
        var first = store.GetNext();
        Assert.Equal((10, 10), (first!.X, first.Y));
    }

    [Fact]
    public void ResetCursor_also_resets_per_monitor_index()
    {
        var store = BuildStoreWith(
            new SavedCoordinate(10, 10, @"\\.\DISPLAY1"),
            new SavedCoordinate(20, 20, @"\\.\DISPLAY1"),
            new SavedCoordinate(30, 30, @"\\.\DISPLAY1"));

        store.GetNextInMonitor(@"\\.\DISPLAY1"); // index 0
        store.GetNextInMonitor(@"\\.\DISPLAY1"); // index 1
        store.ResetCursor();

        var afterReset = store.GetNextInMonitor(@"\\.\DISPLAY1");
        Assert.Equal((10, 10), (afterReset!.X, afterReset.Y));
    }

    // ── v1.9.3: モニタ安定キー（ドック着脱でのデバイス名振り直し対策） ──

    private static MonitorInfo Mon(string name, string key, string friendly, int left)
        => new MonitorInfo(name, key, friendly, new System.Drawing.Rectangle(left, 0, 1920, 1080));

    [Fact]
    public void Load_does_not_overwrite_an_existing_key()
    {
        var store = new CoordinateStore();
        bool migrated = store.Load(new[]
        {
            new SavedCoordinate(100, 200,
                System.Windows.Forms.Screen.PrimaryScreen!.DeviceName, 100, 200, "KEY_ALREADY_SET", "Mon|1x1"),
        });

        Assert.False(migrated);
        Assert.Equal("KEY_ALREADY_SET", store.GetAll()[0].MonitorKey);
    }

    [Fact]
    public void Load_leaves_key_empty_when_the_saved_monitor_is_not_connected()
    {
        // 存在しないデバイス名 → 補完しようがないので空のまま（従来動作へフォールバック）
        var store = new CoordinateStore();
        bool migrated = store.Load(new[]
        {
            new SavedCoordinate(100, 200, @"\\.\DISPLAY_NOT_CONNECTED", 100, 200),
        });

        Assert.False(migrated);
        Assert.Equal(string.Empty, store.GetAll()[0].MonitorKey);
    }

    [Fact]
    public void Load_backfills_key_from_the_current_device_name_mapping()
    {
        // 実機依存: 安定キーが取得できる環境でのみ補完を検証する
        var primary = System.Windows.Forms.Screen.PrimaryScreen!.DeviceName;
        var snapshot = MonitorIdentity.Snapshot();
        string expectedKey = string.Empty;
        foreach (var m in snapshot)
        {
            if (m.GdiDeviceName == primary) { expectedKey = m.StableKey; break; }
        }
        if (string.IsNullOrEmpty(expectedKey)) return; // キー非対応環境ではスキップ

        var store = new CoordinateStore();
        bool migrated = store.Load(new[] { new SavedCoordinate(100, 200, primary, 100, 200) });

        Assert.True(migrated);
        Assert.Equal(expectedKey, store.GetAll()[0].MonitorKey);
    }

    [Fact]
    public void GetNextInMonitor_groups_by_stable_key_not_device_name()
    {
        // 保存時 DISPLAY1 だった 2 点が、再接続後は DISPLAY2 という名前のモニタに乗っている
        var store = BuildStoreWith(
            new SavedCoordinate(10, 10, @"\\.\DISPLAY1", 10, 10, "KEY_A", "MonA|1920x1080"),
            new SavedCoordinate(20, 20, @"\\.\DISPLAY1", 20, 20, "KEY_A", "MonA|1920x1080"),
            new SavedCoordinate(30, 30, @"\\.\DISPLAY2", 30, 30, "KEY_B", "MonB|1920x1080"));

        var monitorA = Mon(@"\\.\DISPLAY2", "KEY_A", "MonA", 1920);

        var first  = store.GetNextInMonitor(monitorA);
        var second = store.GetNextInMonitor(monitorA);
        var third  = store.GetNextInMonitor(monitorA);

        // KEY_A の 2 点だけを循環する（名前が DISPLAY2 でも KEY_B の座標は拾わない）
        Assert.Equal((10, 10), (first!.X, first.Y));
        Assert.Equal((20, 20), (second!.X, second.Y));
        Assert.Equal((10, 10), (third!.X, third.Y));
    }

    [Fact]
    public void GetNextInMonitor_returns_null_when_key_has_no_coordinates()
    {
        var store = BuildStoreWith(
            new SavedCoordinate(10, 10, @"\\.\DISPLAY1", 10, 10, "KEY_A", "MonA|1920x1080"));

        Assert.Null(store.GetNextInMonitor(Mon(@"\\.\DISPLAY1", "KEY_OTHER", "MonZ", 0)));
    }

    [Fact]
    public void GetNext_with_snapshot_skips_coordinates_whose_key_is_gone()
    {
        var store = BuildStoreWith(
            new SavedCoordinate(10, 10, @"\\.\DISPLAY1", 10, 10, "KEY_A", "MonA|1920x1080"),
            new SavedCoordinate(20, 20, @"\\.\DISPLAY2", 20, 20, "KEY_GONE", "Gone|1920x1080"),
            new SavedCoordinate(30, 30, @"\\.\DISPLAY3", 30, 30, "KEY_C", "MonC|1920x1080"));

        // 名前は 3 つとも存在するが、KEY_GONE の物理モニタだけ外れている
        var monitors = new[]
        {
            Mon(@"\\.\DISPLAY1", "KEY_C", "MonC", 0),
            Mon(@"\\.\DISPLAY2", "KEY_A", "MonA", 1920),
            Mon(@"\\.\DISPLAY3", "KEY_X", "MonX", 3840),
        };

        var first  = store.GetNext(monitors);
        var second = store.GetNext(monitors);
        var third  = store.GetNext(monitors);

        Assert.Equal((10, 10), (first!.X, first.Y));
        Assert.Equal((30, 30), (second!.X, second.Y));  // KEY_GONE をスキップ
        Assert.Equal((10, 10), (third!.X, third.Y));
    }
}
