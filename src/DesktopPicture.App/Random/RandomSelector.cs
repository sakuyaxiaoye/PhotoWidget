using System;
using System.Collections.Generic;
using System.IO;
using DesktopPicture.Logging;

namespace DesktopPicture.Random;

public sealed class RandomSelector
{
    private readonly System.Random _random;
    private readonly ErrorBackoffTracker _backoffTracker;

    public const int MaxRetryAttempts = 32;

    public ErrorBackoffTracker BackoffTracker => _backoffTracker;

    public RandomSelector(ErrorBackoffTracker? backoffTracker = null, int? seed = null)
    {
        _random = seed.HasValue ? new System.Random(seed.Value) : System.Random.Shared;
        _backoffTracker = backoffTracker ?? new ErrorBackoffTracker();
    }

    public string? SelectNext(IReadOnlyList<string> candidates, string? lastShownPath)
    {
        int n = candidates.Count;
        if (n == 0) return null;
        if (n == 1) return candidates[0];

        // Find last shown index
        int lastIndex = -1;
        if (!string.IsNullOrEmpty(lastShownPath))
        {
            for (int i = 0; i < n; i++)
            {
                if (string.Equals(candidates[i], lastShownPath, StringComparison.OrdinalIgnoreCase))
                {
                    lastIndex = i;
                    break;
                }
            }
        }

        int selectedIndex;
        if (lastIndex >= 0)
        {
            // Pick from [0, n - 2]
            int r = _random.Next(0, n - 1);
            selectedIndex = (r >= lastIndex) ? r + 1 : r;
        }
        else
        {
            selectedIndex = _random.Next(0, n);
        }

        return candidates[selectedIndex];
    }

    public string? SelectValidCandidate(IReadOnlyList<string> candidates, string? lastShownPath, HashSet<string>? excludedInThisSwitch = null)
    {
        int n = candidates.Count;
        if (n == 0) return null;

        var excluded = excludedInThisSwitch ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? currentLast = lastShownPath;

        for (int attempt = 0; attempt < MaxRetryAttempts; attempt++)
        {
            var candidate = SelectNext(candidates, currentLast);
            if (candidate == null) break;

            if (excluded.Contains(candidate))
            {
                currentLast = candidate;
                continue;
            }

            if (_backoffTracker.IsUnderBackoff(candidate))
            {
                excluded.Add(candidate);
                currentLast = candidate;
                continue;
            }

            if (!File.Exists(candidate))
            {
                _backoffTracker.RecordFailure(candidate);
                excluded.Add(candidate);
                currentLast = candidate;
                continue;
            }

            return candidate;
        }

        AppLogger.Instance.Warn($"RandomSelector: Exceeded {MaxRetryAttempts} retry attempts. No healthy candidate selected.");
        return null;
    }

    public int? SelectNextId(Catalog.CompactIdSnapshot snapshot, int? lastShownId)
    {
        int n = snapshot.Count;
        if (n == 0) return null;
        if (n == 1) return snapshot.GetIdAt(0);

        int lastIndex = -1;
        if (lastShownId.HasValue)
        {
            lastIndex = snapshot.FindIndex(lastShownId.Value);
        }

        int selectedIndex;
        if (lastIndex >= 0)
        {
            int r = _random.Next(0, n - 1);
            selectedIndex = (r >= lastIndex) ? r + 1 : r;
        }
        else
        {
            selectedIndex = _random.Next(0, n);
        }

        return snapshot.GetIdAt(selectedIndex);
    }

    public (int Id, string FullPath)? SelectValidCandidateId(
        Catalog.RootCatalogContext context,
        int? lastShownId,
        HashSet<int>? excludedInThisSwitch = null)
    {
        var snapshot = context.CurrentSnapshot;
        int n = snapshot.Count;
        if (n == 0) return null;

        var excluded = excludedInThisSwitch ?? new HashSet<int>();
        int? currentLast = lastShownId;

        for (int attempt = 0; attempt < MaxRetryAttempts; attempt++)
        {
            var candidateId = SelectNextId(snapshot, currentLast);
            if (!candidateId.HasValue) break;

            int id = candidateId.Value;
            if (excluded.Contains(id))
            {
                currentLast = id;
                continue;
            }

            var fullPath = context.GetFullPath(id);
            if (string.IsNullOrEmpty(fullPath))
            {
                excluded.Add(id);
                currentLast = id;
                continue;
            }

            if (_backoffTracker.IsUnderBackoff(fullPath))
            {
                excluded.Add(id);
                currentLast = id;
                continue;
            }

            if (!File.Exists(fullPath))
            {
                _backoffTracker.RecordFailure(fullPath);
                excluded.Add(id);
                currentLast = id;
                continue;
            }

            return (id, fullPath);
        }

        return null;
    }
}
