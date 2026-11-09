#if STRESS
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using CrosshairOverlay.Core.Abstractions;
using CrosshairOverlay.Core.Models;
using CrosshairOverlay.Services.Presets;
using CrosshairOverlay.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace CrosshairOverlay.Tests;

/// <summary>Isolated Release x64 stress test; run alone with DefineConstants=STRESS.</summary>
public sealed class PixelPresetPrivateBytesTests(ITestOutputHelper output)
{
    [Fact]
    public void Pixel1024UndoRedoAndPresetSwitchesStayWithinFiveMiBAfterWarmup()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { Run(); }
            catch (Exception ex) { failure = ex; }
            finally { System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromMinutes(3)), "Combined pixel/preset stress exceeded three minutes.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private void Run()
    {
        using var library = new PresetLibrary(TestCrosshairTests.StubFor<IPresetRepository>(),
            NullLogger<PresetLibrary>.Instance);
        var editor = new PixelArtViewModel(library,
            TestCrosshairTests.StubFor<ICustomImageStore>(),
            TestCrosshairTests.StubFor<IOverlayController>()) { GridSize = 1024 };
        var presets = new[] { CrosshairProfile.CreateDefault(), CrosshairProfile.CreateDefault() };
        using var process = Process.GetCurrentProcess();

        void Work(int count)
        {
            for (var i = 0; i < count; i++)
            {
                library.SetActive(presets[i & 1]);
                editor.DrawLine(i & 1023, (i >> 10) & 1023, i & 1023, (i >> 10) & 1023, erase: false);
                editor.FinishStroke();
                if (i % 5 == 0)
                {
                    editor.UndoCommand.Execute(null);
                    editor.RedoCommand.Execute(null);
                }
            }
        }

        long Sample()
        {
            GC.Collect(2, GCCollectionMode.Forced, blocking: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, blocking: true);
            process.Refresh();
            return process.PrivateMemorySize64;
        }

        Work(100);
        var baseline = Sample();
        Work(5000);
        var final = Sample();
        var delta = final - baseline;
        output.WriteLine($"Private Bytes: warmup={baseline}, final={final}, delta={delta}");
        Assert.True(delta < 5L * 1024 * 1024,
            $"Private Bytes increased by {delta} bytes after warmup (limit: 5 MiB).");
    }
}
#endif
