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
}
