using CrosshairOverlay.Core.Models;

namespace CrosshairOverlay.Core.Abstractions;

/// <summary>Nơi lưu dữ liệu người dùng. Gom vào một chỗ để test có thể trỏ sang thư mục tạm.</summary>
public interface IAppPathProvider
{
    /// <summary>%APPDATA%\CrosshairOverlay</summary>
    string RootDirectory { get; }

    /// <summary>%APPDATA%\CrosshairOverlay\presets</summary>
    string PresetsDirectory { get; }

    /// <summary>%APPDATA%\CrosshairOverlay\settings.json</summary>
    string SettingsFilePath { get; }

    /// <summary>%LOCALAPPDATA%\CrosshairOverlay\logs</summary>
    string LogsDirectory { get; }

    /// <summary>Tạo các thư mục còn thiếu. Gọi sớm nhất có thể lúc khởi động.</summary>
    void EnsureCreated();
}

/// <summary>
/// Bật/tắt khởi động cùng Windows qua khoá registry
/// <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c>.
/// Dùng HKCU (không phải HKLM) nên không cần quyền admin.
/// </summary>
public interface IStartupService
{
    bool IsEnabled { get; }

    /// <summary>Trả về false nếu registry bị chặn (vd chính sách nhóm) — không ném exception.</summary>
    bool SetEnabled(bool enabled);
}

/// <summary>Icon khay hệ thống và menu chuột phải.</summary>
public interface ITrayIconController : IDisposable
{
    void Initialize();

    /// <summary>Cập nhật tooltip / dấu tích trong menu khi trạng thái đổi.</summary>
    void UpdateState(bool overlayEnabled, string activePresetName);

    /// <summary>Bong bóng thông báo, dùng cho cảnh báo Exclusive Fullscreen và lỗi hotkey.</summary>
    void ShowNotification(string title, string message, bool isWarning = false);

    event EventHandler? OpenSettingsRequested;
    event EventHandler? ToggleOverlayRequested;
    event EventHandler? ExitRequested;
}

/// <summary>Tương tác UI mà ViewModel cần nhưng không được tự gọi (giữ VM test được).</summary>
public interface IDialogService
{
    void ShowSettingsWindow();

    void ShowMessage(string title, string message);

    /// <summary>Thông báo mang sắc thái cảnh báo hoặc lỗi (đổi màu dải nhấn của hộp thoại).</summary>
    void ShowMessage(string title, string message, bool isError);

    bool Confirm(string title, string message);

    /// <summary>Xác nhận với chữ trên nút do nơi gọi quyết định, vd "Cập nhật ngay" / "Để sau".</summary>
    bool Confirm(string title, string message, string primaryText, string secondaryText);

    /// <summary>
    /// Mở hộp thoại dán mã crosshair của game (CS2 hoặc Valorant) và trả về preset đã dựng,
    /// hoặc null nếu người dùng huỷ.
    /// </summary>
    Models.CrosshairProfile? PromptForCrosshairCode();

    /// <summary>Chép chuỗi vào clipboard. Trả về false nếu ứng dụng khác đang giữ clipboard.</summary>
    bool CopyToClipboard(string text);

    /// <summary>Trả về null nếu người dùng huỷ.</summary>
    string? PickFileToOpen(string filter, string? initialDirectory = null);

    string? PickFileToSave(string filter, string suggestedFileName, string? initialDirectory = null);
}

/// <summary>Đảm bảo chỉ một instance chạy; instance thứ hai sẽ đánh thức cửa sổ Settings.</summary>
public interface ISingleInstanceGuard : IDisposable
{
    bool TryAcquire();

    void SignalExistingInstance();

    event EventHandler? SecondInstanceLaunched;
}
