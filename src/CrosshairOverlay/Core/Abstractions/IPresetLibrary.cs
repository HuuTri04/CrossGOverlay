using System.Collections.ObjectModel;
using CrosshairOverlay.Core.Models;

namespace CrosshairOverlay.Core.Abstractions;

/// <summary>
/// Thư viện preset đang sống trong bộ nhớ — nguồn chân lý duy nhất về các instance
/// <see cref="CrosshairProfile"/>.
/// </summary>
/// <remarks>
/// <see cref="IPresetRepository"/> chỉ lo đọc/ghi đĩa và trả về instance MỚI sau mỗi lần đọc.
/// Nếu editor bind vào một instance còn overlay giữ một instance khác thì kéo slider sẽ không
/// ra preview. Lớp này giữ đúng một tập instance cho cả ứng dụng, đồng thời tự lưu xuống đĩa
/// (có debounce) khi người dùng chỉnh sửa.
/// </remarks>
public interface IPresetLibrary : IDisposable
{
    ReadOnlyObservableCollection<CrosshairProfile> Presets { get; }

    /// <summary>Preset đang được overlay vẽ. Null trước khi <see cref="InitializeAsync"/> chạy xong.</summary>
    CrosshairProfile? Active { get; }

    /// <summary>Nạp thư viện từ đĩa và chọn preset active theo Id đã lưu trong settings.</summary>
    /// <param name="order">
    /// Thứ tự đã lưu (Id). Preset không có trong đó xếp sau, theo tên. null/rỗng là xếp hết theo tên.
    /// </param>
    Task InitializeAsync(
        Guid preferredActiveId, IReadOnlyList<Guid>? order = null, CancellationToken cancellationToken = default);

    void SetActive(CrosshairProfile profile);

    /// <summary>
    /// Chuyển sang preset kế tiếp theo thứ tự danh sách, có quay vòng.
    /// <paramref name="direction"/> là +1 hoặc -1. Dùng cho hotkey Next/Previous ở Phase 5.
    /// </summary>
    void StepActive(int direction);

    Task<CrosshairProfile> CreateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Thêm một preset đã dựng sẵn (vd từ mã crosshair của game) vào thư viện và ghi xuống đĩa.
    /// </summary>
    Task<CrosshairProfile> AddAsync(
        CrosshairProfile profile, CancellationToken cancellationToken = default);

    Task<CrosshairProfile> DuplicateAsync(
        CrosshairProfile source, CancellationToken cancellationToken = default);

    /// <summary>
    /// Chuyển preset tới vị trí <paramref name="newIndex"/> (kéo thả trong danh sách). Next/Previous preset
    /// đi theo thứ tự mới ngay. Không đổi preset đang dùng.
    /// </summary>
    /// <returns>false nếu preset không thuộc thư viện, chỉ số sai, hoặc đã ở đúng chỗ.</returns>
    bool Move(CrosshairProfile profile, int newIndex);

    /// <summary>Xoá preset. Không bao giờ để thư viện rỗng — xoá cái cuối sẽ sinh lại mặc định.</summary>
    Task DeleteAsync(CrosshairProfile profile, CancellationToken cancellationToken = default);

    /// <summary>Ghi ngay mọi preset còn treo trong hàng đợi debounce. Gọi trước khi thoát app.</summary>
    Task FlushAsync(CancellationToken cancellationToken = default);

    event EventHandler? ActiveChanged;

    /// <summary>
    /// Thứ tự danh sách vừa đổi: kéo thả, hoặc thêm/nhân bản/xoá preset. Nơi lưu cài đặt nghe sự kiện này
    /// để ghi lại thứ tự.
    /// </summary>
    event EventHandler? OrderChanged;
}
