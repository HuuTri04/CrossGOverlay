using System.Windows;

namespace CrosshairOverlay.Core.Models;

/// <summary>
/// Thông tin một màn hình vật lý, lấy từ <c>EnumDisplayMonitors</c> + <c>GetDpiForMonitor</c>.
/// </summary>
/// <param name="Handle">HMONITOR.</param>
/// <param name="DeviceName">Vd "\\\\.\\DISPLAY1". Định danh bền để ghim màn hình trong settings.</param>
/// <param name="Bounds">Toàn bộ vùng màn hình, đơn vị PHYSICAL pixel trong virtual desktop.</param>
/// <param name="WorkArea">Vùng trừ taskbar, đơn vị PHYSICAL pixel.</param>
/// <param name="IsPrimary">Có phải màn hình chính không.</param>
/// <param name="DpiScaleX">Hệ số scale ngang (1.0 = 100%, 1.5 = 150%).</param>
/// <param name="DpiScaleY">Hệ số scale dọc.</param>
public readonly record struct MonitorInfo(
    nint Handle,
    string DeviceName,
    Rect Bounds,
    Rect WorkArea,
    bool IsPrimary,
    double DpiScaleX,
    double DpiScaleY)
{
    /// <summary>Tâm hình học của màn hình, đơn vị physical pixel.</summary>
    public Point PhysicalCenter => new(
        Bounds.X + (Bounds.Width / 2d),
        Bounds.Y + (Bounds.Height / 2d));

    /// <summary>Kích thước theo DIP — dùng khi set Width/Height cho <c>Window</c> của WPF.</summary>
    public Size SizeInDips => new(Bounds.Width / DpiScaleX, Bounds.Height / DpiScaleY);
}
