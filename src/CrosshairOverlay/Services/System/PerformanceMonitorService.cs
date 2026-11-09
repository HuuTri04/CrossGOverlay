using System.Diagnostics;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CrosshairOverlay.Services.System;

/// <summary>Samples this process only while the System Info panel is visible.</summary>
public sealed partial class PerformanceMonitorService : ObservableObject, IDisposable
{
    private readonly Dispatcher _dispatcher;
    private CancellationTokenSource? _samplingCancellation;
    private int _generation;
    private bool _disposed;

    [ObservableProperty] private string _cpuValue = "…";
    [ObservableProperty] private string _memoryValue = "…";

    public PerformanceMonitorService(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public void Start()
    {
        if (_disposed || _samplingCancellation is not null) return;
        var cancellation = new CancellationTokenSource();
        _samplingCancellation = cancellation;
        var generation = ++_generation;
        _ = Task.Run(() => SampleLoopAsync(generation, cancellation));
    }

    public void Stop()
    {
        if (_samplingCancellation is null) return;
        ++_generation;
        try { _samplingCancellation.Cancel(); }
        catch (ObjectDisposedException) { } // A failed worker may have already exited.
        _samplingCancellation = null;
        CpuValue = "…";
    }

    private async Task SampleLoopAsync(int generation, CancellationTokenSource cancellation)
    {
        try
        {
            using var process = global::System.Diagnostics.Process.GetCurrentProcess();
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(3));
            var lastTimestamp = 0L;
            var lastCpuTime = TimeSpan.Zero;
            do
            {
                cancellation.Token.ThrowIfCancellationRequested();
                process.Refresh();
                var now = Stopwatch.GetTimestamp();
                var cpuTime = process.TotalProcessorTime;
                var memory = $"{process.WorkingSet64 / (1024d * 1024d):0} MB";
                string? cpu = null;
                if (lastTimestamp != 0)
                {
                    var elapsed = Stopwatch.GetElapsedTime(lastTimestamp, now).TotalSeconds;
                    if (elapsed > 0)
                    {
                        var usage = (cpuTime - lastCpuTime).TotalSeconds / elapsed
                                    / Environment.ProcessorCount * 100d;
                        cpu = $"{Math.Clamp(usage, 0d, 100d):0.0}%";
                    }
                }
                lastCpuTime = cpuTime;
                lastTimestamp = now;

                PostToUi(() =>
                {
                    if (_disposed || generation != _generation) return;
                    MemoryValue = memory;
                    if (cpu is not null) CpuValue = cpu;
                });
            }
            while (await timer.WaitForNextTickAsync(cancellation.Token).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception ex) when (ex is InvalidOperationException or global::System.ComponentModel.Win32Exception
                                   or TaskCanceledException)
        {
            Trace.WriteLine($"[PerformanceMonitor] Sampling stopped: {ex}");
            PostToUi(() =>
            {
                if (generation == _generation) Stop();
            });
        }
        finally { cancellation.Dispose(); }
    }

    private void PostToUi(Action update)
    {
        if (_dispatcher.HasShutdownStarted) return;
        try { _ = _dispatcher.BeginInvoke(DispatcherPriority.Background, update); }
        catch (InvalidOperationException) { } // The dispatcher may shut down after the check.
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }
}
