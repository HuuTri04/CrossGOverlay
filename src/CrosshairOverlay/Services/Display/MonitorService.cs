using System.Windows;
using System.Windows.Threading;
using CrosshairOverlay.Core.Abstractions;
using CrosshairOverlay.Core.Models;
using CrosshairOverlay.Interop;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace CrosshairOverlay.Services.Display;

/// <inheritdoc cref="IMonitorService"/>
public sealed class MonitorService : IMonitorService
{
    private readonly ILogger<MonitorService> _logger;
    private readonly Dispatcher _dispatcher;
    private readonly object _gate = new();

    private IReadOnlyList<MonitorInfo>? _cache;
    private bool _disposed;

    public MonitorService(ILogger<MonitorService> logger)
    {
        _logger = logger;
        _dispatcher = Dispatcher.CurrentDispatcher;

        // DisplaySettingsChanged bao trùm cả đổi độ phân giải, cắm/rút màn hình và đổi DPI scaling.
        // SystemEvents tự dựng một cửa sổ ẩn top-level để nhận WM_DISPLAYCHANGE — cửa sổ
        // message-only (HWND_MESSAGE) sẽ KHÔNG nhận được message broadcast này.
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    public event EventHandler? DisplayConfigurationChanged;

    public IReadOnlyList<MonitorInfo> GetMonitors()
    {
        lock (_gate)
        {
            return _cache ??= Enumerate();
        }
    }

    public MonitorInfo GetPrimaryMonitor()
    {
        var monitors = GetMonitors();
        foreach (var monitor in monitors)
        {
            if (monitor.IsPrimary) return monitor;
        }

        // Không bao giờ nên xảy ra, nhưng thà trả về màn hình đầu tiên còn hơn ném exception
        // trong luồng khởi động overlay.
        return monitors.Count > 0 ? monitors[0] : CreateFallbackMonitor();
    }

    public MonitorInfo? GetMonitorFromWindow(nint hwnd)
    {
        if (hwnd == 0) return null;

        var handle = NativeMethods.MonitorFromWindow(hwnd, Win32Constants.MONITOR_DEFAULTTONEAREST);
        if (handle == 0) return null;

        foreach (var monitor in GetMonitors())
        {
            if (monitor.Handle == handle) return monitor;
        }

        // HMONITOR lạ so với cache — display config vừa đổi mà ta chưa kịp biết.
        Invalidate();
        foreach (var monitor in GetMonitors())
        {
            if (monitor.Handle == handle) return monitor;
        }

        return null;
    }

    public MonitorInfo? GetMonitorByDeviceName(string deviceName)
    {
        if (string.IsNullOrWhiteSpace(deviceName)) return null;

        foreach (var monitor in GetMonitors())
        {
            if (string.Equals(monitor.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase))
                return monitor;
        }

        return null;
    }

    public MonitorInfo ResolveTargetMonitor(
        MonitorSelectionMode mode,
        string? targetDeviceName,
        nint foregroundWindow)
    {
        switch (mode)
        {
            case MonitorSelectionMode.Primary:
                return GetPrimaryMonitor();

            case MonitorSelectionMode.Specific:
                var pinned = targetDeviceName is null ? null : GetMonitorByDeviceName(targetDeviceName);
                if (pinned is { } found) return found;

                _logger.LogWarning(
                    "Không tìm thấy màn hình đã ghim '{DeviceName}', chuyển về màn hình chính.",
                    targetDeviceName);
                return GetPrimaryMonitor();

            case MonitorSelectionMode.FollowForegroundWindow:
            default:
                var hwnd = foregroundWindow != 0 ? foregroundWindow : NativeMethods.GetForegroundWindow();
                return GetMonitorFromWindow(hwnd) ?? GetPrimaryMonitor();
        }
    }

    /// <summary>Xoá cache để lần đọc sau enumerate lại từ hệ thống.</summary>
    public void Invalidate()
    {
        lock (_gate)
        {
            _cache = null;
        }
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        Invalidate();

        // SystemEvents bắn trên thread riêng của nó; đưa về UI thread vì handler sẽ
        // đụng tới cửa sổ overlay.
        _dispatcher.BeginInvoke(() =>
        {
            _logger.LogInformation(
                "Cấu hình màn hình thay đổi, hiện có {Count} màn hình.", GetMonitors().Count);
            DisplayConfigurationChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    private List<MonitorInfo> Enumerate()
    {
        var results = new List<MonitorInfo>(2);

        bool Callback(nint hMonitor, nint hdc, ref RECT rect, nint data)
        {
            var info = MONITORINFOEXW.Create();
            if (!NativeMethods.GetMonitorInfo(hMonitor, ref info))
            {
                _logger.LogWarning("GetMonitorInfo thất bại cho HMONITOR {Handle:X}.", hMonitor);
                return true; // bỏ qua màn hình này, tiếp tục enumerate
            }

            var (scaleX, scaleY) = GetDpiScale(hMonitor);

            results.Add(new MonitorInfo(
                Handle: hMonitor,
                DeviceName: info.szDevice,
                Bounds: info.rcMonitor.ToRect(),
                WorkArea: info.rcWork.ToRect(),
                IsPrimary: (info.dwFlags & Win32Constants.MONITORINFOF_PRIMARY) != 0,
                DpiScaleX: scaleX,
                DpiScaleY: scaleY));

            return true;
        }

        if (!NativeMethods.EnumDisplayMonitors(0, 0, Callback, 0) || results.Count == 0)
        {
            _logger.LogError("EnumDisplayMonitors không trả về màn hình nào, dùng giá trị dự phòng.");
            results.Add(CreateFallbackMonitor());
        }

        return results;
    }

    private (double X, double Y) GetDpiScale(nint hMonitor)
    {
        // GetDpiForMonitor có từ Win8.1. Nếu thất bại thì coi như 100%.
        if (NativeMethods.GetDpiForMonitor(hMonitor, MonitorDpiType.EffectiveDpi, out var dpiX, out var dpiY) == 0
            && dpiX > 0 && dpiY > 0)
        {
            return (dpiX / Win32Constants.DefaultDpi, dpiY / Win32Constants.DefaultDpi);
        }

        _logger.LogDebug("GetDpiForMonitor thất bại cho HMONITOR {Handle:X}, mặc định 100%.", hMonitor);
        return (1d, 1d);
    }

    /// <summary>Dùng khi API enumerate thất bại hoàn toàn — vẫn cho app chạy tiếp.</summary>
    private static MonitorInfo CreateFallbackMonitor()
    {
        var width = SystemParameters.PrimaryScreenWidth;
        var height = SystemParameters.PrimaryScreenHeight;
        var bounds = new Rect(0, 0, width, height);
        return new MonitorInfo(0, @"\\.\DISPLAY1", bounds, bounds, true, 1d, 1d);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
    }
}
