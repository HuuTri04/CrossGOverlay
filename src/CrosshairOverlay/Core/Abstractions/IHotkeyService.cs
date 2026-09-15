using CrosshairOverlay.Core.Models;

namespace CrosshairOverlay.Core.Abstractions;

/// <summary>
/// Global hotkey dựa trên <c>RegisterHotKey</c>/<c>UnregisterHotKey</c> của User32.
/// </summary>
/// <remarks>
/// Có chủ ý KHÔNG dùng <c>SetWindowsHookEx(WH_KEYBOARD_LL)</c>: low-level hook nhìn thấy mọi
/// phím gõ toàn hệ thống và là mẫu hành vi mà Vanguard/EAC/BattlEye cảnh giác.
/// <c>RegisterHotKey</c> chỉ nhận đúng tổ hợp đã đăng ký và vẫn chạy khi game giữ focus.
/// </remarks>
public interface IHotkeyService : IDisposable
{
    /// <summary>Gắn vào HWND của một cửa sổ ẩn để nhận WM_HOTKEY. Gọi một lần lúc khởi động.</summary>
    void Attach(nint hwnd);

    /// <summary>
    /// Huỷ toàn bộ đăng ký cũ rồi đăng ký lại bộ binding mới.
    /// Thất bại từng phím không ném exception — báo qua kết quả trả về để UI hiển thị.
    /// </summary>
    HotkeyRegistrationResult Apply(IEnumerable<HotkeyBinding> bindings);

    void UnregisterAll();

    event EventHandler<HotkeyPressedEventArgs>? HotkeyPressed;

    /// <summary>
    /// Theo dõi nút chuột phải (bấm/nhả) — cho tính năng "ẩn tâm ngắm khi ngắm ADS".
    /// </summary>
    /// <remarks>
    /// Đọc qua Raw Input dùng chung với phím tắt nút chuột: kênh CHỈ ĐỌC, không chặn hay giả lập sự
    /// kiện nào, và không phải hook cấp thấp (<c>WH_MOUSE_LL</c> chen vào mọi sự kiện chuột của hệ
    /// thống, gây trễ chuột trong game). Tắt thì gỡ đăng ký nếu không còn phím tắt nào cần.
    /// </remarks>
    bool TrackRightButton { get; set; }

    /// <summary>Nút phải vừa được bấm (true) hoặc nhả (false). Phát trên UI thread.</summary>
    event EventHandler<bool>? RightButtonChanged;
}

/// <param name="Registered">Các binding đăng ký thành công.</param>
/// <param name="Failures">Binding thất bại kèm lý do (thường do app khác đã chiếm tổ hợp phím).</param>
public sealed record HotkeyRegistrationResult(
    IReadOnlyList<HotkeyBinding> Registered,
    IReadOnlyList<HotkeyRegistrationFailure> Failures)
{
    public bool AllSucceeded => Failures.Count == 0;
}

public sealed record HotkeyRegistrationFailure(HotkeyBinding Binding, string Reason);

public sealed class HotkeyPressedEventArgs(HotkeyAction action) : EventArgs
{
    public HotkeyAction Action { get; } = action;
}

/// <summary>
/// Theo dõi con trỏ chuột của Windows đang hiện hay ẩn — game ẩn con trỏ khi vào trận, hiện lại khi
/// mở menu.
/// </summary>
public interface ICursorVisibilityMonitor : IDisposable
{
    /// <summary>Con trỏ đang hiện. Chỉ có ý nghĩa khi đang <see cref="Start"/>.</summary>
    bool IsCursorVisible { get; }

    void Start();

    void Stop();

    /// <summary>Con trỏ vừa đổi giữa hiện và ẩn. Phát trên UI thread.</summary>
    event EventHandler<bool>? VisibilityChanged;
}
