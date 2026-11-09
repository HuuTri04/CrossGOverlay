using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using CrosshairOverlay.Core.Abstractions;
using CrosshairOverlay.ViewModels;
using CrosshairOverlay.Views;

namespace CrosshairOverlay.Core.Diagnostics;

/// <summary>Opt-in, isolated Release stress fixture. Never runs without an explicit benchmark root.</summary>
internal static class StressScenarioRunner
{
    private const int GrGdiObjects = 0;
    private const int GrUserObjects = 1;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetGuiResources(nint process, int flags);

    public static void TryStart(IPresetLibrary library, IDialogService dialogs)
    {
        var root = Environment.GetEnvironmentVariable("CROSSGOVERLAY_BENCHMARK_ROOT");
        var scenario = Environment.GetEnvironmentVariable("CROSSGOVERLAY_STRESS_SCENARIO");
        if (string.IsNullOrWhiteSpace(root) || scenario is not ("settings" or "preset" or "preset-multistage" or "pixel" or "pixel-batched")) return;

        var expected = scenario switch { "settings" => 500, "preset" => 10_000, "preset-multistage" => 16_000, _ => 100_000 };
        _ = RunAsync(Path.Combine(root, "diagnostics"), scenario, expected, library, dialogs);
    }

    private static async Task RunAsync(string directory, string scenario, int count,
        IPresetLibrary library, IDialogService dialogs)
    {
        var rows = new StringBuilder("Scenario,Stage,Iteration,ElapsedMs,CpuMs,AllocatedBytes,PrivateBytes,WorkingSetBytes,ManagedBytes,GdiHandles,UserHandles,ProcessHandles,Gen2Count\n");
        using var process = Process.GetCurrentProcess();
        var watch = Stopwatch.StartNew();

        (int Gdi, long PrivateBytes) Sample(string stage, int iteration)
        {
            process.Refresh();
            var gdi = GetGuiResources(process.Handle, GrGdiObjects);
            var privateBytes = process.PrivateMemorySize64;
            rows.Append(scenario).Append(',').Append(stage).Append(',').Append(iteration).Append(',')
                .Append(watch.ElapsedMilliseconds).Append(',')
                .Append((long)process.TotalProcessorTime.TotalMilliseconds).Append(',')
                .Append(GC.GetTotalAllocatedBytes(false)).Append(',')
                .Append(privateBytes).Append(',')
                .Append(process.WorkingSet64).Append(',').Append(GC.GetTotalMemory(false)).Append(',')
                .Append(gdi).Append(',')
                .Append(GetGuiResources(process.Handle, GrUserObjects)).Append(',')
                .Append(process.HandleCount).Append(',').Append(GC.CollectionCount(2)).Append('\n');
            return (gdi, privateBytes);
        }

        static void CollectForStress()
        {
            GC.Collect(2, GCCollectionMode.Forced, blocking: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, blocking: true);
        }

        try
        {
            Directory.CreateDirectory(directory);
            var ready = Path.Combine(directory, "stress-ready.txt");
            var start = Path.Combine(directory, "stress-start.signal");
            var done = Path.Combine(directory, "stress-done.txt");
            var result = Path.Combine(directory, $"stress-{scenario}.csv");
            await File.WriteAllTextAsync(ready, scenario, App.ShutdownToken);
            var deadline = DateTime.UtcNow.AddMinutes(2);
            while (!File.Exists(start) && DateTime.UtcNow < deadline)
                await Task.Delay(50, App.ShutdownToken);
            if (!File.Exists(start)) return;

            Sample("before", 0);
            switch (scenario)
            {
                case "settings":
                    for (var i = 1; i <= count; i++)
                    {
                        App.ShutdownToken.ThrowIfCancellationRequested();
                        await dialogs.ShowSettingsWindowAsync();
                        await Application.Current.Dispatcher.InvokeAsync(static () => { },
                            DispatcherPriority.ApplicationIdle, App.ShutdownToken);
                        var window = Application.Current.Windows.OfType<SettingsWindow>().FirstOrDefault();
                        if (window is null) throw new InvalidOperationException("SettingsWindow was not created.");
                        window.Hide();
                        if (i % 50 == 0) Sample("cycle", i);
                    }
                    break;

                case "preset":
                    if (library.Presets.Count < 2)
                        throw new InvalidOperationException("Preset stress needs at least two isolated presets.");
                    for (var i = 1; i <= count; i++)
                    {
                        App.ShutdownToken.ThrowIfCancellationRequested();
                        library.SetActive(library.Presets[i % 2]);
                        if (i % 1000 == 0) Sample("switch", i);
                        await Task.Delay(5, App.ShutdownToken);
                    }
                    break;

                case "preset-multistage":
                    if (library.Presets.Count < 2)
                        throw new InvalidOperationException("Preset stress needs two isolated presets.");
                    var settingsWindow = Application.Current.Windows.OfType<SettingsWindow>().FirstOrDefault();
                    if (settingsWindow is null)
                        throw new InvalidOperationException("The multistage fixture requires a Settings window to hide.");
                    settingsWindow.Hide();
                    await Task.Yield();
                    (int Gdi, long PrivateBytes) warm = default;
                    (int Gdi, long PrivateBytes) final = default;
                    for (var i = 1; i <= count; i++)
                    {
                        App.ShutdownToken.ThrowIfCancellationRequested();
                        library.SetActive(library.Presets[i % 2]);
                        if (i % 100 == 0) await Task.Delay(1, App.ShutdownToken);
                        if (i != 1000 && i != 6000 && i != 11000 && i != 16000) continue;
                        CollectForStress();
                        final = Sample(i == 1000 ? "warmup" : "stage", i);
                        if (i == 1000) warm = final;
                    }
                    var gdiPass = final.Gdi <= warm.Gdi + 2;
                    var privatePass = final.PrivateBytes <= warm.PrivateBytes + 1024 * 1024;
                    await File.WriteAllTextAsync(Path.Combine(directory, "stress-assertions.txt"),
                        $"GDI: {(gdiPass ? "PASS" : "FAIL")} ({warm.Gdi} -> {final.Gdi}, limit +2)\n" +
                        $"PrivateBytes: {(privatePass ? "PASS" : "FAIL")} ({warm.PrivateBytes} -> {final.PrivateBytes}, limit +1048576)\n",
                        App.ShutdownToken);
                    break;

                case "pixel":
                case "pixel-batched":
                    await dialogs.ShowSettingsWindowAsync();
                    var settings = Application.Current.Windows.OfType<SettingsWindow>().FirstOrDefault();
                    if (settings?.DataContext is not SettingsViewModel vm)
                        throw new InvalidOperationException("PixelArt ViewModel was not created.");
                    var editor = vm.PixelArt;
                    editor.GridSize = 1024;
                    var batch = scenario == "pixel-batched" ? new PixelStrokeSegment[1000] : null;
                    for (var i = 1; i <= count; i++)
                    {
                        App.ShutdownToken.ThrowIfCancellationRequested();
                        var x = i % 1024;
                        var y = (i / 1024) % 1024;
                        if (batch is null) editor.DrawLine(x, y, (x + 1) % 1024, y, false);
                        else batch[(i - 1) % batch.Length] = new PixelStrokeSegment(
                            x, y, (x + 1) % 1024, y, editor.StrokeColor(false));
                        if (i % 1000 != 0) continue;
                        if (batch is not null) editor.DrawSegments(batch);
                        editor.FinishStroke();
                        Sample("sample", i);
                        await Application.Current.Dispatcher.InvokeAsync(static () => { },
                            DispatcherPriority.Background, App.ShutdownToken);
                    }
                    settings.Hide();
                    break;
            }

            // Stress-only retention snapshot. Production never forces a collection.
            CollectForStress();
            Sample("after-gc", count);
            await File.WriteAllTextAsync(result, rows.ToString(), App.ShutdownToken);
            await File.WriteAllTextAsync(done, scenario, App.ShutdownToken);
            if (Environment.GetEnvironmentVariable("CROSSGOVERLAY_BENCHMARK_AUTO_EXIT") == "1")
                App.RequestShutdown();
        }
        catch (OperationCanceledException) when (App.IsShuttingDown) { }
        catch (Exception ex)
        {
            try { await File.WriteAllTextAsync(Path.Combine(directory, "stress-error.txt"), ex.ToString()); }
            catch (IOException) { Trace.WriteLine(ex); }
            catch (UnauthorizedAccessException) { Trace.WriteLine(ex); }
        }
    }
}
