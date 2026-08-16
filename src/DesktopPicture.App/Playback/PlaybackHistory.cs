using System;
using System.Collections.Generic;

namespace DesktopPicture.Playback;

public sealed class PlaybackHistory
{
    private readonly int _maxCapacity;
    private readonly List<string> _history = new();
    private int _currentIndex = -1;
    private readonly object _lock = new();

    public PlaybackHistory(int maxCapacity = 100)
    {
        _maxCapacity = Math.Max(10, maxCapacity);
    }

    public bool CanGoBack
    {
        get
        {
            lock (_lock)
            {
                return _currentIndex > 0;
            }
        }
    }

    public bool CanGoForward
    {
        get
        {
            lock (_lock)
            {
                return _currentIndex >= 0 && _currentIndex < _history.Count - 1;
            }
        }
    }

    public string? Current
    {
        get
        {
            lock (_lock)
            {
                if (_currentIndex >= 0 && _currentIndex < _history.Count)
                {
                    return _history[_currentIndex];
                }
                return null;
            }
        }
    }

    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _history.Count;
            }
        }
    }

    public void Push(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return;

        lock (_lock)
        {
            // If we are currently pointing at an existing item that is identical, do nothing
            if (_currentIndex >= 0 && _currentIndex < _history.Count && _history[_currentIndex] == filePath)
            {
                return;
            }

            // If we branched off from a previous history point, trim the forward history
            if (_currentIndex >= 0 && _currentIndex < _history.Count - 1)
            {
                _history.RemoveRange(_currentIndex + 1, _history.Count - (_currentIndex + 1));
            }

            _history.Add(filePath);

            // Trim oldest if exceeding max capacity
            while (_history.Count > _maxCapacity)
            {
                _history.RemoveAt(0);
            }

            _currentIndex = _history.Count - 1;
        }
    }

    public string? GoBack()
    {
        lock (_lock)
        {
            if (_currentIndex > 0)
            {
                _currentIndex--;
                return _history[_currentIndex];
            }
            return null;
        }
    }

    public string? GoForward()
    {
        lock (_lock)
        {
            if (_currentIndex >= 0 && _currentIndex < _history.Count - 1)
            {
                _currentIndex++;
                return _history[_currentIndex];
            }
            return null;
        }
    }

    public string? PeekForward()
    {
        lock (_lock)
        {
            if (_currentIndex >= 0 && _currentIndex < _history.Count - 1)
            {
                return _history[_currentIndex + 1];
            }
            return null;
        }
    }

    public string? PeekBack()
    {
        lock (_lock)
        {
            if (_currentIndex > 0)
            {
                return _history[_currentIndex - 1];
            }
            return null;
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _history.Clear();
            _currentIndex = -1;
        }
    }
}
