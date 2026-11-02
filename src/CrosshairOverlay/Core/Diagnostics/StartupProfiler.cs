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

    public static bool Enabled { get; } =
        Environment.GetEnvironmentVariable("CROSSGOVERLAY_BENCHMARK_TIMELINE") == "1";

    public static void Begin(long constructedAt, double runtimeStartupMs)
    {
        if (!Enabled) return;
        _constructedAt = constructedAt;
        _runtimeStartupMs = runtimeStartupMs;
    }

    public static double ElapsedMs(long? timestamp = null)
    {
        if (!Enabled || _constructedAt == 0) return -1;
        return _runtimeStartupMs +
            Stopwatch.GetElapsedTime(_constructedAt, timestamp ?? Stopwatch.GetTimestamp()).TotalMilliseconds;
    }
}
