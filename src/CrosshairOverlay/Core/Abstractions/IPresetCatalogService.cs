using CrosshairOverlay.Core.Models;

namespace CrosshairOverlay.Core.Abstractions;

/// <summary>
/// Thư viện mẫu tâm ngắm dựng sẵn (500+ mẫu, file <c>Data/builtin_presets.json</c> cạnh file chạy).
/// </summary>
/// <remarks>
/// Đọc LƯỜI: khởi động app không chạm tới file này. Lần đầu người dùng mở cửa sổ Thư viện mới đọc (trên luồng
/// nền), sau đó giữ trong RAM cho cả phiên.
/// </remarks>
public interface IPresetCatalogService
{
    /// <summary>Đã đọc xong file (thành công hay rỗng).</summary>
    bool IsLoaded { get; }

    /// <summary>
    /// Đọc file nếu chưa đọc; gọi lại chỉ trả về bản đã cache. Không ném lỗi khi thiếu hay hỏng file — trả về
    /// danh sách rỗng và ghi log.
    /// </summary>
    Task<IReadOnlyList<CatalogPreset>> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>Khoá danh mục theo thứ tự hiển thị, bắt đầu bằng <see cref="CatalogCategories.All"/>.</summary>
    IReadOnlyList<string> GetCategories();

    /// <summary>Số mẫu trong một danh mục (<see cref="CatalogCategories.All"/> = tất cả). 0 khi chưa đọc.</summary>
    int CountIn(string category);

    /// <summary>
    /// Lọc tức thì trong RAM: mọi từ khoá đều phải khớp (không phân biệt hoa thường, không dấu — "cham" khớp
    /// "Chấm"), và thuộc danh mục đã chọn. Tìm được cả theo tên tiếng Việt của kiểu dáng ("chữ thập", "vòng tròn").
    /// Chưa đọc file thì trả về rỗng.
    /// </summary>
    IReadOnlyList<CatalogPreset> FilterPresets(string? searchText, string? category);
}
