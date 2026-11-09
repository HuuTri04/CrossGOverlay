using System.IO;
using System.Runtime;
using System.Windows;

namespace CrosshairOverlay;

public partial class App : Application
{
    private static readonly CancellationTokenSource ShutdownCancellation = new();
    private readonly long _constructedAt = global::System.Diagnostics.Stopwatch.GetTimestamp();
    private readonly double _runtimeStartupMs = MeasureRuntimeStartup();
    private AppBootstrapper? _bootstrapper;

    public App()
    {
        try
        {
            var benchmarkRoot = Environment.GetEnvironmentVariable("CROSSGOVERLAY_BENCHMARK_ROOT");
            var root = string.IsNullOrWhiteSpace(benchmarkRoot)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "CrosshairOverlay", "cache")
                : Path.Combine(Path.GetFullPath(benchmarkRoot), "cache");
            Directory.CreateDirectory(root);
            ProfileOptimization.SetProfileRoot(root);
            ProfileOptimization.StartProfile("Startup.profile");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // The profile only helps subsequent launches; a read-only profile location
            // must not break the first launch.
            global::System.Diagnostics.Trace.TraceWarning($"JIT startup profile unavailable: {ex.Message}");
        }
    }

    public static bool IsShuttingDown { get; private set; }
    public static CancellationToken ShutdownToken => ShutdownCancellation.Token;

    protected override void OnStartup(StartupEventArgs e)
    {
        var bootstrapper = _bootstrapper = new AppBootstrapper(this, _constructedAt, _runtimeStartupMs);
        bootstrapper.Run(e);
    }

    public static void RequestShutdown(int exitCode = 0)
    {
        if (IsShuttingDown) return;
        IsShuttingDown = true;
        ShutdownCancellation.Cancel();
        if (Current is not App app) return;

        if (!app.Dispatcher.CheckAccess())
        {
            app.Dispatcher.BeginInvoke(() => app.RequestShutdownCore(exitCode));
            return;
        }

        app.RequestShutdownCore(exitCode);
    }

    private async void RequestShutdownCore(int exitCode)
    {
        try
        {
            if (_bootstrapper is not null)
                await _bootstrapper.ShutdownAsync(closeApplication: false);
        }
        catch (Exception ex)
        {
            global::System.Diagnostics.Trace.TraceError($"Shutdown failed: {ex}");
        }
        finally { Shutdown(exitCode); }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        IsShuttingDown = true;
        ShutdownCancellation.Cancel();
        _bootstrapper?.Shutdown(closeApplication: false);
        base.OnExit(e);
    }

    private static double MeasureRuntimeStartup()
    {
        try
        {
            using var process = global::System.Diagnostics.Process.GetCurrentProcess();
            return (DateTime.Now - process.StartTime).TotalMilliseconds;
        }
        catch (Exception)
        {
            return -1;
        }
    }
}
