using CrosshairOverlay.Core.Models;

namespace CrosshairOverlay.Core.Abstractions;

/// <summary>
/// Theo dõi cửa sổ đang active bằng <c>SetWinEventHook(EVENT_SYSTEM_FOREGROUND, WINEVENT_OUTOFCONTEXT)</c>.
/// </summary>
/// <remarks>
/// Cờ <c>WINEVENT_OUTOFCONTEXT</c> là điểm mấu chốt: callback chạy trong tiến trình của
/// chính app này, KHÔNG có DLL nào được nạp vào game. Đây là API accessibility chuẩn của
/// Windows, an toàn với anti-cheat.
/// </remarks>
public interface IForegroundWindowWatcher : IDisposable
{
    ForegroundWindowInfo Current { get; }

    /// <summary>
    /// Cửa sổ foreground gần nhất KHÔNG thuộc tiến trình này.
    /// </summary>
    /// <remarks>
    /// Khi người dùng đang nhìn cửa sổ Settings thì <see cref="Current"/> chính là cửa sổ đó,
    /// vô dụng cho việc "thêm game đang chạy". Giá trị này giữ lại cửa sổ ngoài gần nhất —
    /// thường đúng là game mà người dùng vừa Alt-Tab rời khỏi.
    /// </remarks>
    ForegroundWindowInfo LastExternal { get; }

    void Start();

    void Stop();

    /// <summary>
    /// Theo dõi riêng một cửa sổ (thường là game đang khớp profile) để biết ngay khi nó bị đóng,
    /// ẩn hoặc thu nhỏ — kể cả khi Windows không trao foreground cho cửa sổ nào khác.
    /// </summary>
    /// <remarks>
    /// Chỉ foreground thôi là không đủ: thoát game xong, foreground có thể rơi vào "không ai" và
    /// không có sự kiện đổi foreground nào cho tới khi người dùng tự Alt-Tab, nên crosshair của
    /// game cứ nằm lại trên màn hình. Truyền <see cref="ForegroundWindowInfo.Empty"/> để thôi theo dõi.
    /// Mỗi lúc chỉ theo dõi một cửa sổ; gọi lại với cửa sổ khác sẽ thay thế cửa sổ cũ.
    /// </remarks>
    void Track(ForegroundWindowInfo window);

    event EventHandler<ForegroundWindowChangedEventArgs>? ForegroundChanged;

    /// <summary>
    /// Cửa sổ đang <see cref="Track"/> đã bị huỷ, hoặc tiến trình của nó đã thoát. Phát TRƯỚC khi
    /// foreground được đọc lại; việc theo dõi đã tự dừng.
    /// </summary>
    event EventHandler<ForegroundWindowInfo>? TrackedWindowClosed;
}

public sealed class ForegroundWindowChangedEventArgs(
    ForegroundWindowInfo previous,
    ForegroundWindowInfo current) : EventArgs
{
    public ForegroundWindowInfo Previous { get; } = previous;
    public ForegroundWindowInfo Current { get; } = current;
}
