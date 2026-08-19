using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using CursorJump.App.Models;

namespace CursorJump.App;

internal sealed class CoordinateStore
{
    private readonly List<SavedCoordinate> _coordinates = new();
    private int _currentIndex = -1;
    // モニタ別の循環インデックス（GetNextInMonitor 用）。
    // キーはモニタの安定キー（取得できない環境ではデバイス名）＝ MonitorInfo.GroupKey。
    private readonly Dictionary<string, int> _monitorIndices = new();

    /// <summary>座標リストが変更された（Add/RemoveAt/Clear/Load）ときに発火。永続化フックに使う。</summary>
    public event Action? Changed;

    public int Count => _coordinates.Count;

    public void Add(int x, int y)
    {
        string deviceName = string.Empty;
        string key = string.Empty;
        string fingerprint = string.Empty;
        int relX = -1, relY = -1;

        // モニタ内相対座標 (左上原点からのピクセルオフセット) と、
        // ドック着脱を跨いで安定なモニタキー（v1.9.3+）を同時に記録する。
        // 再生時はキーで物理モニタを特定し、その現 Bounds から絶対座標を再計算する。
        try
        {
            var monitors = MonitorIdentity.Snapshot();
            var monitor = MonitorIdentity.FromPoint(monitors, x, y);
            if (monitor is not null)
            {
                deviceName  = monitor.Value.GdiDeviceName;
                key         = monitor.Value.StableKey;
                fingerprint = monitor.Value.Fingerprint;
                relX = x - monitor.Value.Bounds.Left;
                relY = y - monitor.Value.Bounds.Top;
            }
        }
        catch (Exception ex)
        {
            DebugLog.Write($"CoordinateStore.Add: monitor snapshot failed: {ex.GetType().Name}");
        }

        if (string.IsNullOrEmpty(deviceName))
        {
            // スナップショット取得失敗 / どのモニタ矩形にも含まれない座標 → 従来動作
            var screen = Screen.FromPoint(new Point(x, y));
            deviceName = screen.DeviceName;
            relX = x - screen.Bounds.Left;
            relY = y - screen.Bounds.Top;
        }

        _coordinates.Add(new SavedCoordinate(x, y, deviceName, relX, relY, key, fingerprint));
        Changed?.Invoke();
    }

    public SavedCoordinate? GetNext()
    {
        if (_coordinates.Count == 0) return null;
        _currentIndex = (_currentIndex + 1) % _coordinates.Count;
        return _coordinates[_currentIndex];
    }

    /// <summary>
    /// 接続中のモニタに存在する座標のみを循環して返す。
    /// 未接続モニタの座標は飛ばし、該当0件なら null。
    /// </summary>
    public SavedCoordinate? GetNext(IReadOnlyList<MonitorInfo> monitors)
        => GetNextFiltered(c => MonitorFilter.IsCoordinateOnConnectedMonitor(c, monitors));

    /// <summary>デバイス名一覧のみで判定する簡易版（従来動作）。</summary>
    public SavedCoordinate? GetNext(IReadOnlyList<string> connectedDeviceNames)
        => GetNextFiltered(c => MonitorFilter.IsCoordinateOnConnectedMonitor(c, connectedDeviceNames));

    private SavedCoordinate? GetNextFiltered(Func<SavedCoordinate, bool> isConnected)
    {
        if (_coordinates.Count == 0) return null;

        var indices = new List<int>();
        for (int i = 0; i < _coordinates.Count; i++)
        {
            if (isConnected(_coordinates[i])) indices.Add(i);
        }
        if (indices.Count == 0) return null;

        // 直近 _currentIndex の次の有効座標へ進む
        // 「次に大きい有効インデックス」を探し、無ければ先頭に戻る
        int nextValid = -1;
        for (int j = 0; j < indices.Count; j++)
        {
            if (indices[j] > _currentIndex)
            {
                nextValid = indices[j];
                break;
            }
        }
        if (nextValid < 0) nextValid = indices[0];

        _currentIndex = nextValid;
        return _coordinates[_currentIndex];
    }

    /// <summary>
    /// 指定モニタ内の座標のみを循環して返す。
    /// 該当モニタに座標が存在しない場合は null を返す（フォールバックなし）。
    /// </summary>
    public SavedCoordinate? GetNextInMonitor(MonitorInfo monitor)
    {
        var indices = IndicesOnMonitor(monitor);
        if (indices.Count == 0) return null;

        string groupKey = monitor.GroupKey;

        // モニタ別インデックスを取得（未登録なら -1 から開始）
        if (!_monitorIndices.TryGetValue(groupKey, out int lastRawIndex))
            lastRawIndex = -1;

        // 次のインデックス位置を循環
        int nextPos = (lastRawIndex + 1) % indices.Count;
        _monitorIndices[groupKey] = nextPos;

        return _coordinates[indices[nextPos]];
    }

    /// <summary>デバイス名のみで指定する簡易版（安定キー非対応環境・旧テスト互換）。</summary>
    public SavedCoordinate? GetNextInMonitor(string monitorDeviceName)
        => GetNextInMonitor(NameOnly(monitorDeviceName));

    /// <summary>
    /// GetNext の逆方向版。直近インデックスから 1 つ戻った座標を循環で返す。
    /// </summary>
    public SavedCoordinate? GetPrev()
    {
        if (_coordinates.Count == 0) return null;
        if (_currentIndex < 0) _currentIndex = 0;
        _currentIndex = (_currentIndex - 1 + _coordinates.Count) % _coordinates.Count;
        return _coordinates[_currentIndex];
    }

    /// <summary>
    /// 接続中モニタ限定 GetNext の逆方向版。
    /// 「次に小さい有効インデックス」を探し、無ければ末尾 (= 最大の有効インデックス)。
    /// </summary>
    public SavedCoordinate? GetPrev(IReadOnlyList<MonitorInfo> monitors)
        => GetPrevFiltered(c => MonitorFilter.IsCoordinateOnConnectedMonitor(c, monitors));

    /// <summary>デバイス名一覧のみで判定する簡易版（従来動作）。</summary>
    public SavedCoordinate? GetPrev(IReadOnlyList<string> connectedDeviceNames)
        => GetPrevFiltered(c => MonitorFilter.IsCoordinateOnConnectedMonitor(c, connectedDeviceNames));

    private SavedCoordinate? GetPrevFiltered(Func<SavedCoordinate, bool> isConnected)
    {
        if (_coordinates.Count == 0) return null;

        var indices = new List<int>();
        for (int i = 0; i < _coordinates.Count; i++)
        {
            if (isConnected(_coordinates[i])) indices.Add(i);
        }
        if (indices.Count == 0) return null;

        int prevValid = -1;
        for (int j = indices.Count - 1; j >= 0; j--)
        {
            if (indices[j] < _currentIndex)
            {
                prevValid = indices[j];
                break;
            }
        }
        if (prevValid < 0) prevValid = indices[^1];

        _currentIndex = prevValid;
        return _coordinates[_currentIndex];
    }

    /// <summary>
    /// GetNextInMonitor の逆方向版。初回 (未登録) は末尾の有効座標、それ以降は循環で 1 つ戻る。
    /// </summary>
    public SavedCoordinate? GetPrevInMonitor(MonitorInfo monitor)
    {
        var indices = IndicesOnMonitor(monitor);
        if (indices.Count == 0) return null;

        string groupKey = monitor.GroupKey;

        int nextPos;
        if (!_monitorIndices.TryGetValue(groupKey, out int lastRawIndex))
            nextPos = indices.Count - 1;
        else
            nextPos = (lastRawIndex - 1 + indices.Count) % indices.Count;

        _monitorIndices[groupKey] = nextPos;
        return _coordinates[indices[nextPos]];
    }

    /// <summary>デバイス名のみで指定する簡易版（安定キー非対応環境・旧テスト互換）。</summary>
    public SavedCoordinate? GetPrevInMonitor(string monitorDeviceName)
        => GetPrevInMonitor(NameOnly(monitorDeviceName));

    /// <summary>
    /// 指定モニタに属する座標のインデックス一覧。
    /// 座標・モニタの双方が安定キーを持つならキーで、そうでなければデバイス名で照合する。
    /// </summary>
    private List<int> IndicesOnMonitor(MonitorInfo monitor)
    {
        var indices = new List<int>();
        for (int i = 0; i < _coordinates.Count; i++)
        {
            if (BelongsTo(_coordinates[i], monitor)) indices.Add(i);
        }
        return indices;
    }

    private static bool BelongsTo(SavedCoordinate coord, MonitorInfo monitor)
    {
        if (!string.IsNullOrEmpty(coord.MonitorKey) && !string.IsNullOrEmpty(monitor.StableKey))
            return string.Equals(coord.MonitorKey, monitor.StableKey, StringComparison.OrdinalIgnoreCase);

        return string.Equals(coord.MonitorDeviceName, monitor.GdiDeviceName, StringComparison.OrdinalIgnoreCase);
    }

    private static MonitorInfo NameOnly(string deviceName)
        => new MonitorInfo(deviceName, string.Empty, string.Empty, Rectangle.Empty);

    public IReadOnlyList<SavedCoordinate> GetAll() => _coordinates.AsReadOnly();

    public bool RemoveAt(int index)
    {
        if (index < 0 || index >= _coordinates.Count) return false;

        // 削除座標が属するモニタのインデックスをリセット（ずれ防止）。
        // グルーピングキーは安定キー優先だが、旧データはデバイス名なので両方消す。
        var removed = _coordinates[index];
        if (!string.IsNullOrEmpty(removed.MonitorKey)) _monitorIndices.Remove(removed.MonitorKey);
        if (!string.IsNullOrEmpty(removed.MonitorDeviceName)) _monitorIndices.Remove(removed.MonitorDeviceName);

        _coordinates.RemoveAt(index);

        if (_coordinates.Count == 0)
        {
            _currentIndex = -1;
        }
        else if (_currentIndex >= _coordinates.Count)
        {
            _currentIndex = _coordinates.Count - 1;
        }

        Changed?.Invoke();
        return true;
    }

    /// <summary>
    /// 座標は保持したまま循環インデックスのみ先頭前 (-1) へ戻す（v1.9.0+）。
    /// 次の <see cref="GetNext()"/> / <see cref="GetNext(IReadOnlyList{MonitorInfo})"/> は先頭の有効座標を、
    /// <see cref="GetNextInMonitor(MonitorInfo)"/> は各モニタ先頭から再開する。即ジャンプはしない。
    /// 座標自体は変更しないため <see cref="Changed"/> は発火しない（無駄な永続化を避ける）。
    /// </summary>
    public void ResetCursor()
    {
        _currentIndex = -1;
        _monitorIndices.Clear();
    }

    /// <summary>全座標を削除し、インデックスをリセットする。</summary>
    public void Clear()
    {
        bool wasNonEmpty = _coordinates.Count > 0;
        _coordinates.Clear();
        _currentIndex = -1;
        _monitorIndices.Clear();
        if (wasNonEmpty) Changed?.Invoke();
    }

    /// <summary>
    /// 既存座標をクリアして指定リストで初期化する。アプリ起動時の永続化座標復元用。
    /// MonitorDeviceName が空の場合は座標から再判定する（旧 settings.json 互換）。
    /// MonitorRelativeX/Y が -1 (旧 settings.json 互換) なら、現在の Screen.Bounds から相対座標を補完する。
    /// MonitorKey / MonitorFingerprint が空 (v1.9.2 以前のデータ) なら、
    /// 「実行時点の DeviceName ↔ 物理モニタ対応」を正としてキーを補完する（v1.9.3+）。
    /// 補完が発生した場合は MainWindow 側の Changed イベント経由で settings.json に書き戻される
    /// (Changed は呼出側の責務で発火させる。Load 自体は発火しない仕様を維持)。
    /// </summary>
    /// <returns>補完が 1 件以上発生した場合 true (呼出側で Save 推奨)</returns>
    public bool Load(IEnumerable<SavedCoordinate> coordinates)
    {
        _coordinates.Clear();
        _currentIndex = -1;
        _monitorIndices.Clear();
        bool migrated = false;

        IReadOnlyList<MonitorInfo> monitors;
        try { monitors = MonitorIdentity.Snapshot(); }
        catch { monitors = Array.Empty<MonitorInfo>(); }

        foreach (var c in coordinates)
        {
            string monitor = c.MonitorDeviceName;
            int relX = c.MonitorRelativeX;
            int relY = c.MonitorRelativeY;
            string key = c.MonitorKey;
            string fingerprint = c.MonitorFingerprint;

            if (string.IsNullOrEmpty(monitor))
            {
                try
                {
                    var screen = Screen.FromPoint(new Point(c.X, c.Y));
                    monitor = screen.DeviceName;
                    if (relX < 0 || relY < 0)
                    {
                        relX = c.X - screen.Bounds.Left;
                        relY = c.Y - screen.Bounds.Top;
                        migrated = true;
                    }
                }
                catch
                {
                    monitor = string.Empty;
                }
            }
            else if (relX < 0 || relY < 0)
            {
                // モニタ名はあるが相対座標が未設定 → 該当モニタの Bounds から補完
                try
                {
                    var screen = Screen.AllScreens.FirstOrDefault(s => s.DeviceName == monitor);
                    if (screen is not null)
                    {
                        relX = c.X - screen.Bounds.Left;
                        relY = c.Y - screen.Bounds.Top;
                        migrated = true;
                    }
                }
                catch { }
            }

            // 安定キー / フィンガープリントの補完（v1.9.3 マイグレーション）。
            // 「実行時点の DeviceName ↔ 物理モニタ対応」を正とするため、
            // 既に振り直しが起きている状態で初回起動すると誤ったキーが埋まる可能性がある
            // （その場合は座標を保存し直せば解消する）。
            if ((string.IsNullOrEmpty(key) || string.IsNullOrEmpty(fingerprint))
                && !string.IsNullOrEmpty(monitor))
            {
                for (int i = 0; i < monitors.Count; i++)
                {
                    if (!string.Equals(monitors[i].GdiDeviceName, monitor, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(monitors[i].StableKey))
                    {
                        key = monitors[i].StableKey;
                        migrated = true;
                    }
                    if (string.IsNullOrEmpty(fingerprint) && !string.IsNullOrEmpty(monitors[i].Fingerprint))
                    {
                        fingerprint = monitors[i].Fingerprint;
                        migrated = true;
                    }
                    break;
                }
            }

            _coordinates.Add(new SavedCoordinate(c.X, c.Y, monitor, relX, relY, key, fingerprint));
        }
        return migrated;
    }
}
