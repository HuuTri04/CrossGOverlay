using System.Runtime;

namespace CrosshairOverlay.Services.System;

/// <summary>
/// Limits the process-wide low-latency GC setting to a visible, matched game session.
/// It never forces a collection, and restores the mode that was in use beforehand.
/// </summary>
public sealed class GamingGcLatencyService : IDisposable
{
    private readonly Func<GCLatencyMode> _getMode;
    private readonly Action<GCLatencyMode> _setMode;
    private GCLatencyMode _previousMode;
    private bool _active;
    private bool _disposed;

    public GamingGcLatencyService() : this(() => GCSettings.LatencyMode, mode => GCSettings.LatencyMode = mode) { }

    internal GamingGcLatencyService(Func<GCLatencyMode> getMode, Action<GCLatencyMode> setMode)
    {
        _getMode = getMode;
        _setMode = setMode;
    }

    public void SetActive(bool active)
    {
        if (_disposed || active == _active) return;

        if (active)
        {
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
            return;
        }

        Restore();
    }

    private void Restore()
    {
        if (!_active) return;
        // Respect a later change made by another part of the process.
        if (_getMode() == GCLatencyMode.SustainedLowLatency)
            _setMode(_previousMode);
        _active = false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        Restore();
        _disposed = true;
    }
}
