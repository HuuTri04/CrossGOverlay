#if STRESS
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using CrosshairOverlay.Core.Abstractions;
using CrosshairOverlay.Core.Models;
using CrosshairOverlay.Services.Presets;
using CrosshairOverlay.Services.Rendering;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace CrosshairOverlay.Tests;

/// <summary>Run alone with -p:DefineConstants=STRESS and a test-name filter.</summary>
public sealed class PresetResourceStabilityTests(ITestOutputHelper output)
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetGuiResources(nint process, int flags);

    [Fact]
    public void RendererAndPresetLibraryStayFlatAfterWarmup()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { Run(); }
            catch (Exception ex) { failure = ex; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(60)), "Preset stress exceeded 60 seconds.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private void Run()
    {
        using var process = Process.GetCurrentProcess();
        using var library = new PresetLibrary(TestCrosshairTests.StubFor<IPresetRepository>(),
            NullLogger<PresetLibrary>.Instance);
        var renderer = new CrosshairRenderer(NullLogger<CrosshairRenderer>.Instance);
        var presets = new[] { CrosshairProfile.CreateDefault(), CrosshairProfile.CreateDefault() };
        var options = CrosshairRenderOptions.Default;
        library.ActiveChanged += (_, _) => GC.KeepAlive(renderer.Build(library.Active!, options));

        void Switch(int count)
        {
            for (var i = 0; i < count; i++) library.SetActive(presets[i & 1]);
        }

        (int Gdi, long PrivateBytes) Sample()
        {
            GC.Collect(2, GCCollectionMode.Forced, blocking: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, blocking: true);
            process.Refresh();
            return (GetGuiResources(process.Handle, 0), process.PrivateMemorySize64);
        }

        Switch(1000);
        var warm = Sample();
        output.WriteLine($"warmup: GDI={warm.Gdi}, PrivateBytes={warm.PrivateBytes}");
        for (var stage = 1; stage <= 3; stage++)
        {
            Switch(5000);
            var current = Sample();
            output.WriteLine($"stage {stage}: GDI={current.Gdi}, PrivateBytes={current.PrivateBytes}");
            if (stage != 3) continue;
            Assert.True(current.Gdi <= warm.Gdi + 2,
                $"GDI after stage 3: {current.Gdi}; warmup: {warm.Gdi}.");
            Assert.True(current.PrivateBytes <= warm.PrivateBytes + 1024 * 1024,
                $"Private Bytes after stage 3: {current.PrivateBytes}; warmup: {warm.PrivateBytes}.");
        }
    }
}
#endif
