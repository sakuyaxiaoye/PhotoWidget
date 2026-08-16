using System;
using System.Linq;
using DesktopPicture.Catalog;
using Xunit;

namespace DesktopPicture.Tests;

public class CompactIdSnapshotTests
{
    [Fact]
    public void Test_300k_CompactSnapshot_Memory_And_Lookup()
    {
        const int count = 300_000;
        var ids = Enumerable.Range(1, count).ToArray();

        var snapshot = new CompactIdSnapshot(ids);
        Assert.Equal(count, snapshot.Count);

        // Memory validation: 300,000 * 4 bytes = 1,200,000 bytes (~1.14 MB)
        long estimatedBytes = (long)snapshot.Count * sizeof(int);
        Assert.True(estimatedBytes <= 1_500_000, "Compact ID memory exceeds 1.5 MB");

        // Lookup validation
        Assert.Equal(1, snapshot.GetIdAt(0));
        Assert.Equal(300_000, snapshot.GetIdAt(count - 1));

        int idx = snapshot.FindIndex(150_000);
        Assert.Equal(149_999, idx);
    }

    [Fact]
    public void Test_HotPathCache_Lru_Eviction()
    {
        var cache = new HotPathCache(capacity: 3);

        cache.Put(1, "path1.jpg");
        cache.Put(2, "path2.jpg");
        cache.Put(3, "path3.jpg");

        Assert.True(cache.TryGet(1, out _)); // access 1, making 2 the oldest
        cache.Put(4, "path4.jpg"); // evicts 2

        Assert.True(cache.TryGet(1, out _));
        Assert.False(cache.TryGet(2, out _)); // evicted
        Assert.True(cache.TryGet(3, out _));
        Assert.True(cache.TryGet(4, out _));
    }
}
