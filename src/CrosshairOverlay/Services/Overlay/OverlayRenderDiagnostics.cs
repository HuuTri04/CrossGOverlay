#if DEBUG
namespace CrosshairOverlay.Services.Overlay;

/// <summary>Read these counters in the debugger without logging on every frame.</summary>
internal static class OverlayRenderDiagnostics
{
    private static long _renderCalls;
    private static long _invalidations;
    private static long _windowPositions;

    internal static (long RenderCalls, long Invalidations, long WindowPositions) Snapshot =>
        (Interlocked.Read(ref _renderCalls), Interlocked.Read(ref _invalidations),
            Interlocked.Read(ref _windowPositions));

    internal static void Rendered() => Interlocked.Increment(ref _renderCalls);
    internal static void Invalidated() => Interlocked.Increment(ref _invalidations);
    internal static void Positioned() => Interlocked.Increment(ref _windowPositions);
}
#endif
