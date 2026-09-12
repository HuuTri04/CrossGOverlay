using System.Text;
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

    private nint _hook;
    private bool _disposed;

    public ForegroundWindowWatcher(IMonitorService monitors, ILogger<ForegroundWindowWatcher> logger)
    {
        _monitors = monitors;
        _logger = logger;
        _dispatcher = Dispatcher.CurrentDispatcher;
        _callback = OnWinEvent;
        Current = ForegroundWindowInfo.Empty;
        LastExternal = ForegroundWindowInfo.Empty;
    }

    public ForegroundWindowInfo Current { get; private set; }

    public ForegroundWindowInfo LastExternal { get; private set; }

    public event EventHandler<ForegroundWindowChangedEventArgs>? ForegroundChanged;

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
        Update(NativeMethods.GetForegroundWindow());
    }

    public void Stop()
    {
        if (_hook == 0) return;

        NativeMethods.UnhookWinEvent(_hook);
        _hook = 0;
        _logger.LogInformation("Đã dừng theo dõi cửa sổ foreground.");
    }

    private void OnWinEvent(
        nint hook, uint eventType, nint hwnd, int idObject, int idChild, uint thread, uint time)
    {
        // idObject != OBJID_WINDOW (0) là sự kiện của control con, không phải cửa sổ.
        if (eventType != Win32Constants.EVENT_SYSTEM_FOREGROUND || idObject != 0 || hwnd == 0) return;

        // Callback tới trên thread tuỳ ý của hệ thống; đưa về UI thread vì phía nhận sẽ
        // đụng tới overlay và thư viện preset.
        _dispatcher.BeginInvoke(() => Update(hwnd));
    }

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
}
