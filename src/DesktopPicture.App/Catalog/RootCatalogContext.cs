using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DesktopPicture.Logging;
using DesktopPicture.Storage;
using DesktopPicture.Watcher;

namespace DesktopPicture.Catalog;

public sealed class RootCatalogContext : IDisposable
{
    private readonly SqliteCatalogDatabase _db;
    private RootRecord _rootRecord;
    private readonly string _canonicalRoot;
    private CompactIdSnapshot _currentSnapshot = CompactIdSnapshot.Empty;
    private readonly HotPathCache _hotPathCache = new(1024);
    private readonly DirectoryWatcher _watcher;
    private int _referenceCount = 0;
    private bool _isReconciling = false;
    private readonly object _lock = new();

    public RootRecord RootRecord => _rootRecord;
    public string CanonicalRoot => _canonicalRoot;
    public CompactIdSnapshot CurrentSnapshot => _currentSnapshot;
    public int ReferenceCount => _referenceCount;

    public event Action<CompactIdSnapshot>? OnSnapshotUpdated;

    public RootCatalogContext(string rootPath, SqliteCatalogDatabase? db = null)
    {
        _canonicalRoot = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        _db = db ?? SqliteCatalogDatabase.Instance;

        _rootRecord = _db.GetOrCreateRoot(_canonicalRoot);
        _watcher = new DirectoryWatcher(_canonicalRoot);

        _watcher.OnEventsBatchProcessed += HandleWatcherEventsBatch;
        _watcher.OnWatcherOverflow += HandleWatcherOverflow;

        // Load initial snapshot from DB
        int[] existingIds = _db.GetHealthyImageIds(_rootRecord.Id);
        _currentSnapshot = new CompactIdSnapshot(existingIds);
    }

    public void AddReference()
    {
        Interlocked.Increment(ref _referenceCount);
    }

    public int ReleaseReference()
    {
        return Interlocked.Decrement(ref _referenceCount);
    }

    public string? GetFullPath(int imageId)
    {
        if (_hotPathCache.TryGet(imageId, out var cachedRelPath))
        {
            return Path.Combine(_canonicalRoot, cachedRelPath);
        }

        var relPath = _db.GetRelativePath(imageId);
        if (!string.IsNullOrEmpty(relPath))
        {
            _hotPathCache.Put(imageId, relPath);
            return Path.Combine(_canonicalRoot, relPath);
        }

        return null;
    }

    public async Task TriggerReconciliationAsync(Action<int>? onProgress = null, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (_isReconciling) return;
            _isReconciling = true;
        }

        try
        {
            long nextVersion = _rootRecord.ScanVersion + 1;
            var newSnapshot = await ReconciliationEngine.ReconcileAsync(
                _rootRecord.Id,
                _canonicalRoot,
                nextVersion,
                _db,
                onProgress,
                onIncrementalSnapshot: partialSnap =>
                {
                    _currentSnapshot = partialSnap;
                    OnSnapshotUpdated?.Invoke(partialSnap);
                },
                ct);

            _currentSnapshot = newSnapshot;
            _rootRecord = _db.GetOrCreateRoot(_canonicalRoot);
            _hotPathCache.Clear();

            OnSnapshotUpdated?.Invoke(_currentSnapshot);
        }
        finally
        {
            lock (_lock)
            {
                _isReconciling = false;
            }
        }
    }

    private void HandleWatcherEventsBatch(IReadOnlyList<WatcherEvent> events)
    {
        try
        {
            var upserts = new List<ImageUpsertEntry>();
            foreach (var evt in events)
            {
                if (evt.Kind == WatcherChangeKind.Upsert)
                {
                    var ext = Path.GetExtension(evt.RelativePath).ToLowerInvariant() switch
                    {
                        ".jpg" => ImageExtensionType.Jpg,
                        ".jpeg" => ImageExtensionType.Jpeg,
                        ".png" => ImageExtensionType.Png,
                        ".webp" => ImageExtensionType.Webp,
                        ".gif" => ImageExtensionType.Gif,
                        _ => ImageExtensionType.Unknown
                    };

                    upserts.Add(new ImageUpsertEntry(evt.RelativePath, ext, evt.Length, evt.LastWriteUtcTicks));
                }
                else if (evt.Kind == WatcherChangeKind.Delete)
                {
                    _db.DeleteImage(_rootRecord.Id, evt.RelativePath);
                }
            }

            if (upserts.Count > 0)
            {
                _db.UpsertImagesBatch(_rootRecord.Id, upserts, _rootRecord.ScanVersion);
            }

            // Reload snapshot
            int[] healthyIds = _db.GetHealthyImageIds(_rootRecord.Id);
            _currentSnapshot = new CompactIdSnapshot(healthyIds);
            _hotPathCache.Clear();

            AppLogger.Instance.Info($"RootCatalogContext: Processed {events.Count} watcher events for '{_canonicalRoot}'. Active candidate count: {_currentSnapshot.Count}");
            OnSnapshotUpdated?.Invoke(_currentSnapshot);
        }
        catch (Exception ex)
        {
            AppLogger.Instance.Error($"RootCatalogContext: Error processing watcher events", ex);
        }
    }

    private void HandleWatcherOverflow()
    {
        _db.UpdateRootHealth(_rootRecord.Id, RootHealthState.Untrusted);
        _ = TriggerReconciliationAsync();
    }

    public void Dispose()
    {
        _watcher.OnEventsBatchProcessed -= HandleWatcherEventsBatch;
        _watcher.OnWatcherOverflow -= HandleWatcherOverflow;
        _watcher.Dispose();
    }
}
