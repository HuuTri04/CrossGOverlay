namespace CrosshairOverlay.Core.Abstractions;

/// <summary>Một ứng dụng đang chạy và có cửa sổ giao diện.</summary>
/// <param name="ProcessId">PID.</param>
/// <param name="ProcessName">Tên file thực thi kèm đuôi, vd "cs2.exe".</param>
/// <param name="WindowTitle">Tiêu đề cửa sổ chính.</param>
/// <param name="ExecutablePath">Đường dẫn đầy đủ, null nếu tiến trình chạy quyền cao hơn.</param>
public sealed record RunningApplication(
    int ProcessId,
    string ProcessName,
    string WindowTitle,
    string? ExecutablePath)
{
    /// <summary>Dòng hiển thị trong danh sách chọn.</summary>
    public string Display => $"{ProcessName}  —  {WindowTitle}";
}

/// <summary>
/// Liệt kê các tiến trình đang chạy CÓ cửa sổ giao diện, để người dùng chọn thay vì gõ tay
/// tên file thực thi.
/// </summary>
/// <remarks>
/// Chỉ đọc <c>MainWindowHandle</c>, <c>MainWindowTitle</c> và tên file thực thi — đều là
/// metadata công khai. Không mở handle với quyền đọc bộ nhớ, không liệt kê module.
/// </remarks>
public interface IRunningApplicationScanner
{
    IReadOnlyList<RunningApplication> Scan();
}
