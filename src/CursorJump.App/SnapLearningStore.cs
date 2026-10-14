using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace CursorJump.App;

/// <summary>
/// 要素スナップの学習データ（よくクリックする要素のスコア）。スレッドセーフ。
/// キーは <see cref="SnapElementRules.BuildLearnKey"/> のハッシュ。スコアは時間で半減していく。
/// 保存は settings.json とは別ファイル（壊れても設定やライセンスに波及させないため）。
/// </summary>
internal sealed class SnapLearningStore
{
    private sealed class Entry
    {
        public double S { get; set; }   // 最終更新時点のスコア
        public long T { get; set; }     // 最終更新 (UTC ticks)
    }

    private sealed class FileModel
    {
        public int Version { get; set; } = 1;
        public Dictionary<string, Entry> Entries { get; set; } = new();
    }

    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly object _lock = new();

    public int Count { get { lock (_lock) return _entries.Count; } }

    private static double Decayed(Entry e, DateTime nowUtc)
    {
        double days = (nowUtc.Ticks - e.T) / (double)TimeSpan.TicksPerDay;
        if (days <= 0) return e.S;
        return e.S * Math.Pow(0.5, days / SnapTuning.LearningHalfLifeDays);
    }

    /// <summary>クリックを 1 件（または amount 件分）記録する。</summary>
    public void Record(string? key, DateTime nowUtc, double amount = 1.0)
    {
        if (string.IsNullOrEmpty(key) || amount <= 0) return;
        lock (_lock)
        {
            if (_entries.TryGetValue(key, out var e))
            {
                e.S = Decayed(e, nowUtc) + amount;
                e.T = nowUtc.Ticks;
            }
            else
            {
                _entries[key] = new Entry { S = amount, T = nowUtc.Ticks };
                if (_entries.Count > SnapTuning.LearningMaxEntries) PruneLocked(nowUtc);
            }
        }
    }

    public double Score(string? key, DateTime nowUtc)
    {
        if (string.IsNullOrEmpty(key)) return 0;
        lock (_lock)
            return _entries.TryGetValue(key, out var e) ? Decayed(e, nowUtc) : 0;
    }

    /// <summary>距離スコアに掛ける重み。学習なし = 1、よく押すほど小さい（= 近く扱い）。</summary>
    public double Weight(string? key, DateTime nowUtc)
        => WeightFromScore(Score(key, nowUtc));

    public static double WeightFromScore(double score)
        => score <= 0 ? 1.0 : 1.0 / (1.0 + SnapTuning.LearningStrength * Math.Log2(1.0 + score));

    public void Clear() { lock (_lock) _entries.Clear(); }

    /// <summary>上限の 9 割までスコアの低いものから捨てる（毎回 1 件ずつ捨てると上限付近で頻繁に走るため）。</summary>
    private void PruneLocked(DateTime nowUtc)
    {
        int keep = SnapTuning.LearningMaxEntries * 9 / 10;
        var drop = _entries.OrderBy(kv => Decayed(kv.Value, nowUtc)).Take(_entries.Count - keep).Select(kv => kv.Key).ToList();
        foreach (var k in drop) _entries.Remove(k);
    }

    public string Serialize()
    {
        lock (_lock)
        {
            var model = new FileModel();
            foreach (var kv in _entries) model.Entries[kv.Key] = new Entry { S = kv.Value.S, T = kv.Value.T };
            return JsonSerializer.Serialize(model);
        }
    }

    /// <summary>JSON から読み込む。壊れていれば空のストアを返す（例外を投げない）。</summary>
    public static SnapLearningStore Deserialize(string json) => TryDeserialize(json, out var store) ? store : store;

    private static bool TryDeserialize(string json, out SnapLearningStore store)
    {
        store = new SnapLearningStore();
        FileModel? model;
        try { model = JsonSerializer.Deserialize<FileModel>(json); }
        catch (JsonException) { return false; }
        if (model?.Entries is null) return false;

        foreach (var kv in model.Entries)
        {
            if (string.IsNullOrEmpty(kv.Key) || kv.Value is null) continue;
            if (double.IsNaN(kv.Value.S) || kv.Value.S <= 0) continue;
            store._entries[kv.Key] = new Entry { S = kv.Value.S, T = kv.Value.T };
        }
        return true;
    }

    /// <summary>
    /// ファイルから読み込む。無ければ空。壊れていれば <c>.bad</c> にコピーを残して空から始める
    /// （次回保存で本体は上書きされるため、壊れた内容を調査用に退避しておく）。
    /// </summary>
    public static SnapLearningStore LoadFile(string path)
    {
        if (!File.Exists(path)) return new SnapLearningStore();
        if (TryDeserialize(File.ReadAllText(path), out var store)) return store;
        File.Copy(path, path + ".bad", overwrite: true);
        return store;
    }

    /// <summary>一時ファイルに書いてから置き換える（書き込み途中で落ちても既存ファイルを壊さない）。</summary>
    public void SaveFile(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, Serialize());
        File.Move(tmp, path, overwrite: true);
    }
}
