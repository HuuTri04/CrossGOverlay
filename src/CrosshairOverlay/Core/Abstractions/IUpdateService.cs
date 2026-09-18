namespace CrosshairOverlay.Core.Abstractions;

/// <param name="Version">Phiên bản mới trên máy chủ.</param>
/// <param name="DownloadUrl">Liên kết tải gói .zip chứa cả thư mục ứng dụng.</param>
/// <param name="ReleaseUrl">Trang phát hành, để người dùng tự tải nếu tự động thất bại.</param>
/// <param name="Changelog">Phần mô tả của bản phát hành (khoá <c>body</c>); rỗng nếu người phát hành không ghi gì.</param>
public sealed record UpdateInfo(Version Version, string DownloadUrl, string ReleaseUrl, string Changelog = "");

/// <summary>Kết quả một lần kiểm tra cập nhật.</summary>
public enum UpdateCheckStatus
{
    /// <summary>Đang dùng bản mới nhất.</summary>
    UpToDate,

    /// <summary>Có bản mới hơn kèm gói tải được.</summary>
    UpdateAvailable,

    /// <summary>Không hỏi được máy chủ (mất mạng, timeout, GitHub trả lỗi, JSON lạ).</summary>
    Failed,
}

/// <param name="Update">Chỉ khác null khi <paramref name="Status"/> là <see cref="UpdateCheckStatus.UpdateAvailable"/>.</param>
public sealed record UpdateCheck(UpdateCheckStatus Status, UpdateInfo? Update = null);

/// <summary>
/// Kiểm tra và cài bản cập nhật từ GitHub Releases.
/// </summary>
/// <remarks>
/// Mọi thao tác đều bất đồng bộ và có timeout. Không có mạng, máy chủ chậm, hay chưa cấu hình
/// kho phát hành đều phải dẫn tới <see cref="UpdateCheckStatus.Failed"/> một cách lặng lẽ — người dùng đang
/// muốn vào game, không phải chờ một lần gọi HTTP.
/// </remarks>
public interface IUpdateService
{
    Version CurrentVersion { get; }

    /// <summary>Hỏi GitHub bản phát hành mới nhất. Không bao giờ ném lỗi.</summary>
    Task<UpdateCheck> CheckAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Tải gói .zip, giải nén, rồi bàn giao cho một script chép đè thư mục cài đặt và khởi động lại ứng dụng.
    /// Trả về true khi script đã chạy — khi đó ứng dụng phải tự thoát ngay.
    /// </summary>
    /// <param name="progress">Phần đã tải, 0..1. Gọi trên luồng nền — nơi nhận phải tự về luồng giao diện.</param>
    Task<bool> DownloadAndApplyAsync(
        UpdateInfo update, IProgress<double>? progress = null, CancellationToken cancellationToken = default);
}
