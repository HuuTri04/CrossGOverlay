using System.IO;
using CrosshairOverlay.Core.Abstractions;

namespace CrosshairOverlay.Core.Diagnostics;

/// <summary>
/// Explicit, isolated profiler probe. It is inert for normal launches and never touches a game process.
/// The controller creates a start file after its before-snapshot, so both paths use the same warm state.
/// </summary>
internal static class BenchmarkScenarioRunner
{
    public static void TryStart(IPresetLibrary library, ITrayIconController tray)
    {
        var root = Environment.GetEnvironmentVariable("CROSSGOVERLAY_BENCHMARK_ROOT");
        var scenario = Environment.GetEnvironmentVariable("CROSSGOVERLAY_BENCHMARK_SCENARIO");
#if BENCHMARK
        if (string.IsNullOrWhiteSpace(root) || scenario is not ("preset" or "toast" or "preset-ab")) return;
#else
        if (string.IsNullOrWhiteSpace(root) || scenario is not ("preset" or "toast")) return;
#endif

        var countText = Environment.GetEnvironmentVariable("CROSSGOVERLAY_BENCHMARK_COUNT");
        if (!int.TryParse(countText, out var count) || count < 2 || count > 5000 ||
            (scenario == "toast" && count > 50)) return;

#if BENCHMARK
        if (scenario is "preset" or "preset-ab" && library.Presets.Count < 2) return;
#else
        if (scenario == "preset" && library.Presets.Count < 2) return;
#endif

        _ = RunAsync(library, tray, Path.Combine(root, "diagnostics"), scenario, count);
    }

    private static async Task RunAsync(
        IPresetLibrary library, ITrayIconController tray, string directory, string scenario, int count)
    {
        try
        {
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(Path.Combine(directory, "ready.txt"), scenario);
            var start = Path.Combine(directory, "start.signal");
            var timeout = DateTime.UtcNow.AddMinutes(2);
            while (!File.Exists(start) && DateTime.UtcNow < timeout)
                await Task.Delay(50);
            if (!File.Exists(start)) return;

#if BENCHMARK
            if (scenario == "preset-ab") BenchmarkPresetProbe.BeginRun();
#endif
            for (var i = 1; i <= count; i++)
            {
#if BENCHMARK
                if (scenario is "preset" or "preset-ab")
#else
                if (scenario == "preset")
#endif
                {
#if BENCHMARK
                    if (scenario == "preset-ab")
                        BenchmarkPresetProbe.RequestReceived("FixtureInternal", library.Presets[i % 2].Id);
#endif
                    library.SetActive(library.Presets[i % 2]);
                }
                else
                    tray.ShowNotification("CrossGOverlay", $"Benchmark toast {i}/{count}");

#if BENCHMARK
                if (scenario is "toast" or "preset-ab" || i % 100 == 0)
#else
                if (scenario == "toast" || i % 100 == 0)
#endif
                    await File.WriteAllTextAsync(Path.Combine(directory, "progress.txt"), i.ToString());
#if BENCHMARK
                await Task.Delay(scenario == "toast" ? 300 : scenario == "preset-ab" ? 80 : 20);
#else
                await Task.Delay(scenario == "toast" ? 300 : 20);
#endif
            }

            // The final toast auto-dismisses before the after-snapshot is requested.
            if (scenario == "toast") await Task.Delay(4000);
#if BENCHMARK
            if (scenario == "preset-ab")
            {
                await Task.Delay(500);
                BenchmarkPresetProbe.EndRun();
                await BenchmarkPresetProbe.FlushAsync();
            }
#endif
            await File.WriteAllTextAsync(Path.Combine(directory, "done.txt"), count.ToString());
            if (Environment.GetEnvironmentVariable("CROSSGOVERLAY_BENCHMARK_AUTO_EXIT") == "1")
                App.RequestShutdown();
        }
        catch (Exception ex)
        {
            try { await File.WriteAllTextAsync(Path.Combine(directory, "error.txt"), ex.ToString()); }
            catch (IOException) { /* Diagnostic path can disappear when the benchmark ends. */ }
            catch (UnauthorizedAccessException) { }
        }
    }
}
