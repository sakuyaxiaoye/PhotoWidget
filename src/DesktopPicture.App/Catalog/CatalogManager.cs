using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using DesktopPicture.Logging;
using DesktopPicture.Storage;

namespace DesktopPicture.Catalog;

public sealed class CatalogManager : IDisposable
{
    private static readonly Lazy<CatalogManager> _instance = new(() => new CatalogManager());
    public static CatalogManager Instance => _instance.Value;

    private readonly ConcurrentDictionary<string, RootCatalogContext> _contexts = new(StringComparer.OrdinalIgnoreCase);
    private readonly SqliteCatalogDatabase _db;
    private readonly Timer _periodicReconciliationTimer;

    public CatalogManager(SqliteCatalogDatabase? db = null)
    {
        _db = db ?? SqliteCatalogDatabase.Instance;

        // Periodic 30-minute idle reconciliation per SPEC 7.2
        _periodicReconciliationTimer = new Timer(
            _ => RunPeriodicReconciliation(),
            null,
            TimeSpan.FromMinutes(30),
            TimeSpan.FromMinutes(30));
    }

    public RootCatalogContext GetOrCreateContext(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            throw new ArgumentException("Root path cannot be empty.", nameof(rootPath));
        }

        string canonical = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        var context = _contexts.GetOrAdd(canonical, path =>
        {
            AppLogger.Instance.Info($"CatalogManager: Creating shared RootCatalogContext for '{path}'");
            return new RootCatalogContext(path, _db);
        });

        context.AddReference();
        return context;
    }

    public void ReleaseContext(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath)) return;

        string canonical = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (_contexts.TryGetValue(canonical, out var context))
        {
            if (context.ReleaseReference() <= 0)
            {
                if (_contexts.TryRemove(canonical, out var removed))
                {
                    AppLogger.Instance.Info($"CatalogManager: Releasing unused RootCatalogContext for '{canonical}'");
                    removed.Dispose();
                }
            }
        }
    }

    private void RunPeriodicReconciliation()
    {
        AppLogger.Instance.Info("CatalogManager: Starting scheduled 30-minute periodic reconciliation.");
        foreach (var ctx in _contexts.Values)
        {
            _ = ctx.TriggerReconciliationAsync();
        }
    }

    public void Dispose()
    {
        _periodicReconciliationTimer.Dispose();
        foreach (var ctx in _contexts.Values)
        {
            ctx.Dispose();
        }
        _contexts.Clear();
    }
}
