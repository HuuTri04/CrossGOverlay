using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using CrosshairOverlay.Core.Abstractions;
using CrosshairOverlay.Core.Models;
using CrosshairOverlay.Core.Threading;
using CrosshairOverlay.Interop;
using Microsoft.Extensions.Logging;

namespace CrosshairOverlay.Services.Overlay;

/// <inheritdoc cref="IOverlayController"/>
public sealed class OverlayController : IOverlayController
{
    /// <summary>
    /// Chu kỳ khẳng định lại vị trí topmost. Game vào fullscreen hoặc app khác bật topmost
    /// có thể đẩy overlay xuống dưới; đây là cách sửa rẻ và thụ động — không đụng gì tới game.
    /// </summary>
    private static readonly TimeSpan TopmostReassertInterval = TimeSpan.FromSeconds(1.5);

    private readonly ICrosshairRenderer _renderer;
    private readonly IMonitorService _monitors;
    private readonly ILogger<OverlayController> _logger;
    private readonly Dispatcher _dispatcher;

    private readonly RenderThrottle _rebuildThrottle;

    private OverlayWindow? _window;
    /// <summary>
    /// Timer của thread pool, không phải DispatcherTimer.
    /// </summary>
    /// <remarks>
    /// Đo thực tế: DispatcherTimer 1,5 giây, dù mỗi nhịp không làm gì, vẫn chiếm phần lớn chi phí
    /// CPU của overlay lúc rảnh — mỗi nhịp đánh thức luồng giao diện WPF và kéo theo một vòng xử
    /// lý của nó. Kiểm tra thứ tự z chỉ là vài lời gọi Win32 đọc trạng thái, làm được từ luồng bất
    /// kỳ; SetWindowPos lên cửa sổ của luồng khác cũng hợp lệ. Luồng giao diện giờ không bị đánh
    /// thức trừ khi thật sự có cửa sổ đè lên overlay.
    /// </remarks>
    private Timer? _topmostTimer;

    /// <summary>HWND của overlay, đọc từ thread pool — chỉ gán một lần lúc khởi tạo.</summary>
    private nint _overlayHandle;

    /// <summary>Đọc từ thread pool nên phải volatile.</summary>
    private volatile bool _topmostActive;

    private CrosshairProfile? _profile;
    private MonitorInfo? _monitor;
    private MonitorSelectionMode _selectionMode = MonitorSelectionMode.FollowForegroundWindow;
    private string? _targetDeviceName;

    private global::System.Windows.Media.Color? _colorOverride;

    /// <summary>Dịch chống lưu ảnh, physical pixel.</summary>
    private int _shiftX;
    private int _shiftY;

    /// <summary>Kích thước hình lần dựng gần nhất (DIP) — để đặt lại vị trí mà không phải dựng lại.</summary>
    private Size _lastContentSize;

    private bool _visible;
    private bool _suppressed;
    private bool _placing;
    private bool _disposed;

    public OverlayController(
        ICrosshairRenderer renderer,
        IMonitorService monitors,
        ILogger<OverlayController> logger)
    {
        _renderer = renderer;
        _monitors = monitors;
        _logger = logger;
        _dispatcher = Dispatcher.CurrentDispatcher;
        _rebuildThrottle = new RenderThrottle(_dispatcher, Rebuild);
    }

    public bool IsVisible => _visible;

    public CrosshairProfile? CurrentProfile => _profile;

    public MonitorInfo? CurrentMonitor => _monitor;

    public event EventHandler<OverlayVisibilityChangedEventArgs>? VisibilityChanged;

    public void Initialize()
    {
        ThrowIfDisposed();
        if (_window is not null) return;

        var watch = global::System.Diagnostics.Stopwatch.StartNew();

        _window = new OverlayWindow();
        var createdMs = watch.Elapsed.TotalMilliseconds;

        _window.EnsureHandle();          // áp extended styles TRƯỚC lần Show đầu tiên
        var handleMs = watch.Elapsed.TotalMilliseconds;
        _window.DpiChanged += OnWindowDpiChanged;

        _monitors.DisplayConfigurationChanged += OnDisplayConfigurationChanged;
        var monitorsMs = watch.Elapsed.TotalMilliseconds;

        _overlayHandle = _window.Handle;
        _topmostTimer = new Timer(OnTopmostTimerTick, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

        _logger.LogDebug(
            "Khởi tạo overlay: dựng Window {Create:0} ms, tạo HWND {Handle:0} ms, theo dõi màn hình {Monitors:0} ms.",
            createdMs, handleMs - createdMs, monitorsMs - handleMs);

        _logger.LogInformation("Cửa sổ overlay đã khởi tạo, HWND={Handle:X}.", _window.Handle);
    }

    public void SetProfile(CrosshairProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ThrowIfDisposed();

        if (ReferenceEquals(_profile, profile)) return;

        if (_profile is not null) HookProfile(_profile, subscribe: false);
        _profile = profile;
        HookProfile(_profile, subscribe: true);

        _logger.LogDebug("Overlay đổi sang preset '{Name}' ({Id}).", profile.Name, profile.Id);
        Invalidate();
    }

    public void SetVisible(bool visible)
    {
        ThrowIfDisposed();
        if (_window is null) Initialize();
        if (_visible == visible) return;

        _visible = visible;

        if (visible)
        {
            // ShowActivated=false + WS_EX_NOACTIVATE ⇒ Show() không kéo focus khỏi game.
            _window!.Show();
            Invalidate();

            // Bật overlay là hành động rời rạc, không phải cú kéo liên tục: bỏ qua bộ chặn
            // nhịp để crosshair hiện ra đúng hình ngay, không chớp một khung hình trống.
            _rebuildThrottle.Flush();

            _topmostActive = true;
            _topmostTimer!.Change(TopmostReassertInterval, TopmostReassertInterval);
        }
        else
        {
            _topmostActive = false;
            _topmostTimer!.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _window!.Hide();

            // Bỏ hình đang hiện: nếu là GIF động, đồng hồ animation của nó sẽ chạy ngầm suốt thời
            // gian overlay tắt. Hiện lại thì Invalidate ở nhánh trên dựng hình mới.
            _window.Host.SetDrawing(null);
        }

        _logger.LogDebug("Overlay {State}.", visible ? "hiện" : "ẩn");
        VisibilityChanged?.Invoke(this, new OverlayVisibilityChangedEventArgs(visible));
    }

    public void Toggle() => SetVisible(!_visible);

    public void SetSuppressed(bool suppressed)
    {
        ThrowIfDisposed();
        if (_suppressed == suppressed) return;

        _suppressed = suppressed;
        if (_window is not null) _window.Host.Opacity = suppressed ? 0d : 1d;

        _logger.LogDebug("Overlay {State} tạm thời.", suppressed ? "ẩn" : "hiện lại");
    }

    public void SetFrameRateLimit(int framesPerSecond)
    {
        ThrowIfDisposed();

        var interval = FrameInterval(framesPerSecond);
        _rebuildThrottle.Interval = interval;
        Rendering.DrawingAnimations.MinFrameInterval = interval;

        _logger.LogInformation(
            "Giới hạn khung hình overlay: {Limit}.", framesPerSecond > 0 ? $"{framesPerSecond} FPS" : "không giới hạn");
    }

    public void SetColorOverride(global::System.Windows.Media.Color? color)
    {
        ThrowIfDisposed();
        if (Nullable.Equals(_colorOverride, color)) return;

        _colorOverride = color;

        // Ảnh không có "màu" để đổi; dựng lại chỉ khởi động lại GIF mỗi lần bấm chuột.
        if (_profile is null || _profile.Type == CrosshairType.Image) return;

        Invalidate();

        // Bấm/nhả chuột là sự kiện rời rạc, người chơi phải thấy màu đổi ngay khung hình kế tiếp chứ
        // không đợi nhịp của bộ chặn (vốn dành cho kéo thanh trượt liên tục).
        _rebuildThrottle.Flush();
    }

    public void SetPixelShift(int dx, int dy)
    {
        ThrowIfDisposed();
        if (_shiftX == dx && _shiftY == dy) return;

        _shiftX = dx;
        _shiftY = dy;

        // Chưa từng dựng hình (hoặc đang ẩn): lần dựng tới tự cộng độ dịch mới vào.
        if (_visible && _monitor is { } monitor && _lastContentSize.Width > 0)
            ApplyPlacement(monitor, _lastContentSize);

        _logger.LogDebug("Dịch overlay chống lưu ảnh: ({X}, {Y}) px.", dx, dy);
    }

    /// <summary>1000 ms / FPS (60 FPS ≈ 16,7 ms); không giới hạn là 0.</summary>
    internal static TimeSpan FrameInterval(int framesPerSecond) =>
        framesPerSecond > 0 ? TimeSpan.FromMilliseconds(1000d / framesPerSecond) : TimeSpan.Zero;

    public void MoveToMonitor(MonitorInfo monitor)
    {
        ThrowIfDisposed();
        _monitor = monitor;
        _selectionMode = MonitorSelectionMode.Specific;
        _targetDeviceName = monitor.DeviceName;
        Invalidate();
    }

    public void SetMonitorSelection(MonitorSelectionMode mode, string? targetDeviceName)
    {
        ThrowIfDisposed();
        _selectionMode = mode;
        _targetDeviceName = targetDeviceName;
        Invalidate();
    }

    /// <summary>
    /// Báo rằng hình đã cũ và cần dựng lại.
    /// </summary>
    /// <remarks>
    /// Dựng lại KHÔNG rẻ: đo hình, dựng lại toàn bộ Geometry rồi gọi <c>SetWindowPos</c>. Kéo
    /// một thanh trượt có thể bắn ra cả nghìn yêu cầu mỗi giây, nên chúng đi qua bộ chặn theo
    /// nhịp khung hình — xem <see cref="RenderThrottle"/> để biết vì sao xếp hàng ở
    /// <see cref="DispatcherPriority.Render"/> không đủ.
    /// </remarks>
    public void Invalidate()
    {
        if (_disposed || _window is null || _profile is null) return;

        _rebuildThrottle.Request();
    }

    // ------------------------------------------------------------------ nội bộ

    private void Rebuild()
    {
        if (_disposed || _window is null || _profile is null) return;

        // Đang ẩn: không ai nhìn thấy hình này, dựng ra chỉ tốn công (và với GIF là khởi động một
        // đồng hồ animation vô ích). SetVisible(true) luôn dựng lại khi hiện.
        if (!_visible) return;

        try
        {
            var monitor = ResolveMonitor();
            _monitor = monitor;

            var options = new CrosshairRenderOptions(
                DpiScale: monitor.DpiScaleX,
                // Snapping làm méo hình đã xoay, nên chỉ bật khi crosshair không xoay.
                SnapToPixels: Math.Abs(_profile.Rotation) < 0.01,
                MaxExtent: Math.Max(
                    monitor.Bounds.Width / monitor.DpiScaleX,
                    monitor.Bounds.Height / monitor.DpiScaleY),
                ColorOverride: _colorOverride);

            var contentSize = _renderer.Measure(_profile, options);
            var drawing = _renderer.Build(_profile, options);

            _window.Host.SetAliasing(_renderer.PrefersAliasedEdges(_profile));
            _window.Host.SetDrawing(drawing);
            _lastContentSize = contentSize;
            ApplyPlacement(monitor, contentSize);
        }
        catch (Exception ex)
        {
            // Preset lỗi không được phép làm sập app — overlay chỉ đơn giản giữ hình cũ.
            _logger.LogError(ex, "Dựng lại overlay thất bại cho preset '{Name}'.", _profile.Name);
        }
    }

    private MonitorInfo ResolveMonitor()
    {
        var foreground = NativeMethods.GetForegroundWindow();

        // Chính overlay không bao giờ là foreground (WS_EX_NOACTIVATE), nhưng cửa sổ Settings
        // thì có thể — và đó là hành vi mong muốn khi người dùng đang chỉnh preset.
        return _monitors.ResolveTargetMonitor(_selectionMode, _targetDeviceName, foreground);
    }

    /// <summary>
    /// Đặt cửa sổ bằng PHYSICAL pixel qua <c>SetWindowPos</c>.
    /// </summary>
    /// <remarks>
    /// Cố tình không dùng <c>Window.Left/Top/Width/Height</c>: WPF diễn giải các giá trị đó
    /// theo DPI của màn hình chính, nên trên setup multi-monitor có DPI khác nhau overlay sẽ
    /// bị đặt lệch. Toạ độ Win32 thì luôn là physical pixel trong virtual desktop.
    /// </remarks>
    private void ApplyPlacement(MonitorInfo monitor, Size contentSizeDip)
    {
        if (_window is null || _placing) return;

        var hwnd = _window.Handle;
        if (hwnd == 0 || !NativeMethods.IsWindow(hwnd)) return;

        // Math.Round chứ không phải Ceiling: renderer đã trả về kích thước ứng với một số
        // CHẴN device pixel, làm tròn lên sẽ cộng thừa 1 px do sai số dấu phẩy động và đẩy
        // tâm crosshair lệch nửa pixel.
        var width = (int)Math.Round(contentSizeDip.Width * monitor.DpiScaleX);
        var height = (int)Math.Round(contentSizeDip.Height * monitor.DpiScaleY);
        if (width <= 0 || height <= 0) return;

        var center = monitor.PhysicalCenter;
        // Độ lệch áp bằng cách DỊCH CỬA SỔ, không bằng TranslateTransform bên trong nó. Dịch hình
        // bên trong buộc cửa sổ phải phình ra gấp đôi độ lệch để chứa đủ — lệch 500 px là một
        // cửa sổ trong suốt hơn 1000 px mà Windows phải tổng hợp lại mỗi khung hình, trong khi
        // phần có hình chỉ vài chục pixel. Chế độ ảnh dùng độ lệch riêng của ảnh.
        var offsetX = (_profile?.EffectiveOffsetX ?? 0d) * monitor.DpiScaleX;
        var offsetY = (_profile?.EffectiveOffsetY ?? 0d) * monitor.DpiScaleY;

        // Độ dịch chống lưu ảnh cộng SAU khi làm tròn: đúng 1 pixel vật lý, không bị làm tròn mất.
        var x = (int)Math.Round(center.X - (width / 2d) + offsetX) + _shiftX;
        var y = (int)Math.Round(center.Y - (height / 2d) + offsetY) + _shiftY;

        _placing = true;
        try
        {
            var flags = Win32Constants.SWP_NOACTIVATE | Win32Constants.SWP_NOOWNERZORDER;
            if (!NativeMethods.SetWindowPos(hwnd, Win32Constants.HWND_TOPMOST, x, y, width, height, flags))
            {
                _logger.LogWarning(
                    "SetWindowPos thất bại (Win32 error {Error}).", Marshal.GetLastWin32Error());
            }
        }
        finally
        {
            _placing = false;
        }
    }

    private void OnTopmostTimerTick(object? state)
    {
        if (!_topmostActive) return;

        var hwnd = _overlayHandle;
        if (hwnd == 0 || !NativeMethods.IsWindow(hwnd)) return;

        // Không có gì đè lên thì không làm gì. Gọi SetWindowPos vô điều kiện khiến cửa sổ layered
        // nhận WM_WINDOWPOSCHANGED và bị xử lý lại mỗi 1,5 giây dù không có gì thay đổi — đo thực
        // tế đó là phần lớn chi phí của overlay lúc rảnh.
        if (!IsCoveredByAnotherWindow(hwnd)) return;

        NativeMethods.SetWindowPos(
            hwnd,
            Win32Constants.HWND_TOPMOST,
            0, 0, 0, 0,
            Win32Constants.SWP_NOMOVE | Win32Constants.SWP_NOSIZE
                | Win32Constants.SWP_NOACTIVATE | Win32Constants.SWP_NOOWNERZORDER);
    }

    /// <summary>
    /// Có cửa sổ hiển thị nào nằm TRÊN overlay trong thứ tự z và chồng lên vùng của nó không.
    /// </summary>
    /// <remarks>
    /// Overlay là topmost, nên thứ nằm trên nó chỉ có thể là cửa sổ topmost khác — thường rất ít,
    /// nên duyệt ngược thứ tự z rất rẻ. Giới hạn số bước cho chắc; vượt giới hạn thì coi như bị đè
    /// và quay về hành vi cũ, không bao giờ bỏ sót.
    /// </remarks>
    internal static bool IsCoveredByAnotherWindow(nint hwnd)
    {
        if (!NativeMethods.GetWindowRect(hwnd, out var mine)) return true;

        var above = NativeMethods.GetWindow(hwnd, Win32Constants.GW_HWNDPREV);
        for (var steps = 0; above != 0; steps++)
        {
            if (steps > 256) return true;

            if (NativeMethods.IsWindowVisible(above)
                && NativeMethods.GetWindowRect(above, out var other)
                && other.Left < mine.Right && mine.Left < other.Right
                && other.Top < mine.Bottom && mine.Top < other.Bottom)
            {
                return true;
            }

            above = NativeMethods.GetWindow(above, Win32Constants.GW_HWNDPREV);
        }

        return false;
    }

    private void OnDisplayConfigurationChanged(object? sender, EventArgs e)
    {
        _logger.LogDebug("Cấu hình màn hình đổi, canh lại overlay.");
        Invalidate();
    }

    private void OnWindowDpiChanged(object sender, DpiChangedEventArgs e)
    {
        // Cửa sổ vừa sang màn hình có DPI khác. Bỏ qua rect gợi ý của Windows và tự canh lại
        // theo tâm màn hình đích — _placing chặn vòng lặp đệ quy.
        if (_placing) return;

        _logger.LogDebug(
            "DPI đổi {Old} → {New}, canh lại overlay.",
            e.OldDpi.DpiScaleX, e.NewDpi.DpiScaleX);

        // Không cần làm mới cache màn hình: DPI của từng màn hình không đổi, chỉ có việc
        // cửa sổ chuyển sang màn hình khác. Cache chỉ bị xoá khi DisplaySettingsChanged bắn.
        Invalidate();
    }

    private void HookProfile(CrosshairProfile profile, bool subscribe) =>
        ProfileNotifications.Hook(profile, OnProfilePropertyChanged, subscribe);

    private void OnProfilePropertyChanged(object? sender, PropertyChangedEventArgs e) => Invalidate();

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _monitors.DisplayConfigurationChanged -= OnDisplayConfigurationChanged;
        _rebuildThrottle.Dispose();

        if (_topmostTimer is not null)
        {
            _topmostActive = false;
            _topmostTimer.Dispose();
            _topmostTimer = null;
        }

        if (_profile is not null)
        {
            HookProfile(_profile, subscribe: false);
            _profile = null;
        }

        if (_window is not null)
        {
            _window.DpiChanged -= OnWindowDpiChanged;
            _window.Close();
            _window = null;
        }
    }
}
