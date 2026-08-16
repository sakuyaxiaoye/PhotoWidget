using System;
using System.Diagnostics.Metrics;

namespace DesktopPicture.Diagnostics;

public static class AppMetrics
{
    public static readonly Meter Meter = new("DesktopPicture", "0.1.0");

    public static readonly Histogram<double> SwitchLatencyMs =
        Meter.CreateHistogram<double>("switch.latency_ms", "ms", "Latency of image switch operation");

    public static readonly Histogram<double> DecodeDurationMs =
        Meter.CreateHistogram<double>("decode.duration_ms", "ms", "Duration of image decode and cover crop");

    public static readonly Counter<long> DecodeFailures =
        Meter.CreateCounter<long>("decode.failures", "count", "Number of image decode failures");

    public static readonly Counter<long> WatchEvents =
        Meter.CreateCounter<long>("watch.events", "count", "Number of file system watcher events received");

    public static readonly Counter<long> WatchOverflows =
        Meter.CreateCounter<long>("watch.overflows", "count", "Number of file system watcher buffer overflows");

    public static readonly Histogram<double> ReconcileDurationMs =
        Meter.CreateHistogram<double>("watch.reconcile_duration_ms", "ms", "Duration of full directory reconciliation");
}
