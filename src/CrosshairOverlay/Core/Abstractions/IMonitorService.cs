using CrosshairOverlay.Core.Models;

namespace CrosshairOverlay.Core.Abstractions;

/// <summary>
/// Liệt kê màn hình và giải quyết bài toán DPI / multi-monitor.
/// </summary>
public interface IMonitorService : IDisposable
{
    /// <summary>Danh sách màn hình hiện tại (có cache, tự làm mới khi display config đổi).</summary>
    IReadOnlyList<MonitorInfo> GetMonitors();

    MonitorInfo GetPrimaryMonitor();

    MonitorInfo? GetMonitorFromWindow(nint hwnd);

    MonitorInfo? GetMonitorByDeviceName(string deviceName);

    /// <summary>
    /// Chọn màn hình đích cho overlay theo chế độ người dùng cấu hình.
    /// Luôn trả về một màn hình hợp lệ — fallback về primary nếu lựa chọn đã ghim biến mất.
    /// </summary>
    MonitorInfo ResolveTargetMonitor(
        MonitorSelectionMode mode,
        string? targetDeviceName,
        nint foregroundWindow);

    /// <summary>
    /// Phát khi độ phân giải, số lượng màn hình hoặc DPI scaling thay đổi
    /// (WM_DISPLAYCHANGE / WM_DPICHANGED). Overlay phải canh lại vị trí khi nhận sự kiện này.
    /// </summary>
    event EventHandler? DisplayConfigurationChanged;
}

/// <summary>Chế độ hiển thị đang chạy của một màn hình.</summary>
/// <param name="RefreshRate">Tần số quét (Hz); 0 nếu driver không báo.</param>
public sealed record DisplayMode(int Width, int Height, int RefreshRate);

/// <summary>Đọc độ phân giải và tần số quét hiện tại của một màn hình.</summary>
public interface IDisplayModeReader
{
    /// <param name="deviceName">Vd "\\.\DISPLAY1". Null nếu không đọc được.</param>
    DisplayMode? Read(string? deviceName);
}
