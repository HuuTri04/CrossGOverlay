using System.Windows;

namespace CrosshairOverlay.Core.Models;

/// <summary>
/// Ảnh chụp trạng thái cửa sổ đang active. Chỉ chứa metadata công khai của cửa sổ/tiến trình.
/// </summary>
/// <param name="Handle">HWND của cửa sổ foreground.</param>
/// <param name="ProcessId">PID sở hữu cửa sổ.</param>
/// <param name="ProcessName">Tên file exe kèm phần mở rộng, vd "cs2.exe". Rỗng nếu không truy cập được.</param>
/// <param name="ExecutablePath">Đường dẫn đầy đủ, có thể null với tiến trình bị bảo vệ.</param>
/// <param name="WindowTitle">Tiêu đề cửa sổ.</param>
/// <param name="Bounds">Vùng cửa sổ, physical pixel.</param>
/// <param name="Fullscreen">Suy đoán kiểu fullscreen, xem <see cref="FullscreenKind"/>.</param>
public readonly record struct ForegroundWindowInfo(
    nint Handle,
    int ProcessId,
    string ProcessName,
    string? ExecutablePath,
    string WindowTitle,
    Rect Bounds,
    FullscreenKind Fullscreen)
{
    public static ForegroundWindowInfo Empty { get; } = new(
        0, 0, string.Empty, null, string.Empty, Rect.Empty, FullscreenKind.None);

    public bool IsValid => Handle != 0;

    /// <summary>Cửa sổ này có thuộc chính tiến trình CrosshairOverlay hay không.</summary>
    public bool IsOwnProcess => ProcessId != 0 && ProcessId == Environment.ProcessId;

    /// <summary>
    /// Có cửa sổ hợp lệ nhưng KHÔNG đọc được tên file thực thi.
    /// </summary>
    /// <remarks>
    /// Gần như luôn có nghĩa tiến trình đó chạy ở mức toàn vẹn cao hơn (thường là quyền
    /// Administrator), nên Windows từ chối <c>OpenProcess</c> từ một tiến trình quyền thường.
    /// Khi đó việc so khớp theo TÊN TIẾN TRÌNH không dùng được, nhưng so khớp theo TIÊU ĐỀ
    /// CỬA SỔ vẫn hoạt động — UI dùng cờ này để hướng người dùng sang cách đó.
    /// </remarks>
    public bool ProcessNameUnavailable => IsValid && ProcessId != 0 && ExecutablePath is null;
}
