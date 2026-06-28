using CursorJump.App;
using Xunit;

namespace CursorJump.Tests;

public class ClickHistoryTests
{
    private const int Radius = 40;
    // カーソルを十分遠くに置くことで「現在位置スキップ」を無効化し、純粋な巡回挙動を検証する。
    private const int Far = 1_000_000;

    private static (int X, int Y)? Back(ClickHistory h, int depth) => h.Back(depth, Far, Far, Radius);
    private static (int, int) Xy((int X, int Y)? p) => (p!.Value.X, p.Value.Y);

    [Fact]
    public void Back_on_empty_returns_null()
    {
        var h = new ClickHistory();
        Assert.Null(Back(h, 2));
    }

    [Fact]
    public void Back_returns_most_recent_click_first()
    {
        var h = new ClickHistory();
        h.Record(10, 10);
        h.Record(200, 200);
        Assert.Equal((200, 200), Xy(Back(h, 5)));
    }

    [Fact]
    public void Cycles_through_window_newest_first_and_wraps()
    {
        var h = new ClickHistory();
        h.Record(100, 100);   // A
        h.Record(200, 200);   // B
        h.Record(300, 300);   // C（最新）

        // depth>=3 なので全 3 点を循環。新しい順 C→B→A、その後 先頭(C)へラップ。
        Assert.Equal((300, 300), Xy(Back(h, 5)));
        Assert.Equal((200, 200), Xy(Back(h, 5)));
        Assert.Equal((100, 100), Xy(Back(h, 5)));
        Assert.Equal((300, 300), Xy(Back(h, 5))); // ラップして先頭へ
        Assert.Equal((200, 200), Xy(Back(h, 5)));
    }

    [Fact]
    public void Depth_limits_cycle_to_most_recent_n()
    {
        var h = new ClickHistory();
        h.Record(100, 100);
        h.Record(200, 200);
        h.Record(300, 300);
        h.Record(400, 400);
        h.Record(500, 500); // 最新

        // depth=2 → 最近 2 点 (500, 400) のみを循環。古い点には行かない。
        Assert.Equal((500, 500), Xy(Back(h, 2)));
        Assert.Equal((400, 400), Xy(Back(h, 2)));
        Assert.Equal((500, 500), Xy(Back(h, 2)));
        Assert.Equal((400, 400), Xy(Back(h, 2)));
    }

    [Fact]
    public void Record_resets_cycle_to_newest()
    {
        var h = new ClickHistory();
        h.Record(100, 100);
        h.Record(200, 200);
        h.Record(300, 300);

        Assert.Equal((300, 300), Xy(Back(h, 5)));
        Assert.Equal((200, 200), Xy(Back(h, 5)));

        // 新規記録で巡回が最新からやり直しになる
        h.Record(400, 400);
        Assert.Equal((400, 400), Xy(Back(h, 5)));
        Assert.Equal((300, 300), Xy(Back(h, 5)));
    }

    [Fact]
    public void Ring_buffer_drops_oldest_beyond_capacity()
    {
        var h = new ClickHistory();
        // MaxDepth=10 を超えて 12 件記録（各点は十分離す）。0,1 は破棄され 2..11 が残る。
        for (int i = 0; i < 12; i++) h.Record(i * 1000, i * 1000);

        // depth=MaxDepth で全保持分を循環。新しい順 11..2、その後ラップして 11。
        Assert.Equal((11000, 11000), Xy(Back(h, ClickHistory.MaxDepth)));
        for (int expected = 10; expected >= 2; expected--)
            Assert.Equal((expected * 1000, expected * 1000), Xy(Back(h, ClickHistory.MaxDepth)));
        Assert.Equal((11000, 11000), Xy(Back(h, ClickHistory.MaxDepth))); // ラップ
    }

    // ── 現在位置スキップ ──

    [Fact]
    public void Skip_current_position_returns_previous()
    {
        var h = new ClickHistory();
        h.Record(0, 0);       // A
        h.Record(100, 100);   // B（最新）

        // カーソルが B 上 → B はスキップされ A が返る
        Assert.Equal((0, 0), Xy(h.Back(5, 100, 100, Radius)));
        // カーソルが A 上 → A はスキップされ B が返る（循環）
        Assert.Equal((100, 100), Xy(h.Back(5, 0, 0, Radius)));
    }

    [Fact]
    public void No_skip_when_cursor_far_returns_most_recent()
    {
        var h = new ClickHistory();
        h.Record(0, 0);
        h.Record(100, 100);

        // カーソルがどの記録からも遠い → 最新 B が返る
        Assert.Equal((100, 100), Xy(h.Back(5, 5000, 5000, Radius)));
    }

    // ── 同一地点連打（ダブルクリック）の重複登録抑止 ──

    [Fact]
    public void DoubleClick_same_position_records_once()
    {
        var h = new ClickHistory();
        h.Record(50, 50);
        h.Record(50, 50);     // 重複 → 無視
        h.Record(300, 300);   // 別地点

        // 重複が無視されていれば循環長は 2（300→50→300…）。
        Assert.Equal((300, 300), Xy(Back(h, ClickHistory.MaxDepth)));
        Assert.Equal((50, 50), Xy(Back(h, ClickHistory.MaxDepth)));
        Assert.Equal((300, 300), Xy(Back(h, ClickHistory.MaxDepth))); // 2 件しか無いのでラップ
    }

    [Fact]
    public void Distinct_positions_are_both_recorded()
    {
        var h = new ClickHistory();
        h.Record(0, 0);
        h.Record(100, 100); // 十分離れている → 別エントリ

        Assert.Equal((100, 100), Xy(Back(h, ClickHistory.MaxDepth)));
        Assert.Equal((0, 0), Xy(Back(h, ClickHistory.MaxDepth)));
        Assert.Equal((100, 100), Xy(Back(h, ClickHistory.MaxDepth))); // ラップ
    }
}
