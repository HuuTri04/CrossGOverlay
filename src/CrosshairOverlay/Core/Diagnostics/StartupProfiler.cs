using System.Diagnostics;

namespace CrosshairOverlay.Core.Diagnostics;

/// <summary>
/// Optional, process-local timestamp origin for Release benchmark runs. Disabled by default.
/// The offset before App construction comes from Process.StartTime; intervals after construction
/// use the high-resolution Stopwatch clock.
/// </summary>
internal static class StartupProfiler
{
    private static long _constructedAt;
    private static double _runtimeStartupMs;
    private static long _settingsShowAt;
    public static double DiskReadMs { get; private set; }
    public static double DecodeMs { get; private set; }
    public static double BlurProcessingMs { get; private set; }
    public static double DispatcherWaitMs { get; private set; }
    public static bool BackgroundCacheHit { get; set; }
    public static double FirstFrameRenderMs => _settingsShowAt == 0 ? 0 :
        Stopwatch.GetElapsedTime(_settingsShowAt).TotalMilliseconds;

    public static bool Enabled { get; } =
        Environment.GetEnvironmentVariable("CROSSGOVERLAY_BENCHMARK_TIMELINE") == "1";

    public static void Begin(long constructedAt, double runtimeStartupMs)
    {
        if (!Enabled) return;
        _constructedAt = constructedAt;
        _runtimeStartupMs = runtimeStartupMs;
        _settingsShowAt = 0;
        DiskReadMs = DecodeMs = BlurProcessingMs = DispatcherWaitMs = 0;
        BackgroundCacheHit = false;
    }

    public static void RecordDecode(double diskReadMs, double decodeMs, long workerCompletedAt)
    {
        if (!Enabled) return;
        DiskReadMs += diskReadMs;
        DecodeMs += decodeMs;
        DispatcherWaitMs += Stopwatch.GetElapsedTime(workerCompletedAt).TotalMilliseconds;
    }

    public static void RecordBlur(long workerStartedAt, long workerCompletedAt)
    {
        if (!Enabled) return;
        BlurProcessingMs += Stopwatch.GetElapsedTime(workerStartedAt, workerCompletedAt).TotalMilliseconds;
        DispatcherWaitMs += Stopwatch.GetElapsedTime(workerCompletedAt).TotalMilliseconds;
    }

    public static void MarkSettingsShow() { if (Enabled) _settingsShowAt = Stopwatch.GetTimestamp(); }

    public static double ElapsedMs(long? timestamp = null)
    {
        if (!Enabled || _constructedAt == 0) return -1;
        return _runtimeStartupMs +
            Stopwatch.GetElapsedTime(_constructedAt, timestamp ?? Stopwatch.GetTimestamp()).TotalMilliseconds;
    }
}
