using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DesktopPicture.Catalog;
using DesktopPicture.Config;
using DesktopPicture.Decoding;
using DesktopPicture.Random;
using DesktopPicture.Scanning;
using DesktopPicture.Storage;
using Xunit;

namespace DesktopPicture.Tests;

public class MultiWidgetConcurrencyTests : IDisposable
{
    private readonly string _testRoot;
    private readonly string _dbPath;
    private readonly SqliteCatalogDatabase _db;

    public MultiWidgetConcurrencyTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), $"MultiWidgetTest_{Guid.NewGuid():N}");
        _dbPath = Path.Combine(Path.GetTempPath(), $"MultiWidgetDb_{Guid.NewGuid():N}.db");
        Directory.CreateDirectory(_testRoot);

        _db = new SqliteCatalogDatabase(_dbPath);

        // Create 20 test dummy images
        for (int i = 1; i <= 20; i++)
        {
            File.WriteAllText(Path.Combine(_testRoot, $"img_{i}.png"), $"data_{i}");
        }

        var root = _db.GetOrCreateRoot(_testRoot);
        var entries = Enumerable.Range(1, 20)
            .Select(i => new ImageUpsertEntry($"img_{i}.png", ImageExtensionType.Png, 100, DateTime.UtcNow.Ticks))
            .ToList();
        _db.UpsertImagesBatch(root.Id, entries, 1);
    }

    [Fact]
    public async Task Test_4_Widgets_Concurrent_RandomSelection()
    {
        var context = new RootCatalogContext(_testRoot, _db);
        var snapshot = context.CurrentSnapshot;
        Assert.Equal(20, snapshot.Count);

        var randomSelectors = Enumerable.Range(0, 4).Select(_ => new RandomSelector()).ToArray();
        var selectedPerWidget = new List<int>[4];
        for (int w = 0; w < 4; w++) selectedPerWidget[w] = new List<int>();

        var tasks = new Task[4];
        for (int w = 0; w < 4; w++)
        {
            int widgetIndex = w;
            tasks[w] = Task.Run(() =>
            {
                int? lastId = null;
                for (int iter = 0; iter < 500; iter++)
                {
                    var nextId = randomSelectors[widgetIndex].SelectNextId(snapshot, lastId);
                    Assert.NotNull(nextId);
                    if (lastId.HasValue)
                    {
                        Assert.NotEqual(lastId.Value, nextId.Value);
                    }
                    selectedPerWidget[widgetIndex].Add(nextId.Value);
                    lastId = nextId;
                }
            });
        }

        await Task.WhenAll(tasks);

        for (int w = 0; w < 4; w++)
        {
            Assert.Equal(500, selectedPerWidget[w].Count);
        }

        context.Dispose();
    }

    public void Dispose()
    {
        _db.Dispose();
        try { Directory.Delete(_testRoot, recursive: true); } catch { }
        try { File.Delete(_dbPath); } catch { }
    }
}
