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

    public int Count => _coordinates.Count;

    public void Add(int x, int y)
    {
        var screen = Screen.FromPoint(new Point(x, y));
        _coordinates.Add(new SavedCoordinate(x, y, screen.DeviceName));
    }

    public SavedCoordinate? GetNext()
    {
        if (_coordinates.Count == 0) return null;
        _currentIndex = (_currentIndex + 1) % _coordinates.Count;
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
        _monitorIndices.TryGetValue(monitorDeviceName, out int lastRawIndex);
        if (!_monitorIndices.ContainsKey(monitorDeviceName))
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

        return true;
    }
}
