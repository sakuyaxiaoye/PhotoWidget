using System;
using System.Collections.Concurrent;
using System.IO;

namespace DesktopPicture.Random;

public sealed class ErrorBackoffTracker
{
    private sealed record FailureEntry(
        int FailureCount,
        DateTime RetryAfterUtc,
        long FileSize,
        DateTime LastWriteTimeUtc);

    private readonly ConcurrentDictionary<string, FailureEntry> _failures = new(StringComparer.OrdinalIgnoreCase);

    public bool IsUnderBackoff(string path)
    {
        if (!_failures.TryGetValue(path, out var entry))
        {
            return false;
        }

        try
        {
            if (File.Exists(path))
            {
                var fi = new FileInfo(path);
                // Clear backoff if file modified or size changed per SPEC 11
                if (fi.Length != entry.FileSize || fi.LastWriteTimeUtc != entry.LastWriteTimeUtc)
                {
                    _failures.TryRemove(path, out _);
                    return false;
                }
            }
        }
        catch { }

        return DateTime.UtcNow < entry.RetryAfterUtc;
    }

    public void RecordFailure(string path)
    {
        int prevCount = 0;
        if (_failures.TryGetValue(path, out var existing))
        {
            prevCount = existing.FailureCount;
        }

        int newCount = prevCount + 1;
        TimeSpan backoffDuration = newCount switch
        {
            1 => TimeSpan.FromMinutes(10),
            2 => TimeSpan.FromHours(1),
            _ => TimeSpan.FromHours(24)
        };

        long size = 0;
        DateTime writeTime = DateTime.MinValue;

        try
        {
            if (File.Exists(path))
            {
                var fi = new FileInfo(path);
                size = fi.Length;
                writeTime = fi.LastWriteTimeUtc;
            }
        }
        catch { }

        _failures[path] = new FailureEntry(
            newCount,
            DateTime.UtcNow.Add(backoffDuration),
            size,
            writeTime);
    }

    public void ClearFailure(string path)
    {
        _failures.TryRemove(path, out _);
    }

    public void ClearAll()
    {
        _failures.Clear();
    }
}
