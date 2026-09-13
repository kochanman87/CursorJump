using System;

namespace CursorJump.App;

/// <summary>
/// 通常の左クリック位置を内部的に記録する短命なリングバッファ（v1.9.0+）。
/// 「戻る」ショートカット（Cursor の Ctrl+Z 風）で過去のクリック位置へカーソルを戻すために使う。
/// ジャンプポイント（Set A/B）とは完全に別系統。
///
/// セマンティクス:
/// - <see cref="Record"/> は新しいクリックを記録し、巡回ポインタを最新へリセットする。
///   直前の記録とほぼ同じ場所（ダブルクリック等の同一地点連打）は重複登録しない。
/// - <see cref="Back"/> は「最近 <c>depth</c> 件の記録」を新しい順に<b>循環</b>して返す（端まで行ったら先頭へラップ）。
///   <b>現在カーソルに近い記録（= 今いる場所）はスキップ</b>して次へ進むため、無意味なその場ジャンプにならない。
///   窓内の全件がカーソル付近（実質 1 点しか無い等）のときのみ null。
/// </summary>
internal sealed class ClickHistory
{
    /// <summary>履歴の最大保持数。</summary>
    public const int MaxDepth = 10;

    /// <summary>
    /// 「現在カーソルと同じ場所」とみなすスキップ半径（物理ピクセル）。
    /// クリック直後はカーソルが記録点とほぼ同一座標になるため、その記録を「今いる場所」として読み飛ばす。
    /// 削除モードのマーカー近接判定 <c>OverlayService.SnapDistancePhysical = 40</c> と同値に揃えている。
    /// </summary>
    public const int SkipRadiusPx = 40;

    /// <summary>
    /// 直前の記録と「同一地点」とみなして重複登録を抑止する半径（物理ピクセル）。
    /// ダブルクリック（実質 0px の同一座標連打）が履歴を 2 件消費するのを防ぐ。
    /// 隣接する別の UI 要素（通常 16px 以上離れている）を巻き込まないよう小さめにしている。
    /// </summary>
    private const int DedupRadiusPx = 8;

    private readonly (int X, int Y)[] _buffer = new (int, int)[MaxDepth];
    private int _count;     // 有効な記録数（0..MaxDepth）
    private int _head;      // 次に書き込む位置（最新の 1 つ先）
    private int _pos;       // 巡回ポインタ。最後に返した記録の「最新からのオフセット」(0=未開始, 1..window)
    private readonly object _lock = new();

    /// <summary>
    /// 左クリック座標を記録する。巡回ポインタを最新へリセットする。
    /// 直前の記録とほぼ同じ場所（ダブルクリック等の同一地点連打）は重複登録しない。
    /// </summary>
    public void Record(int x, int y)
    {
        lock (_lock)
        {
            // 直前の記録と同一地点なら重複登録せず、巡回ポインタだけリセットする
            // （ダブルクリックで履歴が 2 件作られるのを防ぐ）。
            if (_count > 0)
            {
                int lastIdx = ((_head - 1) % MaxDepth + MaxDepth) % MaxDepth;
                var last = _buffer[lastIdx];
                long ddx = last.X - x;
                long ddy = last.Y - y;
                if (ddx * ddx + ddy * ddy <= (long)DedupRadiusPx * DedupRadiusPx)
                {
                    _pos = 0;
                    return;
                }
            }

            _buffer[_head] = (x, y);
            _head = (_head + 1) % MaxDepth;
            if (_count < MaxDepth) _count++;
            _pos = 0;
        }
    }

    /// <summary>
    /// 最近 <paramref name="depth"/>（1..MaxDepth）件の記録を新しい順に循環して 1 つ返す。
    /// 端まで行ったら先頭（最新）へラップする。現在カーソル <paramref name="curX"/>/<paramref name="curY"/> から
    /// <paramref name="skipRadius"/> 以内の記録（= 今いる場所）はスキップして次へ進む。
    /// 窓内の全件がカーソル付近、または履歴が空のときは null。
    /// </summary>
    public (int X, int Y)? Back(int depth, int curX, int curY, int skipRadius)
    {
        lock (_lock)
        {
            int window = Math.Min(Math.Clamp(depth, 1, MaxDepth), _count);
            int offset = FindNextOffset(window, curX, curY, skipRadius, out var nextPos);
            _pos = nextPos;
            return offset == 0 ? null : GetAtOffset(offset);
        }
    }

    /// <summary>新しい順の番号と次の移動先を、巡回ポインタを変更せず一括取得する。</summary>
    public ClickHistoryMarker[] GetMarkers(int depth, int curX, int curY, int skipRadius)
    {
        lock (_lock)
        {
            int window = Math.Min(Math.Clamp(depth, 1, MaxDepth), _count);
            if (window == 0) return Array.Empty<ClickHistoryMarker>();
            int next = FindNextOffset(window, curX, curY, skipRadius, out _);
            var markers = new ClickHistoryMarker[window];
            for (int offset = 1; offset <= window; offset++)
            {
                var p = GetAtOffset(offset);
                markers[offset - 1] = new ClickHistoryMarker(offset, p.X, p.Y, offset == next);
            }
            return markers;
        }
    }

    // 描画の予告と Back の判定を共通化する。呼び出し元でロックを保持すること。
    private int FindNextOffset(int window, int curX, int curY, int skipRadius, out int nextPos)
    {
        nextPos = _pos;
        long r2 = (long)skipRadius * skipRadius;
        for (int tries = 0; tries < window; tries++)
        {
            nextPos = (nextPos % window) + 1;
            var p = GetAtOffset(nextPos);
            long dx = (long)p.X - curX;
            long dy = (long)p.Y - curY;
            if (dx * dx + dy * dy > r2) return nextPos;
        }
        return 0;
    }

    private (int X, int Y) GetAtOffset(int offset) =>
        _buffer[(_head - offset + MaxDepth) % MaxDepth];

    /// <summary>履歴を全消去する。</summary>
    public void Clear()
    {
        lock (_lock)
        {
            _count = 0;
            _head = 0;
            _pos = 0;
        }
    }
}

internal readonly record struct ClickHistoryMarker(int Number, int X, int Y, bool IsNext);
