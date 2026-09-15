using System.Runtime.InteropServices;
using System.Windows.Threading;
using CrosshairOverlay.Core.Abstractions;
using CrosshairOverlay.Interop;
using Microsoft.Extensions.Logging;

namespace CrosshairOverlay.Services.Input;

/// <inheritdoc cref="ICursorVisibilityMonitor"/>
/// <remarks>
/// <para>
/// Theo SỰ KIỆN, không polling. Windows phát <c>EVENT_OBJECT_SHOW</c>/<c>EVENT_OBJECT_HIDE</c> với
/// <c>OBJID_CURSOR</c> mỗi khi con trỏ hiện/ẩn — đo thực tế trên máy: cả <c>ShowCursor(FALSE)</c> lẫn
/// <c>SetCursor(NULL)</c> (hai cách game hay dùng) đều báo ngay dưới 1 ms, và <c>GetCursorInfo</c> đọc
/// ngay lúc đó đã đúng trạng thái mới. Nhờ vậy không cần timer đánh thức CPU định kỳ trong trận,
/// và phản hồi tức thì thay vì trễ tới cả nửa giây.
/// </para>
/// <para>
/// Hook WINEVENT_OUTOFCONTEXT: không nạp gì vào tiến trình khác. Chỉ đăng ký khi tính năng đang bật.
/// </para>
/// </remarks>
public sealed class CursorVisibilityMonitor : ICursorVisibilityMonitor
{
    private readonly ILogger<CursorVisibilityMonitor> _logger;
    private readonly Dispatcher _dispatcher;
    private readonly WinEventProc _callback;
    private readonly Action _refresh;

    private nint _hook;
    private bool _disposed;

    public CursorVisibilityMonitor(ILogger<CursorVisibilityMonitor> logger)
    {
        _logger = logger;
        _dispatcher = Dispatcher.CurrentDispatcher;
        _callback = OnWinEvent;
        _refresh = Refresh;
    }

    public bool IsCursorVisible { get; private set; } = true;

    public event EventHandler<bool>? VisibilityChanged;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_hook != 0) return;

        _hook = NativeMethods.SetWinEventHook(
            Win32Constants.EVENT_OBJECT_SHOW, Win32Constants.EVENT_OBJECT_HIDE,
            0, _callback, 0, 0, Win32Constants.WINEVENT_OUTOFCONTEXT);

        if (_hook == 0)
        {
            _logger.LogWarning("Không theo dõi được con trỏ chuột (lỗi {Error}).", Marshal.GetLastWin32Error());
            return;
        }

        _logger.LogInformation("Bắt đầu theo dõi con trỏ chuột hiện/ẩn.");
        Refresh();
    }

    public void Stop()
    {
        if (_hook == 0) return;

        NativeMethods.UnhookWinEvent(_hook);
        _hook = 0;

        // Không theo dõi nữa thì không được để người nghe kẹt ở trạng thái "con trỏ đang ẩn".
        SetVisible(true);
        _logger.LogInformation("Dừng theo dõi con trỏ chuột.");
    }

    private void OnWinEvent(nint hook, uint eventType, nint hwnd, int idObject, int idChild, uint thread, uint time)
    {
        // Hook này nhận show/hide của MỌI đối tượng trên máy; chỉ quan tâm con trỏ, lọc ngay đầu.
        if (idObject != Win32Constants.OBJID_CURSOR) return;

        _dispatcher.BeginInvoke(DispatcherPriority.Input, _refresh);
    }

    private void Refresh()
    {
        if (_disposed || _hook == 0) return;
        SetVisible(ReadCursorVisible());
    }

    /// <summary>
    /// Hiện nghĩa là có cờ CURSOR_SHOWING VÀ có hình con trỏ: <c>SetCursor(NULL)</c> giữ nguyên cờ
    /// nhưng bỏ hình.
    /// </summary>
    internal static bool ReadCursorVisible()
    {
        var info = new NativeMethods.CURSORINFO { cbSize = Marshal.SizeOf<NativeMethods.CURSORINFO>() };
        if (!NativeMethods.GetCursorInfo(ref info)) return true;   // không đọc được: coi như hiện (an toàn)

        return (info.flags & Win32Constants.CURSOR_SHOWING) != 0 && info.hCursor != 0;
    }

    private void SetVisible(bool visible)
    {
        if (IsCursorVisible == visible) return;

        IsCursorVisible = visible;
        VisibilityChanged?.Invoke(this, visible);
    }

    public void Dispose()
    {
        if (_disposed) return;
        Stop();
        _disposed = true;
    }
}
