using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using DesktopPicture.Catalog;
using DesktopPicture.Random;
using DesktopPicture.Storage;
using Xunit;
using Xunit.Abstractions;

namespace DesktopPicture.Tests;

public class LargeCatalogBenchmarkTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _dbPath;
    private readonly SqliteCatalogDatabase _db;

    public LargeCatalogBenchmarkTests(ITestOutputHelper output)
    {
        _output = output;
        _dbPath = Path.Combine(Path.GetTempPath(), $"BenchmarkCatalog_{Guid.NewGuid():N}.db");
        _db = new SqliteCatalogDatabase(_dbPath);
    }

    [Fact]
    public void Benchmark_LargeCatalog_50k_BatchInsert_And_FastSampling()
    {
        const int totalItems = 50_000;
        var root = _db.GetOrCreateRoot("C:\\MassivePhotos");

        var sw = Stopwatch.StartNew();

        // 1. Batch Insert
        var batch = new List<ImageUpsertEntry>(500);
        for (int i = 1; i <= totalItems; i++)
        {
            batch.Add(new ImageUpsertEntry($"category_{i % 100}/image_{i}.jpg", ImageExtensionType.Jpg, 1024 * (i % 50), DateTime.UtcNow.Ticks));
            if (batch.Count >= 500)
            {
                _db.UpsertImagesBatch(root.Id, batch, scanVersion: 1);
                batch.Clear();
            }
        }
        if (batch.Count > 0)
        {
            _db.UpsertImagesBatch(root.Id, batch, scanVersion: 1);
        }

        sw.Stop();
        _output.WriteLine($"Batch Insert {totalItems} items: {sw.ElapsedMilliseconds} ms ({totalItems * 1000.0 / sw.ElapsedMilliseconds:F0} ops/sec)");

        // 2. Snapshot Load
        sw.Restart();
        var healthyIds = _db.GetHealthyImageIds(root.Id);
        var snapshot = new CompactIdSnapshot(healthyIds);
        sw.Stop();

        Assert.Equal(totalItems, snapshot.Count);
        _output.WriteLine($"Loaded CompactIdSnapshot ({snapshot.Count} IDs): {sw.ElapsedMilliseconds} ms");

        // 3. 10,000 Random Selection Benchmark
        var selector = new RandomSelector();
        int? currentLastId = null;

        sw.Restart();
        for (int i = 0; i < 10_000; i++)
        {
            var nextId = selector.SelectNextId(snapshot, currentLastId);
            Assert.NotNull(nextId);
            if (currentLastId.HasValue)
            {
                Assert.NotEqual(currentLastId.Value, nextId.Value);
            }
            currentLastId = nextId;
        }
        sw.Stop();

        _output.WriteLine($"10,000 Random Picks (with adjacent non-repeating check): {sw.ElapsedMilliseconds} ms ({10_000.0 * 1000 / sw.ElapsedMilliseconds:F0} picks/sec)");
        Assert.True(sw.ElapsedMilliseconds < 500, "10,000 picks took too long!");
    }

    public void Dispose()
    {
        _db.Dispose();
        try { File.Delete(_dbPath); } catch { }
        try { File.Delete(_dbPath + "-wal"); } catch { }
        try { File.Delete(_dbPath + "-shm"); } catch { }
    }
}
