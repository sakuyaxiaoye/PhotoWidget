using System;
using System.IO;
using DesktopPicture.Storage;
using Xunit;

namespace DesktopPicture.Tests;

public class SqliteCatalogDatabaseTests : IDisposable
{
    private readonly string _tempDbPath;
    private readonly SqliteCatalogDatabase _db;

    public SqliteCatalogDatabaseTests()
    {
        _tempDbPath = Path.Combine(Path.GetTempPath(), $"test_catalog_{Guid.NewGuid():N}.db");
        _db = new SqliteCatalogDatabase(_tempDbPath);
    }

    [Fact]
    public void Test_Root_Creation_And_Idempotency()
    {
        var root1 = _db.GetOrCreateRoot("C:\\Pictures");
        var root2 = _db.GetOrCreateRoot("C:\\Pictures");

        Assert.Equal(root1.Id, root2.Id);
        Assert.Equal("C:\\Pictures", root1.CanonicalPath);
        Assert.Equal(RootHealthState.Healthy, root1.Health);
    }

    [Fact]
    public void Test_Images_Upsert_And_Query()
    {
        var root = _db.GetOrCreateRoot("D:\\Photos");
        var entries = new[]
        {
            new ImageUpsertEntry("sub/a.jpg", ImageExtensionType.Jpg, 1024, DateTime.UtcNow.Ticks),
            new ImageUpsertEntry("sub/b.png", ImageExtensionType.Png, 2048, DateTime.UtcNow.Ticks),
            new ImageUpsertEntry("c.webp", ImageExtensionType.Webp, 4096, DateTime.UtcNow.Ticks)
        };

        _db.UpsertImagesBatch(root.Id, entries, scanVersion: 1);

        var ids = _db.GetHealthyImageIds(root.Id);
        Assert.Equal(3, ids.Length);

        var pathA = _db.GetRelativePath(ids[0]);
        Assert.NotNull(pathA);
        Assert.Contains(".", pathA);
    }

    [Fact]
    public void Test_MarkUnseen_DeactivatesDeletedFiles()
    {
        var root = _db.GetOrCreateRoot("E:\\Wallpapers");
        var v1Entries = new[]
        {
            new ImageUpsertEntry("keep.jpg", ImageExtensionType.Jpg, 100, DateTime.UtcNow.Ticks),
            new ImageUpsertEntry("delete_me.png", ImageExtensionType.Png, 200, DateTime.UtcNow.Ticks)
        };

        _db.UpsertImagesBatch(root.Id, v1Entries, scanVersion: 1);
        Assert.Equal(2, _db.GetHealthyImageIds(root.Id).Length);

        // Version 2 scan only found "keep.jpg"
        var v2Entries = new[]
        {
            new ImageUpsertEntry("keep.jpg", ImageExtensionType.Jpg, 100, DateTime.UtcNow.Ticks)
        };
        _db.UpsertImagesBatch(root.Id, v2Entries, scanVersion: 2);
        _db.MarkUnseenImagesInvalid(root.Id, scanVersion: 2);

        var healthyAfterV2 = _db.GetHealthyImageIds(root.Id);
        Assert.Single(healthyAfterV2);
        Assert.Equal("keep.jpg", _db.GetRelativePath(healthyAfterV2[0]));
    }

    public void Dispose()
    {
        _db.Dispose();
        try
        {
            if (File.Exists(_tempDbPath)) File.Delete(_tempDbPath);
            var wal = _tempDbPath + "-wal";
            var shm = _tempDbPath + "-shm";
            if (File.Exists(wal)) File.Delete(wal);
            if (File.Exists(shm)) File.Delete(shm);
        }
        catch { }
    }
}
