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
