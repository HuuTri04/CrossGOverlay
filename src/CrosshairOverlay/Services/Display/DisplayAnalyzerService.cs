using System.Windows;
using CrosshairOverlay.Core.Abstractions;
using CrosshairOverlay.Core.Models;
using CrosshairOverlay.Interop;

namespace CrosshairOverlay.Services.Display;

/// <summary>Reads display characteristics only when explicitly requested by the user.</summary>
public sealed class DisplayAnalyzerService(IMonitorService monitors, IDisplayModeReader displayModes)
{
    public DisplayRecommendation AnalyzeDisplayAndGetRecommendations(
        MonitorSelectionMode selectionMode, string? targetDeviceName)
    {
        var foreground = NativeMethods.GetForegroundWindow();
        var monitor = monitors.ResolveTargetMonitor(selectionMode, targetDeviceName, foreground);
        var mode = displayModes.Read(monitor.DeviceName);

        // EnumDisplaySettings gives physical resolution and active refresh rate. Bounds are
        // already physical pixels, so they remain a safe fallback when a driver omits a mode.
        var width = mode?.Width ?? (int)monitor.Bounds.Width;
        var height = mode?.Height ?? (int)monitor.Bounds.Height;
        var refreshRate = mode?.RefreshRate ?? 0;

        var dpiScale = monitor.DpiScaleX;
        if (foreground != 0 && monitors.GetMonitorFromWindow(foreground)?.Handle == monitor.Handle)
        {
            var windowDpi = NativeMethods.GetDpiForWindow(foreground);
            // An unaware foreground window can report system DPI instead of this monitor's
            // effective DPI. Keep the monitor reading in that case.
            if (windowDpi > 0) dpiScale = Math.Max(dpiScale, windowDpi / 96d);
        }

        var (fps, scale) = Recommend(width, refreshRate, dpiScale);

        return new DisplayRecommendation(
            monitor, width, height, refreshRate,
            fps,
            scale, monitor.PhysicalCenter);
    }

    internal static (int Fps, double Scale) Recommend(int width, int refreshRate, double dpiScale) =>
        (refreshRate >= 144 ? 144 : 60,
         width >= 3840 || dpiScale > 1.5d ? 1.5d
             : width >= 2560 || dpiScale > 1.2d ? 1.25d
             : 1d);
}

public sealed record DisplayRecommendation(
    MonitorInfo Monitor,
    int Width,
    int Height,
    int RefreshRate,
    int RecommendedFps,
    double RecommendedScale,
    Point PhysicalCenter)
{
    public string DisplayLabel => Width >= 3840 ? "4K"
        : Width >= 2560 ? "2K"
        : Width >= 1920 ? "Full HD"
        : $"{Width}×{Height}";
}
