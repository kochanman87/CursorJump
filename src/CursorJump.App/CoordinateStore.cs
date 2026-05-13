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
    // モニタ別の循環インデックス（GetNextInMonitor 用）
    private readonly Dictionary<string, int> _monitorIndices = new();

    /// <summary>座標リストが変更された（Add/RemoveAt/Clear/Load）ときに発火。永続化フックに使う。</summary>
    public event Action? Changed;

    public int Count => _coordinates.Count;

    public void Add(int x, int y)
    {
        var screen = Screen.FromPoint(new Point(x, y));
        _coordinates.Add(new SavedCoordinate(x, y, screen.DeviceName));
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
    public SavedCoordinate? GetNext(IReadOnlyList<string> connectedDeviceNames)
    {
        if (_coordinates.Count == 0) return null;

        var indices = new List<int>();
        for (int i = 0; i < _coordinates.Count; i++)
        {
            if (MonitorFilter.IsCoordinateOnConnectedMonitor(_coordinates[i], connectedDeviceNames))
                indices.Add(i);
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
    public SavedCoordinate? GetNextInMonitor(string monitorDeviceName)
    {
        // 該当モニタの座標インデックス一覧を取得
        var indices = _coordinates
            .Select((c, i) => (coord: c, index: i))
            .Where(t => t.coord.MonitorDeviceName == monitorDeviceName)
            .Select(t => t.index)
            .ToList();

        if (indices.Count == 0) return null;

        // モニタ別インデックスを取得（未登録なら -1 から開始）
        if (!_monitorIndices.TryGetValue(monitorDeviceName, out int lastRawIndex))
            lastRawIndex = -1;

        // 次のインデックス位置を循環
        int nextPos = (lastRawIndex + 1) % indices.Count;
        _monitorIndices[monitorDeviceName] = nextPos;

        return _coordinates[indices[nextPos]];
    }

    public IReadOnlyList<SavedCoordinate> GetAll() => _coordinates.AsReadOnly();

    public bool RemoveAt(int index)
    {
        if (index < 0 || index >= _coordinates.Count) return false;

        // 削除座標が属するモニタのインデックスをリセット（ずれ防止）
        var removedMonitor = _coordinates[index].MonitorDeviceName;
        _monitorIndices.Remove(removedMonitor);

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
    /// Changed は発火しない（永続化先と同じデータの再注入のため、ループを避ける）。
    /// </summary>
    public void Load(IEnumerable<SavedCoordinate> coordinates)
    {
        _coordinates.Clear();
        _currentIndex = -1;
        _monitorIndices.Clear();
        foreach (var c in coordinates)
        {
            string monitor = c.MonitorDeviceName;
            if (string.IsNullOrEmpty(monitor))
            {
                try
                {
                    monitor = Screen.FromPoint(new Point(c.X, c.Y)).DeviceName;
                }
                catch
                {
                    monitor = string.Empty;
                }
            }
            _coordinates.Add(new SavedCoordinate(c.X, c.Y, monitor));
        }
    }
}
