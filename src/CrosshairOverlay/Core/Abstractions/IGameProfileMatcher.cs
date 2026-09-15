using CrosshairOverlay.Core.Models;

namespace CrosshairOverlay.Core.Abstractions;

/// <summary>
/// So khớp cửa sổ foreground với danh sách <see cref="GameProfile"/>. Logic thuần, dễ unit-test.
/// </summary>
public interface IGameProfileMatcher
{
    /// <summary>
    /// Trả về rule khớp có <see cref="GameProfile.Priority"/> nhỏ nhất, hoặc null nếu không rule nào khớp.
    /// </summary>
    GameProfile? Match(ForegroundWindowInfo window, IEnumerable<GameProfile> profiles);
}

/// <summary>
/// Điều phối: nghe foreground đổi → so khớp rule → đổi preset hoặc ẩn/hiện overlay.
/// Đây là nơi duy nhất ghép watcher + matcher + overlay lại với nhau.
/// </summary>
public interface IProfileAutoSwitcher : IDisposable
{
    bool IsEnabled { get; set; }

    /// <summary>Rule đang có hiệu lực, null khi đang dùng preset chọn tay.</summary>
    GameProfile? ActiveGameProfile { get; }

    void Start();

    void Stop();

    /// <summary>
    /// Áp lại quy tắc hiện/ẩn overlay theo trạng thái hiện tại: người dùng có bật overlay không,
    /// tự đổi theo game có bật không, "chỉ hiện trong game đã khớp", và profile đang khớp.
    /// </summary>
    /// <remarks>
    /// Gọi sau MỌI thay đổi có thể ảnh hưởng: bật/tắt overlay (menu khay, phím tắt, cửa sổ
    /// Settings) hay đổi một trong hai tuỳ chọn trên. Đây là nơi DUY NHẤT quyết định overlay hiện
    /// hay ẩn — bật thẳng overlay ở nơi khác sẽ làm nó hiện trên desktop dù đang ở chế độ "chỉ
    /// hiện trong game".
    /// </remarks>
    void ApplyVisibility();

    /// <summary>
    /// Phát khi phát hiện foreground có thể đang ở Exclusive Fullscreen. Shell hiển thị
    /// thông báo hướng dẫn đổi sang Borderless — KHÔNG tìm cách vẽ đè lên.
    /// </summary>
    event EventHandler<ForegroundWindowInfo>? ExclusiveFullscreenDetected;
}
