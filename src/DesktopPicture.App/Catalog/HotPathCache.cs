using System;
using System.Collections.Generic;

namespace DesktopPicture.Catalog;

public sealed class HotPathCache
{
    private readonly int _capacity;
    private readonly Dictionary<long, LinkedListNode<(long Id, string Path)>> _map;
    private readonly LinkedList<(long Id, string Path)> _lruList;
    private readonly object _lock = new();

    public HotPathCache(int capacity = 1024)
    {
        _capacity = Math.Max(1, capacity);
        _map = new Dictionary<long, LinkedListNode<(long Id, string Path)>>(_capacity);
        _lruList = new LinkedList<(long Id, string Path)>();
    }

    public bool TryGet(long id, out string path)
    {
        lock (_lock)
        {
            if (_map.TryGetValue(id, out var node))
            {
                _lruList.Remove(node);
                _lruList.AddFirst(node);
                path = node.Value.Path;
                return true;
            }
        }
        path = string.Empty;
        return false;
    }

    public void Put(long id, string path)
    {
        lock (_lock)
        {
            if (_map.TryGetValue(id, out var existing))
            {
                _lruList.Remove(existing);
                _map.Remove(id);
            }
            else if (_map.Count >= _capacity)
            {
                var oldest = _lruList.Last;
                if (oldest != null)
                {
                    _lruList.RemoveLast();
                    _map.Remove(oldest.Value.Id);
                }
            }

            var newNode = _lruList.AddFirst((id, path));
            _map[id] = newNode;
        }
    }

    public void Remove(long id)
    {
        lock (_lock)
        {
            if (_map.TryGetValue(id, out var node))
            {
                _lruList.Remove(node);
                _map.Remove(id);
            }
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _map.Clear();
            _lruList.Clear();
        }
    }
}
