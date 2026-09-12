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
    /// Phát khi phát hiện foreground có thể đang ở Exclusive Fullscreen. Shell hiển thị
    /// thông báo hướng dẫn đổi sang Borderless — KHÔNG tìm cách vẽ đè lên.
    /// </summary>
    event EventHandler<ForegroundWindowInfo>? ExclusiveFullscreenDetected;
}
