using System.Text;
using Microsoft.Win32.SafeHandles;
using System.Windows;
using System.Windows.Threading;
using CrosshairOverlay.Core.Abstractions;
using CrosshairOverlay.Core.Models;
using CrosshairOverlay.Interop;
using Microsoft.Extensions.Logging;

namespace CrosshairOverlay.Services.Process;

/// <inheritdoc cref="IForegroundWindowWatcher"/>
public sealed class ForegroundWindowWatcher : IForegroundWindowWatcher
{
    private const int MaxPathLength = 1024;

    private readonly IMonitorService _monitors;
    private readonly ILogger<ForegroundWindowWatcher> _logger;
    private readonly Dispatcher _dispatcher;

    /// <summary>
    /// Giữ delegate trong field là BẮT BUỘC. Windows chỉ lưu con trỏ hàm; nếu GC thu hồi
    /// delegate trong lúc hook còn sống, lần callback tiếp theo sẽ làm sập tiến trình.
    /// </summary>
    private readonly WinEventProc _callback;

    /// <summary>
    /// Buffer dùng lại thay vì cấp phát mới mỗi lần đổi cửa sổ.
    /// </summary>
    /// <remarks>
    /// Chỉ được chạm trên UI thread — mọi lối vào đều đi qua <see cref="Dispatcher"/> — nên
    /// không cần đồng bộ hoá.
    /// </remarks>
    private readonly StringBuilder _pathBuffer = new(MaxPathLength);
    private readonly StringBuilder _titleBuffer = new(256);

    /// <summary>Delegate đọc lại foreground, tạo sẵn để mỗi sự kiện không cấp phát closure mới.</summary>
    private readonly Action _refresh;
    private readonly Action _trackedWindowDestroyed;
    private readonly WaitOrTimerCallback _processExitCallback;

    private nint _hook;

    /// <summary>
    /// Hai hook chỉ nghe TIẾN TRÌNH của cửa sổ đang <see cref="Track"/>: một cho thu nhỏ/khôi phục,
    /// một cho huỷ/hiện/ẩn. Giới hạn theo tiến trình để không nhận sự kiện của mọi cửa sổ trên máy,
    /// và chỉ tồn tại khi đang có game khớp profile.
    /// </summary>
    private nint _trackMinimizeHook;
    private nint _trackObjectHook;
    private ForegroundWindowInfo _tracked = ForegroundWindowInfo.Empty;

    /// <summary>
    /// Chờ tiến trình đang Track thoát. Cần thiết vì game (và cả Notepad trên Windows 11) thường
    /// thoát thẳng tiến trình mà không huỷ cửa sổ theo cách thông thường, nên không có
    /// <c>EVENT_OBJECT_DESTROY</c> nào bắn ra.
    /// </summary>
    /// <remarks>
    /// Đợi bằng wait handle của kernel trên thread pool — không polling, không tốn CPU khi game chạy.
    /// </remarks>
    private ProcessExitWaitHandle? _exitHandle;
    private RegisteredWaitHandle? _exitRegistration;

    private bool _disposed;

    public ForegroundWindowWatcher(IMonitorService monitors, ILogger<ForegroundWindowWatcher> logger)
    {
        _monitors = monitors;
        _logger = logger;
        _dispatcher = Dispatcher.CurrentDispatcher;
        _callback = OnWinEvent;
        _refresh = Refresh;
        _trackedWindowDestroyed = OnTrackedWindowDestroyed;
        _processExitCallback = OnTrackedProcessExitSignaled;
        Current = ForegroundWindowInfo.Empty;
        LastExternal = ForegroundWindowInfo.Empty;
    }

    public ForegroundWindowInfo Current { get; private set; }

    public ForegroundWindowInfo LastExternal { get; private set; }

    public event EventHandler<ForegroundWindowChangedEventArgs>? ForegroundChanged;

    public event EventHandler<ForegroundWindowInfo>? TrackedWindowClosed;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_hook != 0) return;

        _hook = NativeMethods.SetWinEventHook(
            Win32Constants.EVENT_SYSTEM_FOREGROUND,
            Win32Constants.EVENT_SYSTEM_FOREGROUND,
            0,
            _callback,
            0,
            0,
            // OUTOFCONTEXT: callback chạy trong tiến trình này, không nạp DLL vào game.
            //
            // Cố tình KHÔNG dùng SKIPOWNPROCESS. Bỏ qua cửa sổ của chính mình nghe có vẻ gọn,
            // nhưng khi người dùng đóng game rồi focus quay về cửa sổ Settings thì sẽ không có
            // sự kiện nào bắn ra, và profile game bị kẹt lại mãi. Phía nhận tự phân biệt cửa
            // sổ của mình qua ForegroundWindowInfo.IsOwnProcess.
            Win32Constants.WINEVENT_OUTOFCONTEXT);

        if (_hook == 0)
        {
            _logger.LogError("SetWinEventHook thất bại — sẽ không tự đổi preset theo game được.");
            return;
        }

        _logger.LogInformation("Đã bắt đầu theo dõi cửa sổ foreground.");

        // Đọc trạng thái hiện tại: hook chỉ báo khi CÓ THAY ĐỔI, mà game có thể đã chạy sẵn.
        Refresh();
    }

    public void Stop()
    {
        Untrack();

        if (_hook == 0) return;

        NativeMethods.UnhookWinEvent(_hook);
        _hook = 0;
        _logger.LogInformation("Đã dừng theo dõi cửa sổ foreground.");
    }

    public void Track(ForegroundWindowInfo window)
    {
        if (_disposed || _hook == 0) return;

        if (!window.IsValid || window.ProcessId == 0)
        {
            Untrack();
            return;
        }

        // Cùng tiến trình: hook cũ vẫn dùng được, chỉ đổi cửa sổ cần để ý (game đổi cửa sổ chính
        // khi chuyển chế độ hiển thị, splash → cửa sổ game...).
        if (window.ProcessId != _tracked.ProcessId)
        {
            Untrack();

            var processId = (uint)window.ProcessId;
            _trackMinimizeHook = NativeMethods.SetWinEventHook(
                Win32Constants.EVENT_SYSTEM_MINIMIZESTART, Win32Constants.EVENT_SYSTEM_MINIMIZEEND,
                0, _callback, processId, 0, Win32Constants.WINEVENT_OUTOFCONTEXT);
            _trackObjectHook = NativeMethods.SetWinEventHook(
                Win32Constants.EVENT_OBJECT_DESTROY, Win32Constants.EVENT_OBJECT_HIDE,
                0, _callback, processId, 0, Win32Constants.WINEVENT_OUTOFCONTEXT);

            if (_trackObjectHook == 0)
            {
                // Không chết người: vẫn còn sự kiện foreground, chỉ là thoát game xong có thể phải
                // Alt-Tab thì crosshair mới ẩn.
                _logger.LogWarning("Không theo dõi được việc đóng cửa sổ của {Process}.", window.ProcessName);
            }

            WaitForExit(window);
        }

        _tracked = window;
    }

    private void WaitForExit(ForegroundWindowInfo window)
    {
        var handle = NativeMethods.OpenProcess(Win32Constants.SYNCHRONIZE, false, (uint)window.ProcessId);
        if (handle == 0)
        {
            // Tiến trình được bảo vệ có thể từ chối; vẫn còn sự kiện foreground và huỷ cửa sổ.
            _logger.LogDebug("Không chờ được {Process} thoát.", window.ProcessName);
            return;
        }

        _exitHandle = new ProcessExitWaitHandle(handle);
        _exitRegistration = ThreadPool.RegisterWaitForSingleObject(
            _exitHandle, _processExitCallback, window.ProcessId, Timeout.Infinite, executeOnlyOnce: true);
    }

    /// <summary>Chạy trên thread pool khi tiến trình thoát.</summary>
    private void OnTrackedProcessExitSignaled(object? state, bool timedOut)
    {
        var processId = (int)state!;
        _dispatcher.BeginInvoke(() => OnTrackedProcessExited(processId));
    }

    private void OnTrackedProcessExited(int processId)
    {
        // Có thể đã chuyển sang theo dõi tiến trình khác trong lúc chờ lên UI thread.
        if (_disposed || _tracked.ProcessId != processId) return;

        CloseTracked("tiến trình đã thoát");
    }

    private void Untrack()
    {
        if (_trackMinimizeHook != 0) NativeMethods.UnhookWinEvent(_trackMinimizeHook);
        if (_trackObjectHook != 0) NativeMethods.UnhookWinEvent(_trackObjectHook);

        _trackMinimizeHook = 0;
        _trackObjectHook = 0;

        // Unregister trước rồi mới đóng handle mà thread pool đang chờ.
        _exitRegistration?.Unregister(null);
        _exitRegistration = null;
        _exitHandle?.Dispose();
        _exitHandle = null;

        _tracked = ForegroundWindowInfo.Empty;
    }

    private void OnWinEvent(
        nint hook, uint eventType, nint hwnd, int idObject, int idChild, uint thread, uint time)
    {
        // Sự kiện của một phần tử bên trong cửa sổ (con trỏ, caret, control con), không phải cửa sổ.
        if (idObject != Win32Constants.OBJID_WINDOW || idChild != Win32Constants.CHILDID_SELF) return;

        // Mọi thứ đưa về UI thread qua BeginInvoke thay vì xử lý ngay trong callback: phía nhận
        // đụng tới overlay và thư viện preset, không nên chạy lồng trong lúc hệ thống đang báo sự kiện.
        if (eventType == Win32Constants.EVENT_SYSTEM_FOREGROUND)
        {
            // Không dùng hwnd của sự kiện: đọc lại foreground lúc xử lý để luôn lấy trạng thái mới
            // nhất, và coi cửa sổ đã ẩn/thu nhỏ là "không có foreground".
            _dispatcher.BeginInvoke(_refresh);
            return;
        }

        // Còn lại là sự kiện từ tiến trình đang Track — tiến trình đó có thể có nhiều cửa sổ khác.
        // WINEVENT_OUTOFCONTEXT gọi callback trên chính thread đã đặt hook (UI thread), nên đọc
        // _tracked ở đây là an toàn.
        if (hwnd == 0 || hwnd != _tracked.Handle) return;

        _dispatcher.BeginInvoke(eventType == Win32Constants.EVENT_OBJECT_DESTROY
            ? _trackedWindowDestroyed
            : _refresh);
    }

    private void OnTrackedWindowDestroyed()
    {
        if (_disposed) return;

        if (!_tracked.IsValid || NativeMethods.IsWindow(_tracked.Handle)) return;

        CloseTracked("cửa sổ đã đóng");
    }

    private void CloseTracked(string reason)
    {
        var closed = _tracked;
        Untrack();

        _logger.LogDebug("Ngừng theo dõi {Process}: {Reason}.", closed.ProcessName, reason);

        TrackedWindowClosed?.Invoke(this, closed);
        Refresh();
    }

    /// <summary>
    /// Đọc lại cửa sổ foreground. Cửa sổ đã huỷ, bị ẩn hay đang thu nhỏ được coi là không có
    /// foreground: <c>GetForegroundWindow</c> vẫn có thể trả nó về khi không cửa sổ nào khác nhận
    /// foreground, nhưng game ở trạng thái đó không còn trên màn hình.
    /// </summary>
    private void Refresh()
    {
        if (_disposed) return;

        var hwnd = NativeMethods.GetForegroundWindow();
        Update(IsOnScreen(hwnd) ? hwnd : 0);
    }

    private static bool IsOnScreen(nint hwnd) =>
        hwnd != 0
        && NativeMethods.IsWindow(hwnd)
        && NativeMethods.IsWindowVisible(hwnd)
        && !NativeMethods.IsIconic(hwnd);

    private void Update(nint hwnd)
    {
        if (_disposed) return;

        try
        {
            var info = Describe(hwnd);
            if (info.Handle == Current.Handle && info.ProcessId == Current.ProcessId) return;

            var previous = Current;
            Current = info;

            if (info.IsValid && !info.IsOwnProcess) LastExternal = info;

            _logger.LogDebug(
                "Foreground: {Process} — '{Title}' ({Fullscreen})",
                info.ProcessName, info.WindowTitle, info.Fullscreen);

            ForegroundChanged?.Invoke(this, new ForegroundWindowChangedEventArgs(previous, info));
        }
        catch (Exception ex)
        {
            // Cửa sổ có thể biến mất ngay giữa lúc ta đang đọc thông tin về nó.
            _logger.LogDebug(ex, "Không đọc được thông tin cửa sổ foreground.");
        }
    }

    private ForegroundWindowInfo Describe(nint hwnd)
    {
        if (hwnd == 0 || !NativeMethods.IsWindow(hwnd)) return ForegroundWindowInfo.Empty;

        NativeMethods.GetWindowThreadProcessId(hwnd, out var processId);

        var executablePath = TryGetExecutablePath(processId);
        var processName = executablePath is null
            ? string.Empty
            : global::System.IO.Path.GetFileName(executablePath);

        var bounds = NativeMethods.GetWindowRect(hwnd, out var rect) ? rect.ToRect() : Rect.Empty;

        var monitor = _monitors.GetMonitorFromWindow(hwnd);
        var fullscreen = monitor is { } m
            ? FullscreenDetector.Detect(hwnd, bounds, m.Bounds)
            : FullscreenKind.None;

        return new ForegroundWindowInfo(
            Handle: hwnd,
            ProcessId: (int)processId,
            ProcessName: processName,
            ExecutablePath: executablePath,
            WindowTitle: TryGetWindowTitle(hwnd),
            Bounds: bounds,
            Fullscreen: fullscreen);
    }

    private string TryGetWindowTitle(nint hwnd)
    {
        var length = NativeMethods.GetWindowTextLength(hwnd);
        if (length <= 0) return string.Empty;

        _titleBuffer.Clear();
        _titleBuffer.EnsureCapacity(length + 1);
        NativeMethods.GetWindowText(hwnd, _titleBuffer, _titleBuffer.Capacity);
        return _titleBuffer.ToString();
    }

    private string? TryGetExecutablePath(uint processId)
    {
        if (processId == 0) return null;

        // PROCESS_QUERY_LIMITED_INFORMATION là quyền tối thiểu đủ đọc tên file thực thi.
        // Tiến trình được anti-cheat bảo vệ vẫn có thể từ chối — đó là kết quả bình thường,
        // không phải lỗi, nên chỉ log ở mức Debug.
        var handle = NativeMethods.OpenProcess(
            Win32Constants.PROCESS_QUERY_LIMITED_INFORMATION, false, processId);

        if (handle == 0)
        {
            _logger.LogDebug("Không mở được tiến trình {ProcessId} để đọc tên file.", processId);
            return null;
        }

        try
        {
            _pathBuffer.Clear();
            _pathBuffer.EnsureCapacity(MaxPathLength);
            var size = (uint)_pathBuffer.Capacity;

            return NativeMethods.QueryFullProcessImageName(handle, 0, _pathBuffer, ref size)
                ? _pathBuffer.ToString()
                : null;
        }
        finally
        {
            NativeMethods.CloseHandle(handle);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }

    /// <summary>Bọc handle tiến trình (chỉ quyền SYNCHRONIZE) để thread pool chờ được.</summary>
    private sealed class ProcessExitWaitHandle : WaitHandle
    {
        public ProcessExitWaitHandle(nint handle) => SafeWaitHandle = new SafeWaitHandle(handle, ownsHandle: true);
    }
}
