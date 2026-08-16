using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using DesktopPicture.Logging;
using DesktopPicture.Scanning;

namespace DesktopPicture.Watcher;

public enum WatcherChangeKind
{
    Upsert,
    Delete
}

public sealed record WatcherEvent(WatcherChangeKind Kind, string RelativePath, long Length = 0, long LastWriteUtcTicks = 0);

public sealed class DirectoryWatcher : IDisposable
{
    private readonly string _canonicalRoot;
    private readonly FileSystemWatcher? _fsw;
    private readonly Channel<WatcherEvent> _channel;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _debounceTask;

    public event Action<IReadOnlyList<WatcherEvent>>? OnEventsBatchProcessed;
    public event Action? OnWatcherOverflow;

    public DirectoryWatcher(string canonicalRoot)
    {
        _canonicalRoot = canonicalRoot;

        var channelOptions = new BoundedChannelOptions(4096)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleWriter = false,
            SingleReader = true
        };
        _channel = Channel.CreateBounded<WatcherEvent>(channelOptions);

        _debounceTask = Task.Run(() => ProcessEventsLoop(_cts.Token));

        if (Directory.Exists(canonicalRoot))
        {
            try
            {
                _fsw = new FileSystemWatcher(canonicalRoot)
                {
                    IncludeSubdirectories = true,
                    Filter = "*",
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
                    InternalBufferSize = 32 * 1024 // 32 KiB per SPEC 7.2
                };

                _fsw.Created += OnFileSystemChanged;
                _fsw.Changed += OnFileSystemChanged;
                _fsw.Deleted += OnFileSystemDeleted;
                _fsw.Renamed += OnFileSystemRenamed;
                _fsw.Error += OnFileSystemError;

                _fsw.EnableRaisingEvents = true;
                AppLogger.Instance.Info($"DirectoryWatcher: Started watching '{canonicalRoot}' with 32KB buffer.");
            }
            catch (Exception ex)
            {
                AppLogger.Instance.Error($"DirectoryWatcher: Failed to initialize watcher for '{canonicalRoot}'", ex);
            }
        }
    }

    private void OnFileSystemChanged(object sender, FileSystemEventArgs e)
    {
        if (Directory.Exists(e.FullPath)) return; // Directory change
        if (!DirectoryScanner.IsSupportedImage(e.FullPath)) return;

        string relPath = Path.GetRelativePath(_canonicalRoot, e.FullPath);
        try
        {
            if (File.Exists(e.FullPath))
            {
                var fi = new FileInfo(e.FullPath);
                _channel.Writer.TryWrite(new WatcherEvent(WatcherChangeKind.Upsert, relPath, fi.Length, fi.LastWriteTimeUtc.Ticks));
            }
        }
        catch { }
    }

    private void OnFileSystemDeleted(object sender, FileSystemEventArgs e)
    {
        if (!DirectoryScanner.IsSupportedImage(e.FullPath)) return;

        string relPath = Path.GetRelativePath(_canonicalRoot, e.FullPath);
        _channel.Writer.TryWrite(new WatcherEvent(WatcherChangeKind.Delete, relPath));
    }

    private void OnFileSystemRenamed(object sender, RenamedEventArgs e)
    {
        if (DirectoryScanner.IsSupportedImage(e.OldFullPath))
        {
            string oldRel = Path.GetRelativePath(_canonicalRoot, e.OldFullPath);
            _channel.Writer.TryWrite(new WatcherEvent(WatcherChangeKind.Delete, oldRel));
        }

        if (DirectoryScanner.IsSupportedImage(e.FullPath) && File.Exists(e.FullPath))
        {
            string newRel = Path.GetRelativePath(_canonicalRoot, e.FullPath);
            try
            {
                var fi = new FileInfo(e.FullPath);
                _channel.Writer.TryWrite(new WatcherEvent(WatcherChangeKind.Upsert, newRel, fi.Length, fi.LastWriteTimeUtc.Ticks));
            }
            catch { }
        }
    }

    private void OnFileSystemError(object sender, ErrorEventArgs e)
    {
        AppLogger.Instance.Warn($"DirectoryWatcher: Buffer overflow or error on '{_canonicalRoot}': {e.GetException().Message}");
        OnWatcherOverflow?.Invoke();
    }

    private async Task ProcessEventsLoop(CancellationToken ct)
    {
        var reader = _channel.Reader;
        var batch = new ConcurrentDictionary<string, WatcherEvent>(StringComparer.OrdinalIgnoreCase);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                // Wait for first event
                if (await reader.WaitToReadAsync(ct))
                {
                    while (reader.TryRead(out var evt))
                    {
                        batch[evt.RelativePath] = evt;
                    }

                    // 500ms debounce window per SPEC 7.2
                    await Task.Delay(500, ct);

                    while (reader.TryRead(out var extra))
                    {
                        batch[extra.RelativePath] = extra;
                    }

                    if (!batch.IsEmpty)
                    {
                        var eventsList = new List<WatcherEvent>(batch.Values);
                        batch.Clear();
                        OnEventsBatchProcessed?.Invoke(eventsList);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                AppLogger.Instance.Error("DirectoryWatcher: Error in event debounce loop", ex);
            }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();

        if (_fsw != null)
        {
            _fsw.EnableRaisingEvents = false;
            _fsw.Created -= OnFileSystemChanged;
            _fsw.Changed -= OnFileSystemChanged;
            _fsw.Deleted -= OnFileSystemDeleted;
            _fsw.Renamed -= OnFileSystemRenamed;
            _fsw.Error -= OnFileSystemError;
            _fsw.Dispose();
        }
    }
}
