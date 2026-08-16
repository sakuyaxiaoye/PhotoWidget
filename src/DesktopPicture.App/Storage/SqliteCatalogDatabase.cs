using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Threading;
using DesktopPicture.Logging;
using DesktopPicture.Watcher;
using Microsoft.Data.Sqlite;

namespace DesktopPicture.Storage;

public sealed record ImageUpsertEntry(
    string RelativePath,
    ImageExtensionType Extension,
    long Length,
    long LastWriteUtcTicks);

public sealed class SqliteCatalogDatabase : IDisposable
{
    private static readonly Lazy<SqliteCatalogDatabase> _instance = new(() => new SqliteCatalogDatabase());
    public static SqliteCatalogDatabase Instance => _instance.Value;

    private readonly string _dbPath;
    private readonly string _connectionString;
    private readonly object _dbLock = new();

    public SqliteCatalogDatabase(string? customDbPath = null)
    {
        var dbDir = customDbPath != null ? Path.GetDirectoryName(customDbPath)! : Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DesktopPicture");

        Directory.CreateDirectory(dbDir);
        _dbPath = customDbPath ?? Path.Combine(dbDir, "catalog.db");
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();

        InitializeDatabase();
    }

    private SqliteConnection CreateConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();

        // Register custom WIN_ORDINAL_NOCASE collation per SPEC 6.2
        connection.CreateCollation("WIN_ORDINAL_NOCASE", (s1, s2) => string.Compare(s1, s2, StringComparison.OrdinalIgnoreCase));

        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
                PRAGMA foreign_keys = ON;
                PRAGMA journal_mode = WAL;
                PRAGMA synchronous = NORMAL;
                PRAGMA mmap_size = 268435456;
                PRAGMA cache_size = -64000;
                PRAGMA temp_store = MEMORY;
            ";
            cmd.ExecuteNonQuery();
        }

        return connection;
    }

    private void InitializeDatabase()
    {
        lock (_dbLock)
        {
            try
            {
                using var conn = CreateConnection();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS roots (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        canonical_path TEXT NOT NULL UNIQUE,
                        scan_version INTEGER NOT NULL,
                        last_full_scan_utc TEXT,
                        health INTEGER NOT NULL
                    );

                    CREATE TABLE IF NOT EXISTS images (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        root_id INTEGER NOT NULL,
                        relative_path TEXT COLLATE WIN_ORDINAL_NOCASE NOT NULL,
                        extension INTEGER NOT NULL,
                        length INTEGER NOT NULL,
                        last_write_utc_ticks INTEGER NOT NULL,
                        state INTEGER NOT NULL,
                        retry_after_utc_ticks INTEGER,
                        seen_scan_version INTEGER NOT NULL,
                        UNIQUE(root_id, relative_path),
                        FOREIGN KEY(root_id) REFERENCES roots(id) ON DELETE CASCADE
                    );

                    CREATE TABLE IF NOT EXISTS directories (
                        root_id INTEGER NOT NULL,
                        relative_path TEXT COLLATE WIN_ORDINAL_NOCASE NOT NULL,
                        last_write_utc_ticks INTEGER NOT NULL,
                        PRIMARY KEY (root_id, relative_path),
                        FOREIGN KEY(root_id) REFERENCES roots(id) ON DELETE CASCADE
                    );

                    CREATE INDEX IF NOT EXISTS idx_images_root_state ON images(root_id, state);
                ";
                cmd.ExecuteNonQuery();
                AppLogger.Instance.Info($"SqliteCatalogDatabase: Schema verified at {_dbPath}");
            }
            catch (Exception ex)
            {
                AppLogger.Instance.Error("SqliteCatalogDatabase: Failed to initialize schema", ex);
                throw;
            }
        }
    }

    public RootRecord GetOrCreateRoot(string canonicalPath)
    {
        lock (_dbLock)
        {
            using var conn = CreateConnection();

            using (var selectCmd = conn.CreateCommand())
            {
                selectCmd.CommandText = "SELECT id, canonical_path, scan_version, last_full_scan_utc, health FROM roots WHERE canonical_path = @path;";
                selectCmd.Parameters.AddWithValue("@path", canonicalPath);

                using var reader = selectCmd.ExecuteReader();
                if (reader.Read())
                {
                    var id = reader.GetInt64(0);
                    var path = reader.GetString(1);
                    var scanVer = reader.GetInt64(2);
                    DateTime? lastScan = reader.IsDBNull(3) ? null : DateTime.Parse(reader.GetString(3));
                    var health = (RootHealthState)reader.GetInt32(4);
                    return new RootRecord(id, path, scanVer, lastScan, health);
                }
            }

            using (var insertCmd = conn.CreateCommand())
            {
                insertCmd.CommandText = @"
                    INSERT INTO roots (canonical_path, scan_version, last_full_scan_utc, health)
                    VALUES (@path, 1, NULL, @health);
                    SELECT last_insert_rowid();
                ";
                insertCmd.Parameters.AddWithValue("@path", canonicalPath);
                insertCmd.Parameters.AddWithValue("@health", (int)RootHealthState.Healthy);

                long newId = (long)insertCmd.ExecuteScalar()!;
                return new RootRecord(newId, canonicalPath, 1, null, RootHealthState.Healthy);
            }
        }
    }

    public void UpdateRootHealth(long rootId, RootHealthState health, long? newScanVersion = null, DateTime? lastScanUtc = null)
    {
        lock (_dbLock)
        {
            using var conn = CreateConnection();
            using var cmd = conn.CreateCommand();

            cmd.CommandText = @"
                UPDATE roots
                SET health = @health,
                    scan_version = COALESCE(@scanVer, scan_version),
                    last_full_scan_utc = COALESCE(@lastScan, last_full_scan_utc)
                WHERE id = @id;
            ";
            cmd.Parameters.AddWithValue("@id", rootId);
            cmd.Parameters.AddWithValue("@health", (int)health);
            cmd.Parameters.AddWithValue("@scanVer", newScanVersion.HasValue ? newScanVersion.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@lastScan", lastScanUtc.HasValue ? lastScanUtc.Value.ToString("O") : DBNull.Value);

            cmd.ExecuteNonQuery();
        }
    }

    public Dictionary<string, long> GetKnownDirectoryTimestamps(long rootId)
    {
        lock (_dbLock)
        {
            var result = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            using var conn = CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT relative_path, last_write_utc_ticks FROM directories WHERE root_id = @rootId;";
            cmd.Parameters.AddWithValue("@rootId", rootId);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                result[reader.GetString(0)] = reader.GetInt64(1);
            }
            return result;
        }
    }

    public void UpsertDirectoriesBatch(long rootId, IReadOnlyList<DiscoveredDirectory> directories)
    {
        if (directories.Count == 0) return;
        lock (_dbLock)
        {
            using var conn = CreateConnection();
            using var tx = conn.BeginTransaction();
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
                INSERT INTO directories (root_id, relative_path, last_write_utc_ticks)
                VALUES (@rootId, @relPath, @lastWrite)
                ON CONFLICT(root_id, relative_path) DO UPDATE SET
                    last_write_utc_ticks = excluded.last_write_utc_ticks;
            ";

            var pRootId = cmd.Parameters.Add("@rootId", SqliteType.Integer);
            var pRelPath = cmd.Parameters.Add("@relPath", SqliteType.Text);
            var pLastWrite = cmd.Parameters.Add("@lastWrite", SqliteType.Integer);

            pRootId.Value = rootId;

            foreach (var dir in directories)
            {
                pRelPath.Value = dir.RelativePath;
                pLastWrite.Value = dir.LastWriteUtcTicks;
                cmd.ExecuteNonQuery();
            }

            tx.Commit();
        }
    }

    public int[] GetHealthyImageIds(long rootId)
    {
        lock (_dbLock)
        {
            using var conn = CreateConnection();
            using var cmd = conn.CreateCommand();

            cmd.CommandText = "SELECT id FROM images WHERE root_id = @rootId AND state = 1 ORDER BY id ASC;";
            cmd.Parameters.AddWithValue("@rootId", rootId);

            var ids = new List<int>(131072);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                ids.Add((int)reader.GetInt64(0));
            }

            return ids.ToArray();
        }
    }

    public string? GetRelativePath(long imageId)
    {
        lock (_dbLock)
        {
            using var conn = CreateConnection();
            using var cmd = conn.CreateCommand();

            cmd.CommandText = "SELECT relative_path FROM images WHERE id = @id LIMIT 1;";
            cmd.Parameters.AddWithValue("@id", imageId);

            var result = cmd.ExecuteScalar();
            return result as string;
        }
    }

    public void UpsertImagesBatch(long rootId, IReadOnlyList<ImageUpsertEntry> entries, long scanVersion)
    {
        if (entries.Count == 0) return;

        lock (_dbLock)
        {
            using var conn = CreateConnection();
            using var tx = conn.BeginTransaction();

            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
                INSERT INTO images (root_id, relative_path, extension, length, last_write_utc_ticks, state, retry_after_utc_ticks, seen_scan_version)
                VALUES (@rootId, @relPath, @ext, @len, @lastWrite, 1, NULL, @seenVer)
                ON CONFLICT(root_id, relative_path) DO UPDATE SET
                    extension = excluded.extension,
                    length = excluded.length,
                    last_write_utc_ticks = excluded.last_write_utc_ticks,
                    state = 1,
                    seen_scan_version = excluded.seen_scan_version;
            ";

            var pRootId = cmd.Parameters.Add("@rootId", SqliteType.Integer);
            var pRelPath = cmd.Parameters.Add("@relPath", SqliteType.Text);
            var pExt = cmd.Parameters.Add("@ext", SqliteType.Integer);
            var pLen = cmd.Parameters.Add("@len", SqliteType.Integer);
            var pLastWrite = cmd.Parameters.Add("@lastWrite", SqliteType.Integer);
            var pSeenVer = cmd.Parameters.Add("@seenVer", SqliteType.Integer);

            pRootId.Value = rootId;
            pSeenVer.Value = scanVersion;

            foreach (var entry in entries)
            {
                pRelPath.Value = entry.RelativePath;
                pExt.Value = (int)entry.Extension;
                pLen.Value = entry.Length;
                pLastWrite.Value = entry.LastWriteUtcTicks;
                cmd.ExecuteNonQuery();
            }

            tx.Commit();
        }
    }

    public void MarkUnseenImagesInvalid(long rootId, long scanVersion)
    {
        lock (_dbLock)
        {
            using var conn = CreateConnection();
            using var cmd = conn.CreateCommand();

            cmd.CommandText = @"
                UPDATE images
                SET state = 3
                WHERE root_id = @rootId AND seen_scan_version != @scanVersion AND state != 3;
            ";
            cmd.Parameters.AddWithValue("@rootId", rootId);
            cmd.Parameters.AddWithValue("@scanVersion", scanVersion);

            cmd.ExecuteNonQuery();
        }
    }

    public void DeleteImage(long rootId, string relativePath)
    {
        lock (_dbLock)
        {
            using var conn = CreateConnection();
            using var cmd = conn.CreateCommand();

            cmd.CommandText = @"
                UPDATE images
                SET state = 3
                WHERE root_id = @rootId AND relative_path = @relPath;
            ";
            cmd.Parameters.AddWithValue("@rootId", rootId);
            cmd.Parameters.AddWithValue("@relPath", relativePath);
            cmd.ExecuteNonQuery();
        }
    }

    public void OptimizeDatabase()
    {
        lock (_dbLock)
        {
            try
            {
                using var conn = CreateConnection();
                using var cmd = conn.CreateCommand();

                cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE); PRAGMA optimize; PRAGMA incremental_vacuum;";
                cmd.ExecuteNonQuery();

                AppLogger.Instance.Info("SqliteCatalogDatabase: Database maintenance complete (WAL truncated, indexes optimized).");
            }
            catch (Exception ex)
            {
                AppLogger.Instance.Warn($"SqliteCatalogDatabase: Error during database optimization: {ex.Message}");
            }
        }
    }

    public void Dispose()
    {
        OptimizeDatabase();
    }
}
