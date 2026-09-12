using CrosshairOverlay.Core.Models;

namespace CrosshairOverlay.Core.Abstractions;

/// <summary>
/// Đọc/ghi <see cref="AppSettings"/>. Instance <see cref="Current"/> là singleton sống suốt
/// phiên chạy nên UI có thể bind trực tiếp hai chiều vào nó.
/// </summary>
public interface IAppSettingsService : IDisposable
{
    AppSettings Current { get; }

    /// <summary>Nạp từ đĩa. File thiếu hoặc hỏng → dùng mặc định và ghi log cảnh báo.</summary>
    Task LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(CancellationToken cancellationToken = default);

    /// <summary>Đặt lịch lưu có debounce — dùng khi slider/toggle bắn thay đổi liên tục.</summary>
    void RequestSave();

    /// <summary>Ghi ngay phần còn treo trong hàng đợi debounce. Gọi trước khi thoát app.</summary>
    Task FlushAsync(CancellationToken cancellationToken = default);
}
