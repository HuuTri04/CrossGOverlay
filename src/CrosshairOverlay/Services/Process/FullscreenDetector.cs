using System.Windows;
using CrosshairOverlay.Core.Models;
using CrosshairOverlay.Interop;

namespace CrosshairOverlay.Services.Process;

/// <summary>
/// Phán đoán cửa sổ foreground đang ở chế độ hiển thị nào.
/// </summary>
/// <remarks>
/// Quan trọng với yêu cầu "không cố bypass Exclusive Fullscreen": không overlay usermode nào
/// vẽ được lên trên chế độ đó, nên việc duy nhất đúng đắn là NHẬN RA nó và báo cho người dùng.
///
/// <para>
/// <c>SHQueryUserNotificationState</c> là API công khai duy nhất cho biết có ứng dụng Direct3D
/// nào đang chạy toàn màn hình độc quyền hay không. Nó chỉ đọc trạng thái shell, không đụng
/// tới tiến trình game. Vì là tín hiệu toàn hệ thống chứ không gắn với một cửa sổ cụ thể, kết
/// quả được đặt tên <see cref="FullscreenKind.LikelyExclusive"/> — "nhiều khả năng", không
/// phải khẳng định.
/// </para>
/// </remarks>
internal static class FullscreenDetector
{
    /// <summary>Sai số cho phép khi so cửa sổ với màn hình, tính bằng physical pixel.</summary>
    private const double CoverageTolerance = 2d;

    public static FullscreenKind Detect(nint hwnd, Rect windowBounds, Rect monitorBounds)
    {
        if (hwnd == 0 || windowBounds.IsEmpty || monitorBounds.IsEmpty) return FullscreenKind.None;

        if (!CoversMonitor(windowBounds, monitorBounds)) return FullscreenKind.None;

        // Cửa sổ phủ kín màn hình nhưng vẫn còn thanh tiêu đề hay viền kéo giãn thì chỉ là
        // cửa sổ được phóng to hết cỡ, không phải fullscreen.
        var style = (long)NativeMethods.GetWindowLongPtr(hwnd, Win32Constants.GWL_STYLE);
        var hasChrome = (style & Win32Constants.WS_CAPTION) != 0
                        || (style & Win32Constants.WS_THICKFRAME) != 0;

        if (hasChrome) return FullscreenKind.None;

        return IsD3dExclusiveRunning() ? FullscreenKind.LikelyExclusive : FullscreenKind.Borderless;
    }

    private static bool CoversMonitor(Rect window, Rect monitor) =>
        window.Left <= monitor.Left + CoverageTolerance
        && window.Top <= monitor.Top + CoverageTolerance
        && window.Right >= monitor.Right - CoverageTolerance
        && window.Bottom >= monitor.Bottom - CoverageTolerance;

    private static bool IsD3dExclusiveRunning()
    {
        // HRESULT S_OK == 0. Thất bại thì coi như không có gì đặc biệt — thà bỏ sót cảnh báo
        // còn hơn quấy rầy người dùng bằng thông báo sai.
        return NativeMethods.SHQueryUserNotificationState(out var state) == 0
               && state == UserNotificationState.RunningD3dFullScreen;
    }
}
