using System.Runtime;

namespace CrosshairOverlay.Services.System;

/// <summary>
/// Limits the process-wide low-latency GC setting to a visible, matched game session.
/// It never forces a collection, and restores the mode that was in use beforehand.
/// </summary>
public sealed class GamingGcLatencyService : IDisposable
{
    internal const long ManagedHeapLimitBytes = 250L * 1024 * 1024;
    private static readonly TimeSpan MemoryCheckInterval = TimeSpan.FromSeconds(30);
    private readonly object _gate = new();
    private readonly Func<GCLatencyMode> _getMode;
    private readonly Action<GCLatencyMode> _setMode;
    private readonly Func<long> _getHeapBytes;
    private Timer? _memoryTimer;
    private GCLatencyMode _previousMode;
    private bool _active;
    private bool _sessionRequested;
    private bool _memoryGuardTripped;
    private bool _disposed;

    public GamingGcLatencyService() : this(() => GCSettings.LatencyMode,
        mode => GCSettings.LatencyMode = mode, () => GC.GetTotalMemory(false)) { }

    internal GamingGcLatencyService(Func<GCLatencyMode> getMode, Action<GCLatencyMode> setMode,
        Func<long>? getHeapBytes = null)
    {
        _getMode = getMode;
        _setMode = setMode;
        _getHeapBytes = getHeapBytes ?? (() => GC.GetTotalMemory(false));
    }

    public void SetActive(bool active)
    {
        lock (_gate)
        {
            if (_disposed) return;
            if (!active)
            {
                _sessionRequested = false;
                _memoryGuardTripped = false;
                Restore();
                return;
            }
            if (_sessionRequested || _memoryGuardTripped) return;
            if (_getHeapBytes() > ManagedHeapLimitBytes)
            {
                _sessionRequested = true;
                _memoryGuardTripped = true;
                return;
            }

            var previous = _getMode();
            // A host may own a NoGCRegion; do not disturb its state.
            if (previous == GCLatencyMode.NoGCRegion) return;

            try
            {
                _setMode(GCLatencyMode.SustainedLowLatency);
                if (_getMode() != GCLatencyMode.SustainedLowLatency) return;
            }
            catch (InvalidOperationException)
            {
                // Background GC may be disabled by runtime configuration.
                return;
            }

            _previousMode = previous;
            _active = true;
            _sessionRequested = true;
            _memoryTimer = new Timer(static state =>
                ((GamingGcLatencyService)state!).CheckMemoryPressure(), this,
                MemoryCheckInterval, MemoryCheckInterval);
        }
    }

    internal void CheckMemoryPressure()
    {
        lock (_gate)
        {
            if (_disposed || !_active || _getHeapBytes() <= ManagedHeapLimitBytes) return;
            // Stop low-latency mode for the rest of this game session. Re-entering it on each
            // visibility event would otherwise immediately recreate the same heap pressure.
            _memoryGuardTripped = true;
            Restore();
        }
    }

    private void Restore()
    {
        _memoryTimer?.Dispose();
        _memoryTimer = null;
        if (!_active) return;
        // Respect a later change made by another part of the process.
        if (_getMode() == GCLatencyMode.SustainedLowLatency)
            _setMode(_previousMode);
        _active = false;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            Restore();
            _disposed = true;
        }
    }
}
