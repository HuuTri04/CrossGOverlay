namespace CrosshairOverlay.Core.Abstractions;

/// <param name="Version">Phiên bản mới trên máy chủ.</param>
/// <param name="DownloadUrl">Liên kết tải file .exe.</param>
/// <param name="ReleaseUrl">Trang phát hành, để người dùng tự tải nếu tự động thất bại.</param>
public sealed record UpdateInfo(Version Version, string DownloadUrl, string ReleaseUrl);

/// <summary>
/// Kiểm tra và cài bản cập nhật.
/// </summary>
/// <remarks>
/// Mọi thao tác đều bất đồng bộ và có timeout. Không có mạng, máy chủ chậm, hay chưa cấu hình
/// kho phát hành đều phải dẫn tới "không có bản mới" một cách lặng lẽ — người dùng đang muốn
/// vào game, không phải chờ một lần gọi HTTP.
/// </remarks>
public interface IUpdateService
{
    Version CurrentVersion { get; }

    /// <summary>Trả về null khi đã ở bản mới nhất, hoặc khi không kiểm tra được vì bất kỳ lý do gì.</summary>
    Task<UpdateInfo?> CheckAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Tải bản mới rồi bàn giao cho một script thay thế file và khởi động lại ứng dụng.
    /// Trả về true khi script đã chạy — khi đó ứng dụng phải tự thoát ngay.
    /// </summary>
    Task<bool> DownloadAndApplyAsync(UpdateInfo update, CancellationToken cancellationToken = default);
}
