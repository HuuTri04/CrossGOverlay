using System.Runtime.InteropServices;
using System.Text;

namespace CrosshairOverlay.Interop;

/// <summary>
/// Callback của <c>SetWinEventHook</c>. Instance delegate PHẢI được giữ trong một field sống
/// lâu — nếu để GC thu hồi trong lúc hook còn hoạt động, tiến trình sẽ chết khi Windows gọi lại.
/// </summary>
internal delegate void WinEventProc(
    nint hWinEventHook, uint eventType, nint hwnd,
    int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

/// <summary>Trạng thái thông báo của shell, xem <c>SHQueryUserNotificationState</c>.</summary>
internal enum UserNotificationState
{
    NotPresent = 1,
    Busy = 2,

    /// <summary>Có ứng dụng Direct3D đang chạy TOÀN MÀN HÌNH ĐỘC QUYỀN.</summary>
    RunningD3dFullScreen = 3,

    PresentationMode = 4,
    AcceptsNotifications = 5,
    QuietTime = 6,
    App = 7,
}

internal static partial class NativeMethods
{
    private const string Kernel32 = "kernel32.dll";
    private const string Shell32 = "shell32.dll";

    // ---------------------------------------------------------------- Global hotkey

    /// <remarks>
    /// Dùng API này thay cho <c>SetWindowsHookEx(WH_KEYBOARD_LL)</c> có chủ đích: low-level hook
    /// nhìn thấy MỌI phím gõ toàn hệ thống — đúng mẫu hành vi keylogger mà anti-cheat gắn cờ.
    /// <c>RegisterHotKey</c> chỉ nhận đúng tổ hợp đã đăng ký, và vẫn hoạt động khi game giữ focus.
    /// </remarks>
    [DllImport(User32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [DllImport(User32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnregisterHotKey(nint hWnd, int id);

    // ---------------------------------------------------------------- Foreground tracking

    /// <remarks>
    /// Luôn gọi với <see cref="Win32Constants.WINEVENT_OUTOFCONTEXT"/>: callback chạy trong
    /// tiến trình của chính app này, KHÔNG có DLL nào được nạp vào game. Đây là API
    /// accessibility chuẩn của Windows.
    /// </remarks>
    [DllImport(User32, SetLastError = true)]
    internal static extern nint SetWinEventHook(
        uint eventMin, uint eventMax, nint hmodWinEventProc,
        WinEventProc lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

    [DllImport(User32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnhookWinEvent(nint hWinEventHook);

    [DllImport(User32)]
    internal static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    [DllImport(User32, EntryPoint = "GetWindowTextW", CharSet = CharSet.Unicode)]
    internal static extern int GetWindowText(nint hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport(User32, EntryPoint = "GetWindowTextLengthW", CharSet = CharSet.Unicode)]
    internal static extern int GetWindowTextLength(nint hWnd);

    [DllImport(User32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindowVisible(nint hWnd);

    [DllImport(User32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsIconic(nint hWnd);

    // ---------------------------------------------------------------- Process metadata

    /// <remarks>
    /// Chỉ mở với <see cref="Win32Constants.PROCESS_QUERY_LIMITED_INFORMATION"/> — quyền tối
    /// thiểu để đọc tên file thực thi — hoặc <see cref="Win32Constants.SYNCHRONIZE"/> để chờ
    /// tiến trình thoát. KHÔNG dùng <c>PROCESS_VM_READ</c> hay
    /// <c>PROCESS_QUERY_INFORMATION</c>; mở handle với quyền đọc memory là thứ anti-cheat
    /// coi là hành vi tấn công.
    /// </remarks>
    [DllImport(Kernel32, SetLastError = true)]
    internal static extern nint OpenProcess(
        uint dwDesiredAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, uint dwProcessId);

    [DllImport(Kernel32, EntryPoint = "QueryFullProcessImageNameW", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool QueryFullProcessImageName(
        nint hProcess, uint dwFlags, StringBuilder lpExeName, ref uint lpdwSize);

    [DllImport(Kernel32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseHandle(nint hObject);

    // ---------------------------------------------------------------- Shell state

    /// <summary>
    /// Trả về HRESULT. Đây là API công khai duy nhất cho biết có ứng dụng D3D nào đang chạy
    /// toàn màn hình độc quyền hay không — tín hiệu để cảnh báo người dùng, chỉ đọc.
    /// </summary>
    [DllImport(Shell32)]
    internal static extern int SHQueryUserNotificationState(out UserNotificationState state);
}
