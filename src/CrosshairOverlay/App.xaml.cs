using System.Windows;

namespace CrosshairOverlay;

public partial class App : Application
{
    private readonly long _constructedAt = global::System.Diagnostics.Stopwatch.GetTimestamp();
    private readonly double _runtimeStartupMs = MeasureRuntimeStartup();
    private AppBootstrapper? _bootstrapper;

    public static bool IsShuttingDown { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        var bootstrapper = _bootstrapper = new AppBootstrapper(this, _constructedAt, _runtimeStartupMs);
        bootstrapper.Run(e);
    }

    public static void RequestShutdown(int exitCode = 0)
    {
        if (IsShuttingDown) return;
        IsShuttingDown = true;
        if (Current is not App app) return;

        if (!app.Dispatcher.CheckAccess())
        {
            app.Dispatcher.BeginInvoke(() => app.RequestShutdownCore(exitCode));
            return;
        }

        app.RequestShutdownCore(exitCode);
    }

    private void RequestShutdownCore(int exitCode)
    {
        _bootstrapper?.Shutdown(closeApplication: false);
        Shutdown(exitCode);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        IsShuttingDown = true;
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
