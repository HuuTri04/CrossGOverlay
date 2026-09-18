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
/// Kho ảnh tâm ngắm của ứng dụng: <c>%APPDATA%\CrosshairOverlay\CustomImages</c>.
/// </summary>
/// <remarks>
/// Preset không bao giờ trỏ thẳng vào file người dùng chọn. Nếu làm vậy, người dùng xoá hay đổi
/// tên ảnh gốc là tâm ngắm biến mất ngay giữa trận. Ảnh được chép vào đây, preset chỉ lưu đường
/// dẫn tương đối tới bản chép.
/// </remarks>
public interface ICustomImageStore
{
    /// <summary>Thư mục kho, đường dẫn tuyệt đối.</summary>
    string Directory { get; }

    /// <summary>
    /// Kiểm tra rồi chép ảnh vào kho; trả về đường dẫn tương đối để lưu vào preset.
    /// </summary>
    /// <exception cref="System.IO.InvalidDataException">
    /// Không phải PNG/JPG/GIF, không giải mã được, hoặc quá lớn. Message đã được dịch, hiển thị
    /// thẳng cho người dùng được.
    /// </exception>
    /// <remarks>
    /// Ảnh có cạnh lớn hơn 256 pixel được thu nhỏ (giữ tỷ lệ) trước khi lưu, trừ GIF động.
    /// </remarks>
    string Import(string sourcePath);

    /// <summary>
    /// Kích thước điểm ảnh của một ảnh trong kho, chỉ đọc phần đầu file. Null nếu không đọc được.
    /// </summary>
    (int Width, int Height)? GetPixelSize(string? storedPath);

    /// <summary>
    /// Như <see cref="Import"/>, nhưng từ nội dung đã có trong bộ nhớ — dùng cho ảnh nhúng trong
    /// file preset nhận từ người khác. Nội dung đó KHÔNG đáng tin, nên qua đúng các bước kiểm
    /// tra như ảnh chọn từ máy.
    /// </summary>
    /// <param name="originalFileName">Chỉ dùng để lấy đuôi file và đặt tên dễ nhận ra.</param>
    string ImportBytes(string originalFileName, byte[] bytes);

    /// <summary>
    /// Xoá ảnh trong kho không còn preset nào dùng.
    /// </summary>
    /// <param name="referencedStoredPaths">Đường dẫn ảnh của MỌI preset hiện có.</param>
    /// <param name="minimumAge">
    /// Chỉ xoá ảnh không được chạm tới trong khoảng này. Cho người dùng đường lùi khi lỡ tay bỏ
    /// ảnh hay xoá preset.
    /// </param>
    /// <returns>Số file đã xoá.</returns>
    int CleanupUnused(IEnumerable<string?> referencedStoredPaths, TimeSpan minimumAge);

    /// <summary>
    /// Đổi đường dẫn lưu trong preset thành đường dẫn tuyệt đối để đọc file. Trả null nếu đường
    /// dẫn rỗng hoặc trỏ ra ngoài thư mục ứng dụng.
    /// </summary>
    string? Resolve(string? storedPath);
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

    /// <summary>Cập nhật icon, dấu tích trong menu và tooltip.</summary>
    /// <param name="overlayEnabled">
    /// Người dùng có BẬT overlay không. Quyết định màu icon và dấu tích — KHÔNG phải overlay có
    /// đang hiện hay không: ở chế độ "chỉ hiện trong game", overlay cố ý ẩn trên desktop nhưng vẫn
    /// đang bật, và icon xám lúc đó sẽ khiến người dùng tưởng ứng dụng đã tắt.
    /// </param>
    /// <param name="overlayVisible">Overlay có đang hiện thật không; chỉ dùng cho tooltip.</param>
    /// <param name="activePresetName">Tên preset đang dùng, hiện trong tooltip.</param>
    void UpdateState(bool overlayEnabled, bool overlayVisible, string activePresetName);

    /// <summary>Bong bóng thông báo, dùng cho cảnh báo Exclusive Fullscreen và lỗi hotkey.</summary>
    void ShowNotification(string title, string message, bool isWarning = false);

    event EventHandler? OpenSettingsRequested;
    event EventHandler? ToggleOverlayRequested;
    event EventHandler? ExitRequested;
}

/// <summary>Nút người dùng bấm trong hộp thoại ba lựa chọn.</summary>
public enum DialogChoice
{
    Primary,
    Secondary,

    /// <summary>Nút huỷ, hoặc Esc.</summary>
    Cancel,
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

    /// <summary>Mở hộp thoại "Xuất mã tâm ngắm" với mã của từng game.</summary>
    void ShowExportCodes(CrosshairExportCodes codes);

    /// <summary>
    /// Mở hộp thoại cập nhật (ghi chú phát hành + thanh tiến trình).
    /// </summary>
    /// <returns>true khi script cập nhật đã chạy — người gọi phải cho ứng dụng thoát ngay.</returns>
    bool ShowUpdate(UpdateInfo update);

    /// <summary>Trả về null nếu người dùng huỷ.</summary>
    string? PickFileToOpen(string filter, string? initialDirectory = null);

    /// <summary>Hộp thoại chọn thư mục. Trả về null nếu người dùng huỷ.</summary>
    string? PickFolder(string title, string? initialDirectory = null);

    /// <summary>Hộp thoại ba nút, vd Có / Không / Huỷ.</summary>
    DialogChoice Ask(string title, string message, string primaryText, string secondaryText, string cancelText);
}

/// <summary>
/// Mở một ứng dụng khác cho tính năng "Test tâm ngắm". Chỉ khởi chạy tiến trình bằng API chuẩn của
/// Windows — không đọc, không ghi, không can thiệp gì vào tiến trình đó.
/// </summary>
public interface IProcessLauncher
{
    /// <param name="fileNameOrPath">
    /// Tên file tìm theo PATH của hệ thống (vd <c>notepad.exe</c>), hoặc đường dẫn đầy đủ tới file .exe.
    /// </param>
    /// <exception cref="System.ComponentModel.Win32Exception">
    /// Không tìm thấy file, không có quyền, hoặc người dùng từ chối hộp thoại UAC.
    /// </exception>
    void Launch(string fileNameOrPath);

    /// <summary>Mở một thư mục trong File Explorer.</summary>
    /// <exception cref="System.ComponentModel.Win32Exception">Không mở được Explorer.</exception>
    void OpenFolder(string path);
}

/// <summary>Khởi động lại ứng dụng.</summary>
public interface IAppRestartService
{
    /// <summary>
    /// Mở instance mới với lệnh khôi phục cài đặt gốc rồi thoát instance này. Instance mới chờ instance
    /// này thoát hẳn mới xoá dữ liệu.
    /// </summary>
    /// <param name="error">Lý do khi không khởi chạy được instance mới — lúc đó ứng dụng KHÔNG thoát.</param>
    bool RestartWithFactoryReset(out string? error);

    /// <summary>Mở instance mới (chờ instance này thoát hẳn) rồi thoát instance này.</summary>
    bool Restart(out string? error);
}

/// <param name="CopiedFiles">Số file đã sao chép (0 nếu không chép).</param>
/// <param name="Error">Lý do thất bại; khi đó con trỏ KHÔNG bị đổi.</param>
public sealed record StorageChangeResult(bool Success, int CopiedFiles, string? Error)
{
    public static StorageChangeResult Succeeded(int copiedFiles) => new(true, copiedFiles, null);

    public static StorageChangeResult Failed(string error) => new(false, 0, error);
}

/// <summary>Đổi thư mục gốc chứa dữ liệu người dùng (settings.json, presets, CustomImages).</summary>
public interface IStorageLocationService
{
    /// <summary>Thư mục dữ liệu ĐANG dùng trong phiên chạy này.</summary>
    string CurrentRoot { get; }

    /// <summary>Thư mục thật sẽ dùng khi người dùng chọn một thư mục (có thể là thư mục con bên trong).</summary>
    string ResolveTarget(string pickedFolder);

    Services.Storage.StorageLocation.Problem Validate(string target);

    /// <summary>
    /// Sao chép dữ liệu hiện tại sang thư mục mới (nếu yêu cầu) rồi ghi con trỏ để lần khởi động tới dùng
    /// thư mục đó. Dữ liệu cũ được GIỮ NGUYÊN. Không ném exception.
    /// </summary>
    Task<StorageChangeResult> ChangeAsync(string target, bool copyExistingData);
}

/// <summary>Thông tin phần cứng/hệ điều hành đọc được. Mục nào không đọc được thì null/rỗng/0.</summary>
public sealed record HardwareInfo(
    string? OsName,
    string? OsBuild,
    bool Is64BitOs,
    string? CpuName,
    int LogicalProcessors,
    IReadOnlyList<string> Gpus,
    ulong InstalledRamBytes,
    ulong UsableRamBytes);

/// <summary>Đọc thông tin phần cứng cho panel "Thông tin hệ thống".</summary>
public interface IHardwareInfoService
{
    /// <summary>
    /// Đọc trên thread pool, không bao giờ chặn UI; lần gọi sau trả lại đúng kết quả lần đầu.
    /// Không ném exception — mục lỗi chỉ để trống.
    /// </summary>
    Task<HardwareInfo> GetAsync();
}

/// <summary>Đảm bảo chỉ một instance chạy; instance thứ hai sẽ đánh thức cửa sổ Settings.</summary>
public interface ISingleInstanceGuard : IDisposable
{
    bool TryAcquire();

    void SignalExistingInstance();

    event EventHandler? SecondInstanceLaunched;
}
