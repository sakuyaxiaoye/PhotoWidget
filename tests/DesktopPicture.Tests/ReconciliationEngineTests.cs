using System;
using System.IO;
using System.Threading.Tasks;
using DesktopPicture.Storage;
using DesktopPicture.Watcher;
using Xunit;

namespace DesktopPicture.Tests;

public class ReconciliationEngineTests : IDisposable
{
    private readonly string _testDir;
    private readonly string _dbPath;
    private readonly SqliteCatalogDatabase _db;

    public ReconciliationEngineTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"ReconTest_Dir_{Guid.NewGuid():N}");
        _dbPath = Path.Combine(Path.GetTempPath(), $"ReconTest_Db_{Guid.NewGuid():N}.db");
        Directory.CreateDirectory(_testDir);

        _db = new SqliteCatalogDatabase(_dbPath);

        // Seed 4 test files
        File.WriteAllText(Path.Combine(_testDir, "pic1.jpg"), "1");
        File.WriteAllText(Path.Combine(_testDir, "pic2.png"), "2");
        Directory.CreateDirectory(Path.Combine(_testDir, "sub"));
        File.WriteAllText(Path.Combine(_testDir, "sub", "pic3.webp"), "3");
        File.WriteAllText(Path.Combine(_testDir, "sub", "pic4.gif"), "4");
    }

    [Fact]
    public async Task Test_Reconcile_Discovers_And_Updates_Snapshot()
    {
        var root = _db.GetOrCreateRoot(_testDir);

        var snapshot1 = await ReconciliationEngine.ReconcileAsync(root.Id, _testDir, scanVersion: 1, _db);
        Assert.Equal(4, snapshot1.Count);

        // Add 1 file and delete 1 file
        File.WriteAllText(Path.Combine(_testDir, "pic5.jpg"), "5");
        File.Delete(Path.Combine(_testDir, "pic1.jpg"));

        var snapshot2 = await ReconciliationEngine.ReconcileAsync(root.Id, _testDir, scanVersion: 2, _db);
        Assert.Equal(4, snapshot2.Count); // 4 - 1 + 1 = 4
    }

    public void Dispose()
    {
        _db.Dispose();
        try { Directory.Delete(_testDir, recursive: true); } catch { }
        try { File.Delete(_dbPath); } catch { }
    }
}
