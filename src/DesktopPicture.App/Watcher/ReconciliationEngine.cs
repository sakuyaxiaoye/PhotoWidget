using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DesktopPicture.Catalog;
using DesktopPicture.Logging;
using DesktopPicture.Storage;

namespace DesktopPicture.Watcher;

public sealed class ReconciliationEngine
{
    private const int ImageBatchSize = 10000;
    private const int DirectoryBatchSize = 2000;

    public static async Task<CompactIdSnapshot> ReconcileAsync(
        long rootId,
        string canonicalRoot,
        long scanVersion,
        SqliteCatalogDatabase db,
        Action<int>? onProgress = null,
        Action<CompactIdSnapshot>? onIncrementalSnapshot = null,
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            Thread.CurrentThread.Priority = ThreadPriority.Lowest;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            AppLogger.Instance.Info($"ReconciliationEngine: Starting ultra-fast HDD sequential scan for Root {rootId} ('{canonicalRoot}') version {scanVersion}");

            if (!Directory.Exists(canonicalRoot))
            {
                db.UpdateRootHealth(rootId, RootHealthState.Unavailable, scanVersion);
                return CompactIdSnapshot.Empty;
            }

            var imageBatch = new List<ImageUpsertEntry>(ImageBatchSize);
            var dirBatch = new List<DiscoveredDirectory>(DirectoryBatchSize);
            var knownDirs = db.GetKnownDirectoryTimestamps(rootId);

            int totalFound = 0;
            bool firstProgressiveUpdateDone = false;

            try
            {
                FastDirectoryEnumerator.EnumerateImagesAndDirectories(
                    canonicalRoot,
                    onImageDiscovered: entry =>
                    {
                        imageBatch.Add(entry);
                        totalFound++;

                        if (imageBatch.Count >= ImageBatchSize)
                        {
                            db.UpsertImagesBatch(rootId, imageBatch, scanVersion);
                            imageBatch.Clear();
                            onProgress?.Invoke(totalFound);

                            // Progressive initial playback trigger
                            if (!firstProgressiveUpdateDone && onIncrementalSnapshot != null && totalFound >= 50)
                            {
                                firstProgressiveUpdateDone = true;
                                int[] partialIds = db.GetHealthyImageIds(rootId);
                                if (partialIds.Length > 0)
                                {
                                    onIncrementalSnapshot(new CompactIdSnapshot(partialIds));
                                }
                            }
                        }
                    },
                    onDirectoryDiscovered: dir =>
                    {
                        dirBatch.Add(dir);
                        if (dirBatch.Count >= DirectoryBatchSize)
                        {
                            db.UpsertDirectoriesBatch(rootId, dirBatch);
                            dirBatch.Clear();
                        }
                    },
                    shouldSkipDirectory: null, // Full versioned crawl with LARGE_FETCH ensures complete correctness
                    ct: cancellationToken);

                // Commit remaining image entries
                if (imageBatch.Count > 0)
                {
                    db.UpsertImagesBatch(rootId, imageBatch, scanVersion);
                    imageBatch.Clear();
                }

                // Commit remaining directory entries
                if (dirBatch.Count > 0)
                {
                    db.UpsertDirectoriesBatch(rootId, dirBatch);
                    dirBatch.Clear();
                }

                // Mark unseen images invalid in database
                db.MarkUnseenImagesInvalid(rootId, scanVersion);

                // Update root record status to Healthy
                db.UpdateRootHealth(rootId, RootHealthState.Healthy, scanVersion, DateTime.UtcNow);

                // Perform automated WAL truncation and index optimization
                db.OptimizeDatabase();

                // Fetch compact healthy IDs
                int[] healthyIds = db.GetHealthyImageIds(rootId);
                var snapshot = new CompactIdSnapshot(healthyIds);

                sw.Stop();
                AppLogger.Instance.Info($"ReconciliationEngine: Completed for Root {rootId}. Discovered {totalFound} images, active snapshot count: {snapshot.Count} in {sw.ElapsedMilliseconds} ms.");

                // Reclaim all temporary crawl memory pages back to OS
                DesktopPicture.Diagnostics.MemoryOptimizer.TrimWorkingSet();

                return snapshot;
            }
            catch (OperationCanceledException)
            {
                AppLogger.Instance.Info($"ReconciliationEngine: Scan cancelled for Root {rootId}.");
                return CompactIdSnapshot.Empty;
            }
            catch (Exception ex)
            {
                AppLogger.Instance.Error($"ReconciliationEngine: Error during scan for Root {rootId}", ex);
                db.UpdateRootHealth(rootId, RootHealthState.Untrusted, scanVersion);
                return CompactIdSnapshot.Empty;
            }
        }, cancellationToken);
    }
}
