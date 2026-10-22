using System.Diagnostics;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CrosshairOverlay.Services.System;

/// <summary>Samples this process only while the System Info panel is visible.</summary>
public sealed partial class PerformanceMonitorService : ObservableObject, IDisposable
{
    private readonly global::System.Diagnostics.Process _process = global::System.Diagnostics.Process.GetCurrentProcess();
    private readonly DispatcherTimer _timer;
    private TimeSpan _lastCpuTime;
    private long _lastTimestamp;
    private bool _disposed;

    [ObservableProperty] private string _cpuValue = "…";
    [ObservableProperty] private string _memoryValue = "…";

    public PerformanceMonitorService(Dispatcher dispatcher)
    {
        _timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
        {
            Interval = TimeSpan.FromSeconds(3),
        };
        _timer.Tick += OnTick;
    }

    public void Start()
    {
        if (_disposed || _timer.IsEnabled) return;
        Sample();
        _timer.Start();
    }

    public void Stop()
    {
        _timer.Stop();
        _lastTimestamp = 0;
        CpuValue = "…";
    }

    private void OnTick(object? sender, EventArgs e) => Sample();

    private void Sample()
    {
        try
        {
            _process.Refresh();
            var now = Stopwatch.GetTimestamp();
            var cpuTime = _process.TotalProcessorTime;
            MemoryValue = $"{_process.WorkingSet64 / (1024d * 1024d):0} MB";

            if (_lastTimestamp != 0)
            {
                var elapsed = Stopwatch.GetElapsedTime(_lastTimestamp, now).TotalSeconds;
                if (elapsed > 0)
                {
                    var usage = (cpuTime - _lastCpuTime).TotalSeconds / elapsed
                                / Environment.ProcessorCount * 100d;
                    CpuValue = $"{Math.Clamp(usage, 0d, 100d):0.0}%";
                }
            }

            _lastCpuTime = cpuTime;
            _lastTimestamp = now;
        }
        catch (Exception ex) when (ex is InvalidOperationException or global::System.ComponentModel.Win32Exception)
        {
            Stop();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        _timer.Tick -= OnTick;
        _process.Dispose();
    }
}
