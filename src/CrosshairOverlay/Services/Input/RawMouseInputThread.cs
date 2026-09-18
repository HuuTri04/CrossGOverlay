using System.Runtime.InteropServices;
using CrosshairOverlay.Interop;

namespace CrosshairOverlay.Services.Input;

/// <summary>
/// Nhận Raw Input chuột trên MỘT LUỒNG NỀN RIÊNG, với cửa sổ message-only Win32 thuần — không qua WPF.
/// </summary>
/// <remarks>
/// <para>
/// Raw Input chuột không cho lọc riêng sự kiện nút: đã đăng ký là nhận MỌI lần di chuột, chuột gaming
/// 500–1000 lần mỗi giây, suốt trận đấu. Trước đây gói tin tới cửa sổ ẩn của WPF: mỗi lần di chuột đánh
/// thức luồng giao diện, chạy vòng dispatcher, chuỗi hook của HwndSource rồi mới tới hàm xử lý — đo thực
/// tế ~0,7 giây CPU mỗi 20 giây ở 500Hz, chỉ để đọc gói tin rồi bỏ đi.
/// </para>
/// <para>
/// Ở đây mỗi gói chỉ là một <c>GetMessage</c> → <c>DispatchMessage</c> → WndProc gọi thẳng
/// <c>GetRawInputData</c> trên luồng riêng. Luồng giao diện KHÔNG bị đánh thức trừ khi có nút được bấm
/// hoặc nhả — việc đó do <see cref="HotkeyService"/> quyết định. Vẫn là kênh CHỈ ĐỌC: không chặn, không
/// sửa, không giả lập sự kiện nào.
/// </para>
/// </remarks>
internal interface IRawMouseInput : IDisposable
{
    bool IsRunning { get; }

    bool Start();

    void Stop();
}

internal sealed class RawMouseInputThread : IRawMouseInput, IDisposable
{
    private const uint WmApp = 0x8000;
    private const uint WmStop = WmApp + 1;

    private static readonly uint RawMouseSize = (uint)Marshal.SizeOf<RAWINPUTMOUSE>();
    private static readonly uint RawHeaderSize = (uint)Marshal.SizeOf<RAWINPUTHEADER>();

    /// <summary>
    /// Gọi TRÊN LUỒNG INPUT cho mỗi gói chuột, với cờ nút (0 = chỉ di chuyển). Phải cực rẻ và an toàn luồng.
    /// </summary>
    private readonly Action<ushort> _onPacket;

    /// <summary>Giữ delegate sống: Windows chỉ giữ con trỏ hàm, GC thu delegate là sập tiến trình.</summary>
    private readonly WndProcDelegate _wndProc;

    private readonly object _gate = new();
    private Thread? _thread;
    private nint _hwnd;
    private volatile bool _registered;

    public RawMouseInputThread(Action<ushort> onPacket)
    {
        _onPacket = onPacket;
        _wndProc = WndProc;
    }

    /// <summary>Đã đăng ký Raw Input và đang nhận gói tin.</summary>
    public bool IsRunning => _registered;

    /// <summary>Số gói đã nhận — chỉ để chẩn đoán.</summary>
    public long PacketCount => Interlocked.Read(ref _packets);

    private long _packets;

    /// <summary>Khởi động luồng và chờ đăng ký xong. Trả false nếu Windows từ chối đăng ký.</summary>
    public bool Start()
    {
        lock (_gate)
        {
            if (_thread is not null) return _registered;

            using var ready = new ManualResetEventSlim(false);
            _thread = new Thread(() => Run(ready))
            {
                IsBackground = true,
                Name = "CrossGOverlay RawMouse",
            };
            _thread.Start();

            ready.Wait(TimeSpan.FromSeconds(5));

            if (!_registered)
            {
                _thread.Join(TimeSpan.FromSeconds(1));
                _thread = null;
            }

            return _registered;
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (_thread is null) return;

            if (_hwnd != 0) PostMessage(_hwnd, WmStop, 0, 0);
            _thread.Join(TimeSpan.FromSeconds(2));
            _thread = null;
            _registered = false;
        }
    }

    private void Run(ManualResetEventSlim ready)
    {
        var instance = GetModuleHandle(null);
        var className = "CrossGOverlay.RawMouse." + Environment.ProcessId + "." + Environment.CurrentManagedThreadId;

        var windowClass = new WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = instance,
            lpszClassName = className,
        };

        if (RegisterClassEx(ref windowClass) == 0)
        {
            ready.Set();
            return;
        }

        _hwnd = CreateWindowEx(0, className, string.Empty, 0, 0, 0, 0, 0, Win32Constants.HWND_MESSAGE, 0, instance, 0);

        if (_hwnd != 0)
        {
            var devices = new[]
            {
                new RAWINPUTDEVICE
                {
                    usUsagePage = Win32Constants.HID_USAGE_PAGE_GENERIC,
                    usUsage = Win32Constants.HID_USAGE_GENERIC_MOUSE,
                    // INPUTSINK: vẫn nhận khi cửa sổ không ở foreground — bắt buộc để chạy được lúc đang trong game.
                    dwFlags = Win32Constants.RIDEV_INPUTSINK,
                    hwndTarget = _hwnd,
                },
            };

            _registered = NativeMethods.RegisterRawInputDevices(devices, 1, (uint)Marshal.SizeOf<RAWINPUTDEVICE>());
        }

        ready.Set();

        if (_registered)
        {
            while (GetMessage(out var message, 0, 0, 0) > 0)
                DispatchMessage(ref message);

            var remove = new[]
            {
                new RAWINPUTDEVICE
                {
                    usUsagePage = Win32Constants.HID_USAGE_PAGE_GENERIC,
                    usUsage = Win32Constants.HID_USAGE_GENERIC_MOUSE,
                    dwFlags = Win32Constants.RIDEV_REMOVE,
                    hwndTarget = 0,
                },
            };
            NativeMethods.RegisterRawInputDevices(remove, 1, (uint)Marshal.SizeOf<RAWINPUTDEVICE>());
        }

        if (_hwnd != 0) DestroyWindow(_hwnd);
        _hwnd = 0;
        UnregisterClass(className, instance);
        _registered = false;
    }

    private nint WndProc(nint hwnd, uint message, nint wParam, nint lParam)
    {
        switch (message)
        {
            case Win32Constants.WM_INPUT:
            {
                var size = RawMouseSize;
                var read = NativeMethods.GetRawInputData(lParam, Win32Constants.RID_INPUT, out var raw, ref size, RawHeaderSize);

                if (read != uint.MaxValue && raw.header.dwType == Win32Constants.RIM_TYPEMOUSE)
                {
                    Interlocked.Increment(ref _packets);

                    try
                    {
                        _onPacket(raw.mouse.usButtonFlags);
                    }
                    catch
                    {
                        // Exception thoát ra khỏi WndProc (reverse P/Invoke) sẽ làm sập tiến trình.
                    }
                }

                // Tài liệu Raw Input: phải gọi DefWindowProc để hệ thống dọn dữ liệu của gói.
                return DefWindowProc(hwnd, message, wParam, lParam);
            }

            case WmStop:
                PostQuitMessage(0);
                return 0;
        }

        return DefWindowProc(hwnd, message, wParam, lParam);
    }

    public void Dispose() => Stop();

    // ------------------------------------------------------------------ interop

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint WndProcDelegate(nint hwnd, uint message, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public uint cbSize;
        public uint style;
        public nint lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public nint hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public nint hwnd;
        public uint message;
        public nint wParam;
        public nint lParam;
        public uint time;
        public int ptX;
        public int ptY;
        public uint lPrivate;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WNDCLASSEX windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterClass(string className, nint instance);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowEx(
        uint exStyle, string className, string windowName, uint style, int x, int y, int width, int height,
        nint parent, nint menu, nint instance, nint param);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(nint hwnd);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out MSG message, nint hwnd, uint filterMin, uint filterMax);

    [DllImport("user32.dll")]
    private static extern nint DispatchMessage(ref MSG message);

    [DllImport("user32.dll")]
    private static extern nint DefWindowProc(nint hwnd, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(nint hwnd, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int exitCode);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? moduleName);
}
