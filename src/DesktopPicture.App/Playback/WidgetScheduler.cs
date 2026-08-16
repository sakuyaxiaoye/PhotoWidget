using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using DesktopPicture.Catalog;
using DesktopPicture.Config;
using DesktopPicture.Decoding;
using DesktopPicture.Display;
using DesktopPicture.Logging;
using DesktopPicture.Random;
using DesktopPicture.UI;

namespace DesktopPicture.Playback;

public sealed record PredecodedItem(int CandidateId, string FilePath, BitmapSource Bitmap);

public sealed class WidgetScheduler : IDisposable
{
    private readonly WidgetConfig _config;
    private readonly WidgetWindow _window;
    private readonly RandomSelector _randomSelector;
    private readonly PlaybackHistory _history = new(maxCapacity: 100);

    private RootCatalogContext? _rootContext;
    private Timer? _timer;
    private Timer? _predecodeTimer;
    private PredecodedItem? _predecodedItem;
    private readonly object _predecodeLock = new();

    private long _currentGeneration = 0;
    private int? _lastShownId;
    private string? _lastShownPath;
    private bool _isDisposed = false;
    private CancellationTokenSource? _scanCts;

    public WidgetConfig Config => _config;
    public RootCatalogContext? RootContext => _rootContext;
    public string? LastShownPath => _lastShownPath;
    public int? LastShownId => _lastShownId;
    public bool CanGoBack => _history.CanGoBack;

    public WidgetScheduler(WidgetConfig config, WidgetWindow window)
    {
        _config = config;
        _window = window;
        _lastShownId = (int?)config.LastShownCatalogId;
        _randomSelector = new RandomSelector();

        DecodePipeline.Instance.OnDecoded += HandleImageDecoded;
    }

    public void Start()
    {
        if (_isDisposed) return;

        // 1. Warm Startup: Load cached preview if available
        var cachedPreview = StartupPreviewCache.Instance.LoadPreview(_config.Id);
        if (cachedPreview != null)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                _window.DisplayImage(cachedPreview, "已加载快速缓存首图");
            });
        }

        // 2. Start catalog context & playback
        RescanAndPlay();
    }

    public void RescanAndPlay()
    {
        _scanCts?.Cancel();
        _scanCts?.Dispose();
        _scanCts = new CancellationTokenSource();

        var rootPath = _config.RootPath;
        if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                _window.ShowPlaceholder("未配置有效图片文件夹", "请右键托盘图标或在设置中指定图片根目录");
            });
            return;
        }

        if (_rootContext != null && !string.Equals(_rootContext.CanonicalRoot, rootPath, StringComparison.OrdinalIgnoreCase))
        {
            _rootContext.OnSnapshotUpdated -= HandleSnapshotUpdated;
            CatalogManager.Instance.ReleaseContext(_rootContext.CanonicalRoot);
            _rootContext = null;
        }

        if (_rootContext == null)
        {
            _rootContext = CatalogManager.Instance.GetOrCreateContext(rootPath);
            _rootContext.OnSnapshotUpdated += HandleSnapshotUpdated;
        }

        Application.Current?.Dispatcher.Invoke(() =>
        {
            _window.UpdateCandidateCount(_rootContext.CurrentSnapshot.Count);
            if (!_rootContext.CurrentSnapshot.IsEmpty)
            {
                // Hot startup: zero-wait instant display
                SwitchNext();
            }
            else
            {
                _window.ShowScanningState();
            }
        });

        var token = _scanCts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                // Trigger initial reconciliation/scan in background
                await _rootContext.TriggerReconciliationAsync(
                    onProgress: count =>
                    {
                        Application.Current?.Dispatcher.Invoke(() =>
                        {
                            _window.UpdateCandidateCount(count);
                        });
                    },
                    ct: token);

                Application.Current?.Dispatcher.Invoke(() =>
                {
                    _window.UpdateCandidateCount(_rootContext.CurrentSnapshot.Count);
                    if (_rootContext.CurrentSnapshot.IsEmpty)
                    {
                        _window.ShowPlaceholder("文件夹内未找到支持的图片", "支持格式: JPG, JPEG, PNG, WebP, AVIF, HEIC, GIF 等");
                    }
                    else
                    {
                        SwitchNext();
                    }
                });

                ResetTimer();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                AppLogger.Instance.Error($"WidgetScheduler: Error in catalog context for '{rootPath}'", ex);
            }
        }, token);
    }

    private void HandleSnapshotUpdated(CompactIdSnapshot snapshot)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            _window.UpdateCandidateCount(snapshot.Count);
            if (snapshot.IsEmpty)
            {
                _window.ShowPlaceholder("文件夹内未找到支持的图片", "支持格式: JPG, JPEG, PNG, WebP, AVIF, HEIC, GIF 等");
            }
            else if (_lastShownPath == null)
            {
                SwitchNext();
            }
        });
    }

    public void SwitchPrevious()
    {
        if (_isDisposed) return;

        string? prevPath = _history.GoBack();
        if (!string.IsNullOrEmpty(prevPath))
        {
            TriggerDecode(prevPath, isHistoryNavigation: true);
            ResetTimer();
        }
    }

    public void SwitchNext(bool isManual = false)
    {
        if (_isDisposed || _rootContext == null) return;

        // 1. If user previously went back into history, advance forward along history stack
        if (_history.CanGoForward)
        {
            string? forwardPath = _history.GoForward();
            if (!string.IsNullOrEmpty(forwardPath) && File.Exists(forwardPath))
            {
                lock (_predecodeLock)
                {
                    _predecodedItem = null;
                }
                TriggerDecode(forwardPath, isHistoryNavigation: true);
                if (isManual)
                {
                    ResetTimer();
                }
                return;
            }
        }

        // 2. Check if pre-decoded buffer is available for zero-latency instant transition
        PredecodedItem? predecoded = null;
        lock (_predecodeLock)
        {
            predecoded = _predecodedItem;
            _predecodedItem = null;
        }

        if (predecoded != null && File.Exists(predecoded.FilePath))
        {
            _lastShownId = predecoded.CandidateId;
            _config.LastShownCatalogId = predecoded.CandidateId;
            _lastShownPath = predecoded.FilePath;
            _history.Push(predecoded.FilePath);

            Application.Current?.Dispatcher.Invoke(() =>
            {
                _window.DisplayImage(predecoded.Bitmap, predecoded.FilePath);
            });

            StartupPreviewCache.Instance.SavePreview(_config.Id, predecoded.Bitmap);
            SettingsService.Instance.ScheduleSave(250);

            if (isManual)
            {
                ResetTimer();
            }
            return;
        }

        // 3. Otherwise randomly pick from catalog
        var candidate = _randomSelector.SelectValidCandidateId(_rootContext, _lastShownId);
        if (candidate.HasValue)
        {
            _lastShownId = candidate.Value.Id;
            _config.LastShownCatalogId = candidate.Value.Id;
            TriggerDecode(candidate.Value.FullPath);
        }
        else
        {
            AppLogger.Instance.Warn($"WidgetScheduler: No valid candidate found for widget {_config.Name}.");
        }

        if (isManual)
        {
            ResetTimer();
        }
    }

    public void RedecodeCurrent()
    {
        if (_isDisposed || string.IsNullOrEmpty(_lastShownPath)) return;
        TriggerDecode(_lastShownPath, isHistoryNavigation: true);
    }

    private Gif.GifAnimationPlayer? _gifPlayer;

    private void TriggerDecode(string filePath, bool isHistoryNavigation = false)
    {
        long gen = Interlocked.Increment(ref _currentGeneration);
        DecodePipeline.Instance.SetActiveGeneration(_config.Id, gen);

        // Stop any currently playing GIF immediately
        _gifPlayer?.Dispose();
        _gifPlayer = null;

        var monitor = DisplayCoordinateService.Instance.GetMonitorForWindow(_window.Handle);
        double scaleX = monitor?.ScaleX ?? 1.0;
        double scaleY = monitor?.ScaleY ?? 1.0;

        int targetWidthPx = (int)Math.Round(_config.WidthDip * scaleX);
        int targetHeightPx = (int)Math.Round(_config.HeightDip * scaleY);

        if (filePath.EndsWith(".gif", StringComparison.OrdinalIgnoreCase))
        {
            var gifDecoder = new Gif.SkiaGifDecoder(filePath);
            if (gifDecoder.IsAnimated)
            {
                _lastShownPath = filePath;
                if (!isHistoryNavigation)
                {
                    _history.Push(filePath);
                }

                _gifPlayer = new Gif.GifAnimationPlayer(
                    gifDecoder,
                    targetWidthPx,
                    targetHeightPx,
                    frame =>
                    {
                        if (gen == _currentGeneration)
                        {
                            Application.Current?.Dispatcher.Invoke(() =>
                            {
                                _window.DisplayImage(frame, filePath);
                            });
                        }
                    });

                _gifPlayer.Start();
                SettingsService.Instance.ScheduleSave(250);
                return;
            }
            gifDecoder.Dispose();
        }

        var request = new DecodeRequest(
            _config.Id,
            gen,
            filePath,
            targetWidthPx,
            targetHeightPx,
            CancellationToken.None,
            isHistoryNavigation);

        DecodePipeline.Instance.QueueRequest(request);
    }

    private void HandleImageDecoded(DecodeResult result)
    {
        if (result.WidgetId != _config.Id) return;
        if (result.Generation != _currentGeneration) return;

        if (!result.Success || result.Bitmap == null)
        {
            _randomSelector.BackoffTracker.RecordFailure(result.FilePath);
            AppLogger.Instance.Warn($"WidgetScheduler: Decode failed for '{result.FilePath}'. Retrying next image.");
            SwitchNext();
            return;
        }

        _lastShownPath = result.FilePath;
        if (!result.IsHistoryNavigation)
        {
            _history.Push(result.FilePath);
        }

        Application.Current?.Dispatcher.Invoke(() =>
        {
            _window.DisplayImage(result.Bitmap, result.FilePath);
        });

        // Save warm startup preview
        StartupPreviewCache.Instance.SavePreview(_config.Id, result.Bitmap);
        SettingsService.Instance.ScheduleSave(250);
    }

    public void ResetTimer()
    {
        _timer?.Dispose();
        _predecodeTimer?.Dispose();

        lock (_predecodeLock)
        {
            _predecodedItem = null;
        }

        if (_config.Paused) return;

        int intervalMs = Math.Clamp(_config.IntervalSeconds, 5, 86400) * 1000;

        // Schedule pre-decode 2.5 seconds ahead of time
        int predecodeDelay = Math.Max(1000, intervalMs - 2500);
        _predecodeTimer = new Timer(_ =>
        {
            PredecodeNext();
        }, null, predecodeDelay, Timeout.Infinite);

        _timer = new Timer(_ =>
        {
            if (!_config.Paused && !_isDisposed)
            {
                SwitchNext();
            }
        }, null, intervalMs, intervalMs);
    }

    private void PredecodeNext()
    {
        if (_isDisposed || _config.Paused || _rootContext == null) return;

        Task.Run(() =>
        {
            try
            {
                string? targetPath = null;
                int candidateId = 0;

                if (_history.CanGoForward)
                {
                    targetPath = _history.PeekForward();
                }
                else
                {
                    var candidate = _randomSelector.SelectValidCandidateId(_rootContext, _lastShownId);
                    if (candidate.HasValue)
                    {
                        targetPath = candidate.Value.FullPath;
                        candidateId = candidate.Value.Id;
                    }
                }

                if (!string.IsNullOrEmpty(targetPath) && File.Exists(targetPath) && !targetPath.EndsWith(".gif", StringComparison.OrdinalIgnoreCase))
                {
                    var monitor = DisplayCoordinateService.Instance.GetMonitorForWindow(_window.Handle);
                    double scaleX = monitor?.ScaleX ?? 1.0;
                    double scaleY = monitor?.ScaleY ?? 1.0;
                    int targetWidthPx = (int)Math.Round(_config.WidthDip * scaleX);
                    int targetHeightPx = (int)Math.Round(_config.HeightDip * scaleY);

                    var bitmap = ImageDecoder.DecodeAndCropToCover(targetPath, targetWidthPx, targetHeightPx);
                    if (bitmap != null)
                    {
                        lock (_predecodeLock)
                        {
                            _predecodedItem = new PredecodedItem(candidateId, targetPath, bitmap);
                        }
                    }
                }
            }
            catch { }
        });
    }

    public void UpdateInterval(int seconds)
    {
        _config.IntervalSeconds = Math.Clamp(seconds, 5, 86400);
        ResetTimer();
    }

    public void SetPaused(bool paused)
    {
        _config.Paused = paused;
        if (paused)
        {
            _timer?.Dispose();
            _timer = null;
            _predecodeTimer?.Dispose();
            _predecodeTimer = null;
            _gifPlayer?.Pause();
        }
        else
        {
            _gifPlayer?.Resume();
            ResetTimer();
        }
    }

    public void Dispose()
    {
        _isDisposed = true;
        _scanCts?.Cancel();
        _scanCts?.Dispose();
        _timer?.Dispose();
        _timer = null;
        _predecodeTimer?.Dispose();
        _predecodeTimer = null;

        _gifPlayer?.Dispose();
        _gifPlayer = null;

        if (_rootContext != null)
        {
            _rootContext.OnSnapshotUpdated -= HandleSnapshotUpdated;
            CatalogManager.Instance.ReleaseContext(_rootContext.CanonicalRoot);
            _rootContext = null;
        }

        DecodePipeline.Instance.OnDecoded -= HandleImageDecoded;
    }
}
