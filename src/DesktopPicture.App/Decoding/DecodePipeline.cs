using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using DesktopPicture.Logging;

namespace DesktopPicture.Decoding;

public sealed class DecodePipeline : IDisposable
{
    private static readonly Lazy<DecodePipeline> _instance = new(() => new DecodePipeline());
    public static DecodePipeline Instance => _instance.Value;

    private readonly Channel<DecodeRequest> _channel;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task[] _workers;
    private readonly ConcurrentDictionary<string, long> _activeGenerations = new();

    public const int MaxConcurrentWorkers = 2;
    public const int ChannelCapacity = 8;

    public event Action<DecodeResult>? OnDecoded;

    public DecodePipeline()
    {
        var options = new BoundedChannelOptions(ChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleWriter = false,
            SingleReader = false
        };

        _channel = Channel.CreateBounded<DecodeRequest>(options);

        _workers = new Task[MaxConcurrentWorkers];
        for (int i = 0; i < MaxConcurrentWorkers; i++)
        {
            int workerId = i + 1;
            _workers[i] = Task.Run(() => WorkerLoop(workerId, _cts.Token));
        }

        AppLogger.Instance.Info($"DecodePipeline: Initialized with {MaxConcurrentWorkers} workers.");
    }

    public void SetActiveGeneration(string widgetId, long generation)
    {
        _activeGenerations[widgetId] = generation;
    }

    public bool QueueRequest(DecodeRequest request)
    {
        _activeGenerations[request.WidgetId] = request.Generation;
        return _channel.Writer.TryWrite(request);
    }

    private async Task WorkerLoop(int workerId, CancellationToken ct)
    {
        var reader = _channel.Reader;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var request = await reader.ReadAsync(ct);

                // Generation check: Drop outdated requests before decoding
                if (_activeGenerations.TryGetValue(request.WidgetId, out var activeGen) && activeGen > request.Generation)
                {
                    continue;
                }

                if (request.CancellationToken.IsCancellationRequested)
                {
                    continue;
                }

                var bitmap = ImageDecoder.DecodeAndCropToCover(
                    request.FilePath,
                    request.TargetWidthPx,
                    request.TargetHeightPx);

                // Re-check generation after decode
                if (_activeGenerations.TryGetValue(request.WidgetId, out activeGen) && activeGen > request.Generation)
                {
                    continue;
                }

                bool success = bitmap != null;
                var result = new DecodeResult(
                    request.WidgetId,
                    request.Generation,
                    request.FilePath,
                    bitmap,
                    success,
                    success ? null : "Decode failed",
                    request.IsHistoryNavigation);

                OnDecoded?.Invoke(result);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                AppLogger.Instance.Error($"DecodePipeline worker {workerId} exception", ex);
            }
        }
    }

    public void Dispose()
    {
        _channel.Writer.TryComplete();
        _cts.Cancel();
        _cts.Dispose();
    }
}
