using System.Text.Json.Serialization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CrosshairOverlay.Core.Models;

/// <summary>
/// Một global hotkey. Đăng ký qua <c>RegisterHotKey</c> (User32) nên hoạt động cả khi game
/// đang giữ focus, mà KHÔNG cần low-level keyboard hook — điểm quan trọng để tránh bị
/// anti-cheat gắn cờ.
/// </summary>
public sealed partial class HotkeyBinding : ObservableObject
{
    [ObservableProperty] private HotkeyAction _action;

    [ObservableProperty] private Key _key = Key.None;

    /// <summary>
    /// Nút chuột phụ, thay cho <see cref="Key"/>. Gán một trong hai, không gán cả hai.
    /// </summary>
    /// <remarks>
    /// Nút chuột KHÔNG đăng ký được bằng <c>RegisterHotKey</c> — Windows chỉ nhận phím bàn
    /// phím. Chúng được nhận qua Raw Input, xem <c>RawMouseListener</c>.
    /// </remarks>
    [ObservableProperty] private HotkeyMouseButton _mouseButton = HotkeyMouseButton.None;

    [ObservableProperty] private ModifierKeys _modifiers = ModifierKeys.None;

    [ObservableProperty] private bool _enabled = true;

    /// <summary>Đã đăng ký thành công với hệ thống hay chưa (runtime, không lưu vào JSON).</summary>
    [ObservableProperty]
    [property: JsonIgnore]
    private bool _isRegistered;

    /// <summary>Lý do đăng ký thất bại — thường do phím đã bị app khác chiếm (runtime).</summary>
    [ObservableProperty]
    [property: JsonIgnore]
    private string? _registrationError;

    [JsonIgnore]
    public bool IsAssigned => Key != Key.None || MouseButton != HotkeyMouseButton.None;

    /// <summary>Phím tắt này dùng nút chuột, nên phải đi qua Raw Input thay vì RegisterHotKey.</summary>
    [JsonIgnore]
    public bool IsMouseBinding => MouseButton != HotkeyMouseButton.None;

    public override string ToString() => KeyNames.Describe(Modifiers, Key, MouseButton);

    public HotkeyBinding Clone() => new()
    {
        Action = Action,
        Key = Key,
        MouseButton = MouseButton,
        Modifiers = Modifiers,
        Enabled = Enabled,
    };

    /// <summary>Bộ hotkey mặc định khi chạy lần đầu.</summary>
    public static List<HotkeyBinding> CreateDefaults() =>
    [
        new() { Action = HotkeyAction.ToggleOverlay, Modifiers = ModifierKeys.Alt, Key = Key.X },
        new() { Action = HotkeyAction.NextPreset, Modifiers = ModifierKeys.Alt, Key = Key.OemCloseBrackets },
        new() { Action = HotkeyAction.PreviousPreset, Modifiers = ModifierKeys.Alt, Key = Key.OemOpenBrackets },
        new() { Action = HotkeyAction.ShowSettingsWindow, Modifiers = ModifierKeys.Alt, Key = Key.C },
        new() { Action = HotkeyAction.ExitApplication, Key = Key.None, Enabled = false },
    ];
}
