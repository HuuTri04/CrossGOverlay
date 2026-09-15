using System.Runtime.InteropServices;

namespace CrosshairOverlay.Interop;

[StructLayout(LayoutKind.Sequential)]
internal struct RAWINPUTDEVICE
{
    public ushort usUsagePage;
    public ushort usUsage;
    public uint dwFlags;
    public nint hwndTarget;
}

[StructLayout(LayoutKind.Sequential)]
internal struct RAWINPUTHEADER
{
    public uint dwType;
    public uint dwSize;
    public nint hDevice;
    public nint wParam;
}

/// <remarks>
/// Bố cục gốc có một union 4 byte ngay sau <c>usFlags</c>, nên trình biên dịch C chèn 2 byte
/// đệm vào đó. Phải khai báo trường đệm tường minh, nếu không mọi trường phía sau lệch 2 byte
/// và cờ nút bấm đọc ra sẽ là rác.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal struct RAWMOUSE
{
    public ushort usFlags;
    public ushort usAlignmentPadding;
    public ushort usButtonFlags;
    public ushort usButtonData;
    public uint ulRawButtons;
    public int lLastX;
    public int lLastY;
    public uint ulExtraInformation;
}

[StructLayout(LayoutKind.Sequential)]
internal struct RAWINPUTMOUSE
{
    public RAWINPUTHEADER header;
    public RAWMOUSE mouse;
}

internal static partial class NativeMethods
{
    /// <remarks>
    /// Raw Input là kênh CHỈ ĐỌC: nó báo cho ta biết có nút nào vừa được bấm, và không có cách
    /// nào chặn, sửa hay giả lập sự kiện qua đây. Đó là lý do chọn nó thay cho
    /// <c>SetWindowsHookEx(WH_MOUSE_LL)</c> — hook cấp thấp nằm TRÊN đường đi của mọi sự kiện
    /// chuột toàn hệ thống và có quyền nuốt chúng, đúng mẫu hành vi mà anti-cheat cảnh giác.
    /// </remarks>
    [DllImport(User32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RegisterRawInputDevices(
        [In] RAWINPUTDEVICE[] pRawInputDevices, uint uiNumDevices, uint cbSize);

    [DllImport(User32, SetLastError = true)]
    internal static extern uint GetRawInputData(
        nint hRawInput, uint uiCommand, out RAWINPUTMOUSE pData, ref uint pcbSize, uint cbSizeHeader);

    /// <summary>
    /// Đọc trạng thái phím bổ trợ tại thời điểm sự kiện chuột xảy ra.
    /// </summary>
    /// <remarks>
    /// Không dùng <c>Keyboard.Modifiers</c> của WPF: giá trị đó phản ánh những gì WPF biết,
    /// và khi ứng dụng không có focus (đúng lúc người dùng đang trong game) nó đã cũ.
    /// </remarks>
    [DllImport(User32)]
    internal static extern short GetAsyncKeyState(int vKey);

    [DllImport(User32)]
    internal static extern int GetSystemMetrics(int nIndex);

    [StructLayout(LayoutKind.Sequential)]
    internal struct CURSORINFO
    {
        public int cbSize;
        public int flags;
        public nint hCursor;
        public int ptScreenX;
        public int ptScreenY;
    }

    /// <summary>Con trỏ đang hiện hay ẩn (<c>ShowCursor(FALSE)</c> hoặc <c>SetCursor(NULL)</c>). Chỉ đọc.</summary>
    [DllImport(User32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetCursorInfo(ref CURSORINFO pci);
}
