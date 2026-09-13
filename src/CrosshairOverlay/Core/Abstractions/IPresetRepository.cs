using CrosshairOverlay.Core.Models;

namespace CrosshairOverlay.Core.Abstractions;

/// <summary>
/// Lưu trữ preset crosshair: mỗi preset là một file <c>.json</c> trong thư mục presets.
/// Một file / một preset giúp import-export chỉ là thao tác copy file.
/// </summary>
public interface IPresetRepository
{
    /// <summary>
    /// Nạp toàn bộ preset. File hỏng được bỏ qua và ghi log thay vì làm sập app;
    /// nếu không còn preset nào hợp lệ, trả về preset mặc định.
    /// </summary>
    Task<IReadOnlyList<CrosshairProfile>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<CrosshairProfile?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Ghi preset (atomic: ghi file tạm rồi replace, tránh mất dữ liệu khi mất điện).</summary>
    Task SaveAsync(CrosshairProfile profile, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Đọc file JSON bên ngoài, cấp Id mới để không ghi đè preset sẵn có.</summary>
    Task<CrosshairProfile> ImportAsync(string filePath, CancellationToken cancellationToken = default);

    Task ExportAsync(CrosshairProfile profile, string filePath, CancellationToken cancellationToken = default);

    /// <summary>Phát khi tập preset thay đổi để UI và tray menu đồng bộ lại.</summary>
    event EventHandler? PresetsChanged;

    /// <summary>
    /// Lần nạp gần nhất có file preset bị bỏ qua vì hỏng. Khi đó KHÔNG được dọn kho ảnh: không
    /// biết file hỏng kia có đang dùng ảnh nào không.
    /// </summary>
    bool LastLoadSkippedFiles { get; }
}
