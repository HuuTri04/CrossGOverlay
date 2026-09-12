namespace CrosshairOverlay.Interop;

/// <summary>Hằng số Win32 dùng cho cửa sổ overlay.</summary>
internal static class Win32Constants
{
    // GetWindowLongPtr / SetWindowLongPtr index
    public const int GWL_STYLE = -16;
    public const int GWL_EXSTYLE = -20;

    // Extended window styles
    /// <summary>Chuột xuyên thấu — mọi click đi thẳng xuống cửa sổ bên dưới (game).</summary>
    public const int WS_EX_TRANSPARENT = 0x00000020;

    /// <summary>Layered window, điều kiện để có per-pixel alpha.</summary>
    public const int WS_EX_LAYERED = 0x00080000;

    /// <summary>Không xuất hiện trong Alt-Tab và thanh taskbar.</summary>
    public const int WS_EX_TOOLWINDOW = 0x00000080;

    /// <summary>Không nhận activation khi được click hoặc hiển thị — chìa khoá để không cướp focus.</summary>
    public const int WS_EX_NOACTIVATE = 0x08000000;

    public const int WS_EX_TOPMOST = 0x00000008;
    public const int WS_EX_APPWINDOW = 0x00040000;

    // SetWindowPos flags
    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOREDRAW = 0x0008;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_SHOWWINDOW = 0x0040;
    public const uint SWP_HIDEWINDOW = 0x0080;
    public const uint SWP_NOOWNERZORDER = 0x0200;
    public const uint SWP_NOSENDCHANGING = 0x0400;

    // SetWindowPos hWndInsertAfter
    public static readonly nint HWND_TOP = 0;
    public static readonly nint HWND_TOPMOST = -1;
    public static readonly nint HWND_NOTOPMOST = -2;

    // MonitorFromWindow / MonitorFromPoint
    public const uint MONITOR_DEFAULTTONULL = 0x00000000;
    public const uint MONITOR_DEFAULTTOPRIMARY = 0x00000001;
    public const uint MONITOR_DEFAULTTONEAREST = 0x00000002;

    // MONITORINFO.dwFlags
    public const uint MONITORINFOF_PRIMARY = 0x00000001;

    // ShowWindow
    public const int SW_HIDE = 0;
    public const int SW_SHOWNOACTIVATE = 4;

    // Window messages
    public const int WM_DISPLAYCHANGE = 0x007E;
    public const int WM_DPICHANGED = 0x02E0;
    public const int WM_SETTINGCHANGE = 0x001A;

    /// <summary>DPI "gốc" của Windows. Scale = dpi / 96.</summary>
    public const double DefaultDpi = 96d;

    // Window styles dùng để phán đoán cửa sổ có đang fullscreen không
    public const long WS_CAPTION = 0x00C00000L;
    public const long WS_THICKFRAME = 0x00040000L;
    public const long WS_CHILD = 0x40000000L;

    // Global hotkey
    public const int WM_HOTKEY = 0x0312;

    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_WIN = 0x0008;

    /// <summary>Chặn tự lặp khi người dùng giữ phím — nếu không, giữ phím sẽ bắn liên tục.</summary>
    public const uint MOD_NOREPEAT = 0x4000;

    /// <summary>Tổ hợp phím đã bị ứng dụng khác chiếm.</summary>
    public const int ERROR_HOTKEY_ALREADY_REGISTERED = 1409;

    // WinEvent
    public const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    public const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    public const uint WINEVENT_SKIPOWNPROCESS = 0x0002;

    /// <summary>Quyền tối thiểu đủ để đọc tên file thực thi của tiến trình.</summary>
    public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    /// <summary>Cửa sổ message-only: không hiển thị, chỉ nhận message.</summary>
    public static readonly nint HWND_MESSAGE = -3;

    // Raw Input — nhận nút chuột phụ (RegisterHotKey chỉ nhận phím bàn phím)
    public const int WM_INPUT = 0x00FF;
    public const uint RID_INPUT = 0x10000003;
    public const uint RIM_TYPEMOUSE = 0;

    /// <summary>Nhận được input cả khi cửa sổ không ở foreground — bắt buộc với phím tắt toàn cục.</summary>
    public const uint RIDEV_INPUTSINK = 0x00000100;

    public const uint RIDEV_REMOVE = 0x00000001;
    public const ushort HID_USAGE_PAGE_GENERIC = 0x01;
    public const ushort HID_USAGE_GENERIC_MOUSE = 0x02;

    public const ushort RI_MOUSE_MIDDLE_BUTTON_DOWN = 0x0010;
    public const ushort RI_MOUSE_BUTTON_4_DOWN = 0x0040;
    public const ushort RI_MOUSE_BUTTON_5_DOWN = 0x0100;

    // Virtual-key của phím bổ trợ, dùng với GetAsyncKeyState
    public const int VK_SHIFT = 0x10;
    public const int VK_CONTROL = 0x11;
    public const int VK_MENU = 0x12;
    public const int VK_LWIN = 0x5B;
    public const int VK_RWIN = 0x5C;
}
