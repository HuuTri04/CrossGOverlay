using CrosshairOverlay.Core.Models;

namespace CrosshairOverlay.Core.Abstractions;

/// <summary>
/// Điều khiển vòng đời cửa sổ overlay: tạo, hiện/ẩn, đổi preset, tái định vị theo màn hình.
/// Phần còn lại của app chỉ nói chuyện với overlay qua interface này.
/// </summary>
public interface IOverlayController : IDisposable
{
    bool IsVisible { get; }

    /// <summary>Preset đang được vẽ, null nếu chưa khởi tạo.</summary>
    CrosshairProfile? CurrentProfile { get; }

    /// <summary>Màn hình overlay đang bám vào.</summary>
    MonitorInfo? CurrentMonitor { get; }

    /// <summary>Tạo cửa sổ overlay và áp mọi window style cần thiết. Gọi một lần lúc khởi động.</summary>
    void Initialize();

    /// <summary>
    /// Đổi preset đang vẽ. Controller tự hủy đăng ký khỏi preset cũ và lắng nghe preset mới
    /// để redraw khi thuộc tính thay đổi.
    /// </summary>
    void SetProfile(CrosshairProfile profile);

    void SetVisible(bool visible);

    void Toggle();

    /// <summary>Đặt overlay lên màn hình chỉ định và canh lại tâm.</summary>
    void MoveToMonitor(MonitorInfo monitor);

    /// <summary>
    /// Cấu hình cách chọn màn hình đích. Tách khỏi <see cref="IAppSettingsService"/> để
    /// overlay không phụ thuộc vào tầng lưu trữ — shell là nơi truyền giá trị xuống.
    /// </summary>
    void SetMonitorSelection(MonitorSelectionMode mode, string? targetDeviceName);

    /// <summary>Ép vẽ lại. Dùng sau khi đổi DPI/độ phân giải hoặc khi preset bị thay toàn bộ.</summary>
    void Invalidate();

    /// <summary>
    /// Ẩn/hiện hình TẠM THỜI (vd đang giữ chuột phải để ngắm) mà không đổi trạng thái hiện/ẩn của
    /// overlay.
    /// </summary>
    /// <remarks>
    /// Rẻ hơn <see cref="SetVisible"/> rất nhiều: không Show/Hide cửa sổ, không dựng lại hình, không
    /// khởi động lại GIF — chỉ bỏ độ mờ về 0. Cần vậy vì người chơi có thể bấm chuột phải nhiều lần
    /// mỗi giây.
    /// </remarks>
    void SetSuppressed(bool suppressed);

    /// <summary>
    /// Giới hạn tần số dựng lại hình khi chỉnh preset và tần số đổi khung hình GIF.
    /// </summary>
    /// <param name="framesPerSecond">60, 120, 144…; 0 hoặc âm là không giới hạn.</param>
    void SetFrameRateLimit(int framesPerSecond);

    event EventHandler<OverlayVisibilityChangedEventArgs>? VisibilityChanged;
}

public sealed class OverlayVisibilityChangedEventArgs(bool isVisible) : EventArgs
{
    public bool IsVisible { get; } = isVisible;
}
