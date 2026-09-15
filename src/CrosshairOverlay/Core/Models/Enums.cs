namespace CrosshairOverlay.Core.Models;

/// <summary>Hình dạng gốc của crosshair. Quyết định renderer vẽ primitive nào.</summary>
public enum CrosshairShape
{
    /// <summary>4 nhánh: trên, dưới, trái, phải.</summary>
    Cross = 0,

    /// <summary>3 nhánh, bỏ nhánh trên (kiểu CS/Valorant "T").</summary>
    TShape = 1,

    /// <summary>Chỉ có chấm giữa.</summary>
    Dot = 2,

    /// <summary>Vòng tròn rỗng.</summary>
    Circle = 3,

    /// <summary>Vòng tròn + chấm giữa.</summary>
    CircleDot = 4,

    /// <summary>Khung vuông rỗng.</summary>
    Square = 5,

    /// <summary>4 nhánh xoay 45 độ.</summary>
    XShape = 6,

    /// <summary>
    /// CHỈ ĐỂ ĐỌC file cũ. Chế độ ảnh giờ là <see cref="CrosshairType.Image"/>; preset cũ mang
    /// giá trị này được <see cref="CrosshairProfile"/> tự chuyển sang khi đọc.
    /// </summary>
    CustomImage = 7,
}

/// <summary>Tâm ngắm vẽ bằng hình học, hay bằng một bức ảnh.</summary>
public enum CrosshairType
{
    /// <summary>Vẽ từ chấm giữa, nhánh trong, nhánh ngoài, vòng và viền.</summary>
    Standard = 0,

    /// <summary>Hiển thị một ảnh PNG/JPG/GIF do người dùng chọn.</summary>
    Image = 1,
}

/// <summary>Cách chọn màn hình để đặt overlay.</summary>
public enum MonitorSelectionMode
{
    /// <summary>Luôn dùng màn hình chính của Windows.</summary>
    Primary = 0,

    /// <summary>Bám theo màn hình đang chứa cửa sổ foreground (mặc định, hợp với multi-monitor).</summary>
    FollowForegroundWindow = 1,

    /// <summary>Ghim vào một màn hình cụ thể theo DeviceName (vd "\\\\.\\DISPLAY2").</summary>
    Specific = 2,
}

/// <summary>Cách một <see cref="GameProfile"/> so khớp với cửa sổ đang active.</summary>
public enum ProcessMatchMode
{
    /// <summary>So sánh tên file exe, không phân biệt hoa thường (vd "cs2.exe").</summary>
    ProcessName = 0,

    /// <summary>Tiêu đề cửa sổ có chứa chuỗi pattern.</summary>
    WindowTitleContains = 1,

    /// <summary>Đường dẫn đầy đủ của executable khớp chính xác.</summary>
    ExecutablePath = 2,
}

/// <summary>Hành động mà một global hotkey kích hoạt.</summary>
public enum HotkeyAction
{
    ToggleOverlay = 0,
    NextPreset = 1,
    PreviousPreset = 2,
    ShowSettingsWindow = 3,
    ExitApplication = 4,
}

/// <summary>
/// Nút chuột dùng được làm phím tắt.
/// </summary>
/// <remarks>
/// Chỉ nhận những nút KHÔNG dùng để thao tác thông thường. Chuột trái/phải bị loại có chủ ý:
/// gán chúng làm phím tắt toàn cục sẽ khiến người dùng không click được gì nữa.
/// </remarks>
public enum HotkeyMouseButton
{
    None = 0,

    /// <summary>Nút giữa (bấm con lăn).</summary>
    Middle = 1,

    /// <summary>Nút phụ bên hông, thường là "Back".</summary>
    XButton1 = 2,

    /// <summary>Nút phụ bên hông, thường là "Forward".</summary>
    XButton2 = 3,
}

/// <summary>Hành vi overlay khi profile game được kích hoạt.</summary>
public enum GameProfileBehavior
{
    /// <summary>Hiện overlay với preset đã gán.</summary>
    ShowPreset = 0,

    /// <summary>Ẩn overlay (dùng cho game có crosshair riêng, hoặc app không muốn overlay).</summary>
    HideOverlay = 1,
}

/// <summary>Mức tin cậy khi phát hiện cửa sổ foreground đang chạy fullscreen.</summary>
public enum FullscreenKind
{
    /// <summary>Cửa sổ thường / windowed.</summary>
    None = 0,

    /// <summary>Borderless fullscreen — overlay hoạt động bình thường.</summary>
    Borderless = 1,

    /// <summary>
    /// Nghi ngờ Exclusive Fullscreen (DXGI flip exclusive). Overlay GDI/WPF sẽ KHÔNG hiển thị.
    /// App phải cảnh báo người dùng đổi sang Borderless, tuyệt đối không tìm cách bypass.
    /// </summary>
    LikelyExclusive = 2,
}

/// <summary>Mức ưu tiên CPU của tiến trình ứng dụng.</summary>
public enum ProcessPriorityMode
{
    Normal,

    /// <summary>
    /// Windows chia CPU cho overlay trước khi chia cho tiến trình thường khi máy đang tải 100%.
    /// Overlay gần như không dùng CPU lúc rảnh, nên không giành gì của game khi không cần.
    /// </summary>
    High,
}
