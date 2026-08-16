using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using DesktopPicture.Logging;

namespace DesktopPicture.Gif;

public sealed class GifAnimationPlayer : IDisposable
{
    private readonly SkiaGifDecoder _decoder;
    private readonly int _targetWidthPx;
    private readonly int _targetHeightPx;
    private readonly Action<BitmapSource> _onFrameReady;
    private readonly CancellationTokenSource _cts = new();
    private Task? _playbackTask;
    private bool _isPaused = false;
    private readonly object _lock = new();

    public const int MaxFps = 30; // Max 30 FPS per SPEC 9.3
    public const int MinFrameDelayMs = 1000 / MaxFps; // 33 ms

    public bool IsPlaying => _playbackTask != null && !_playbackTask.IsCompleted;
    public bool IsPaused => _isPaused;

    public GifAnimationPlayer(
        SkiaGifDecoder decoder,
        int targetWidthPx,
        int targetHeightPx,
        Action<BitmapSource> onFrameReady)
    {
        _decoder = decoder;
        _targetWidthPx = targetWidthPx;
        _targetHeightPx = targetHeightPx;
        _onFrameReady = onFrameReady;
    }

    public void Start()
    {
        lock (_lock)
        {
            if (_playbackTask != null) return;
            _playbackTask = Task.Run(() => PlaybackLoop(_cts.Token));
        }
    }

    public void Pause()
    {
        _isPaused = true;
    }

    public void Resume()
    {
        _isPaused = false;
    }

    private async Task PlaybackLoop(CancellationToken ct)
    {
        int frameCount = _decoder.FrameCount;
        if (frameCount <= 0) return;

        int currentFrameIndex = 0;
        var sw = Stopwatch.StartNew();

        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (_isPaused)
                {
                    await Task.Delay(100, ct);
                    continue;
                }

                int rawDuration = _decoder.GetFrameDurationMs(currentFrameIndex);
                int frameDurationMs = Math.Max(MinFrameDelayMs, rawDuration);

                long frameStartTime = sw.ElapsedMilliseconds;

                var frameBitmap = _decoder.RenderFrame(currentFrameIndex, _targetWidthPx, _targetHeightPx);
                if (frameBitmap != null)
                {
                    _onFrameReady(frameBitmap);
                }

                long renderTime = sw.ElapsedMilliseconds - frameStartTime;
                int remainingDelay = (int)(frameDurationMs - renderTime);

                if (remainingDelay > 0)
                {
                    await Task.Delay(remainingDelay, ct);
                }

                // Advance frame index (looping)
                currentFrameIndex = (currentFrameIndex + 1) % frameCount;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                AppLogger.Instance.Warn($"GifAnimationPlayer loop error: {ex.Message}");
                break;
            }
        }
    }

    public void Stop()
    {
        _cts.Cancel();
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        _decoder.Dispose();
    }
}
