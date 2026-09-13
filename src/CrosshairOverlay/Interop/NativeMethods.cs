using System.Runtime.InteropServices;

namespace CrosshairOverlay.Interop;

internal delegate bool MonitorEnumProc(nint hMonitor, nint hdcMonitor, ref RECT lprcMonitor, nint dwData);

/// <summary>
/// Toàn bộ P/Invoke của ứng dụng gom về một chỗ.
/// </summary>
/// <remarks>
/// Mọi API ở đây đều là API cửa sổ/hiển thị công khai của Windows, chỉ đọc metadata.
/// Không có hàm nào chạm tới memory của tiến trình khác (<c>ReadProcessMemory</c>,
/// <c>OpenProcess</c> với <c>PROCESS_VM_*</c>), không nạp DLL vào tiến trình khác,
/// và không mô phỏng input (<c>SendInput</c>, <c>keybd_event</c>).
/// </remarks>
internal static partial class NativeMethods
{
    private const string User32 = "user32.dll";
    private const string Shcore = "shcore.dll";

    // ---------------------------------------------------------------- Window styles

    [DllImport(User32, EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    internal static extern nint GetWindowLongPtr(nint hWnd, int nIndex);

    [DllImport(User32, EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    internal static extern nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);

    // ---------------------------------------------------------------- Positioning

    [DllImport(User32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowPos(
        nint hWnd, nint hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport(User32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ShowWindow(nint hWnd, int nCmdShow);

    [DllImport(User32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowRect(nint hWnd, out RECT lpRect);

    [DllImport(User32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindow(nint hWnd);

    /// <summary>Duyệt thứ tự z: <c>GW_HWNDPREV</c> trả về cửa sổ nằm ngay TRÊN.</summary>
    [DllImport(User32)]
    internal static extern nint GetWindow(nint hWnd, uint uCmd);

    // ---------------------------------------------------------------- Monitors

    [DllImport(User32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumDisplayMonitors(
        nint hdc, nint lprcClip, MonitorEnumProc lpfnEnum, nint dwData);

    [DllImport(User32, EntryPoint = "GetMonitorInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetMonitorInfo(nint hMonitor, ref MONITORINFOEXW lpmi);

    [DllImport(User32)]
    internal static extern nint MonitorFromWindow(nint hwnd, uint dwFlags);

    [DllImport(User32)]
    internal static extern nint MonitorFromPoint(POINT pt, uint dwFlags);

    // ---------------------------------------------------------------- DPI

    /// <summary>Win8.1+. Trả HRESULT; S_OK = 0.</summary>
    [DllImport(Shcore)]
    internal static extern int GetDpiForMonitor(
        nint hmonitor, MonitorDpiType dpiType, out uint dpiX, out uint dpiY);

    /// <summary>Win10 1607+. Trả 0 nếu thất bại.</summary>
    [DllImport(User32)]
    internal static extern uint GetDpiForWindow(nint hwnd);

    // ---------------------------------------------------------------- Foreground window

    [DllImport(User32)]
    internal static extern nint GetForegroundWindow();

    // ---------------------------------------------------------------- Icons

    /// <summary>
    /// Huỷ HICON do <c>Bitmap.GetHicon</c> tạo ra. <c>Icon.FromHandle</c> không sở hữu handle,
    /// nên không gọi hàm này là rò handle.
    /// </summary>
    [DllImport(User32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyIcon(nint hIcon);
}
