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

    event EventHandler<ForegroundWindowChangedEventArgs>? ForegroundChanged;
}

public sealed class ForegroundWindowChangedEventArgs(
    ForegroundWindowInfo previous,
    ForegroundWindowInfo current) : EventArgs
{
    public ForegroundWindowInfo Previous { get; } = previous;
    public ForegroundWindowInfo Current { get; } = current;
}
