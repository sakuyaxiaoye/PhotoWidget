using System;
using System.Collections.Generic;
using System.Linq;

namespace DesktopPicture.Scanning;

public sealed class FolderCatalog
{
    private readonly object _lock = new();
    private string[] _snapshot = Array.Empty<string>();
    private readonly HashSet<string> _canonicalPaths = new(StringComparer.OrdinalIgnoreCase);

    public string RootPath { get; }
    public int Count
    {
        get
        {
            lock (_lock) return _snapshot.Length;
        }
    }

    public bool IsEmpty => Count == 0;

    public FolderCatalog(string rootPath)
    {
        RootPath = rootPath;
    }

    public bool AddCandidate(string path)
    {
        lock (_lock)
        {
            if (_canonicalPaths.Add(path))
            {
                _snapshot = _canonicalPaths.ToArray();
                return true;
            }
            return false;
        }
    }

    public void SetCandidates(IEnumerable<string> paths)
    {
        lock (_lock)
        {
            _canonicalPaths.Clear();
            foreach (var p in paths)
            {
                _canonicalPaths.Add(p);
            }
            _snapshot = _canonicalPaths.ToArray();
        }
    }

    public string[] GetSnapshot()
    {
        lock (_lock)
        {
            return _snapshot;
        }
    }

    public void RemoveCandidate(string path)
    {
        lock (_lock)
        {
            if (_canonicalPaths.Remove(path))
            {
                _snapshot = _canonicalPaths.ToArray();
            }
        }
    }
}
