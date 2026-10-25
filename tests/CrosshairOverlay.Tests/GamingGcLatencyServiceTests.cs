using System.Runtime;
using CrosshairOverlay.Services.System;
using Xunit;

namespace CrosshairOverlay.Tests;

public sealed class GamingGcLatencyServiceTests
{
    [Fact]
    public void RestoresPreviousModeAfterGameAndOnDispose()
    {
        var mode = GCLatencyMode.Interactive;
        var writes = 0;
        var service = new GamingGcLatencyService(() => mode, value => { mode = value; writes++; });

        service.SetActive(true);
        service.SetActive(true);
        Assert.Equal(GCLatencyMode.SustainedLowLatency, mode);
        Assert.Equal(1, writes);

        service.Dispose();
        Assert.Equal(GCLatencyMode.Interactive, mode);
        Assert.Equal(2, writes);
    }

    [Fact]
    public void LeavesHostOwnedNoGcRegionAlone()
    {
        var mode = GCLatencyMode.NoGCRegion;
        var writes = 0;
        using var service = new GamingGcLatencyService(() => mode, value => { mode = value; writes++; });

        service.SetActive(true);
        service.SetActive(false);

        Assert.Equal(GCLatencyMode.NoGCRegion, mode);
        Assert.Equal(0, writes);
    }

    [Fact]
    public void DoesNotOverrideALaterExternalModeChange()
    {
        var mode = GCLatencyMode.Interactive;
        using var service = new GamingGcLatencyService(() => mode, value => mode = value);

        service.SetActive(true);
        mode = GCLatencyMode.Batch;
        service.SetActive(false);

        Assert.Equal(GCLatencyMode.Batch, mode);
    }

    [Fact]
    public void HeapGuardLeavesLowLatencyUntilNextGameSession()
    {
        var mode = GCLatencyMode.Interactive;
        var heapBytes = GamingGcLatencyService.ManagedHeapLimitBytes;
        var writes = 0;
        using var service = new GamingGcLatencyService(() => mode,
            value => { mode = value; writes++; }, () => heapBytes);

        service.SetActive(true);
        service.CheckMemoryPressure();
        Assert.Equal(GCLatencyMode.SustainedLowLatency, mode);

        heapBytes++;
        service.CheckMemoryPressure();
        Assert.Equal(GCLatencyMode.Interactive, mode);
        service.SetActive(true);
        Assert.Equal(2, writes);

        service.SetActive(false);
        heapBytes = GamingGcLatencyService.ManagedHeapLimitBytes;
        service.SetActive(true);
        Assert.Equal(GCLatencyMode.SustainedLowLatency, mode);
        Assert.Equal(3, writes);
    }

    [Fact]
    public void HighHeapAtGameEntrySkipsLowLatencyMode()
    {
        var mode = GCLatencyMode.Interactive;
        using var service = new GamingGcLatencyService(() => mode, value => mode = value,
            () => GamingGcLatencyService.ManagedHeapLimitBytes + 1);

        service.SetActive(true);
        service.CheckMemoryPressure();
        Assert.Equal(GCLatencyMode.Interactive, mode);
    }
}
