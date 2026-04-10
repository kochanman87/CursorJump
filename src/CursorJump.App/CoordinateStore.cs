using System.Collections.Generic;
using CursorJump.App.Models;

namespace CursorJump.App;

internal sealed class CoordinateStore
{
    private readonly List<SavedCoordinate> _coordinates = new();
    private int _currentIndex = -1;

    public int Count => _coordinates.Count;

    public void Add(int x, int y)
    {
        _coordinates.Add(new SavedCoordinate(x, y));
    }

    public SavedCoordinate? GetNext()
    {
        if (_coordinates.Count == 0) return null;
        _currentIndex = (_currentIndex + 1) % _coordinates.Count;
        return _coordinates[_currentIndex];
    }

    public IReadOnlyList<SavedCoordinate> GetAll() => _coordinates.AsReadOnly();

    public bool RemoveAt(int index)
    {
        if (index < 0 || index >= _coordinates.Count) return false;

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
