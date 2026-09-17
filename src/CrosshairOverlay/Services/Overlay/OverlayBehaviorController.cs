using System.ComponentModel;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using CrosshairOverlay.Core.Abstractions;
using CrosshairOverlay.Core.Models;
using Microsoft.Extensions.Logging;

namespace CrosshairOverlay.Services.Overlay;

/// <summary>
/// Áp các tuỳ chọn "Hiệu năng" và "Hành vi nâng cao" lên overlay: giới hạn FPS, tăng tốc phần cứng,
/// ẩn khi giữ chuột phải, chỉ hiện khi con trỏ bị ẩn, đổi màu khi bắn, dịch pixel chống lưu ảnh OLED.
/// </summary>
/// <remarks>
/// Ẩn TẠM THỜI chồng lên quy tắc hiện/ẩn của <see cref="IProfileAutoSwitcher"/>, không thay nó: overlay
/// vẫn "đang hiện" theo game profile, chỉ là hình bị ẩn khi đang ngắm hay khi con trỏ đang hiện.
/// Thiết bị theo dõi (Raw Input nút phải, hook con trỏ) chỉ chạy khi tuỳ chọn tương ứng đang bật.
/// </remarks>
public sealed class OverlayBehaviorController : IDisposable
{
    private readonly IAppSettingsService _settings;
    private readonly IOverlayController _overlay;
    private readonly IHotkeyService _hotkeys;
    private readonly ICursorVisibilityMonitor _cursor;
    private readonly IProfileAutoSwitcher _autoSwitcher;
    private readonly ILogger<OverlayBehaviorController> _logger;

    /// <summary>
    /// Chu kỳ dịch chống lưu ảnh. Vài phút là đủ: lưu ảnh OLED tích tụ theo hàng giờ, còn dịch dày hơn thì
    /// người chơi dễ bắt gặp tâm ngắm "nhảy" giữa trận.
    /// </summary>
    internal static readonly TimeSpan PixelShiftInterval = TimeSpan.FromMinutes(2);

    private bool _rightHeld;
    private bool _leftHeld;

    private DispatcherTimer? _pixelShiftTimer;
    private int _pixelShiftStep;

    private bool _started;
    private bool _disposed;

    public OverlayBehaviorController(
        IAppSettingsService settings,
        IOverlayController overlay,
        IHotkeyService hotkeys,
        ICursorVisibilityMonitor cursor,
        IProfileAutoSwitcher autoSwitcher,
        ILogger<OverlayBehaviorController> logger)
    {
        _settings = settings;
        _overlay = overlay;
        _hotkeys = hotkeys;
        _cursor = cursor;
        _autoSwitcher = autoSwitcher;
        _logger = logger;
    }

    /// <summary>
    /// Chế độ render của WPF cho CẢ tiến trình. Gọi sớm lúc khởi động, trước khi dựng cửa sổ nào.
    /// </summary>
    /// <remarks>
    /// WPF chỉ có hai giá trị: <see cref="RenderMode.Default"/> (dùng GPU khi có) và
    /// <see cref="RenderMode.SoftwareOnly"/> — không có "HardwareOnly". Tắt tăng tốc thì mọi thứ vẽ
    /// bằng CPU, không tranh GPU với game.
    /// </remarks>
    public static void ApplyRenderMode(bool useHardwareAcceleration) =>
        RenderOptions.ProcessRenderMode = useHardwareAcceleration ? RenderMode.Default : RenderMode.SoftwareOnly;

    /// <summary>
    /// Đặt mức ưu tiên CPU cho tiến trình. Normal và High không cần quyền admin (chỉ Realtime mới cần).
    /// </summary>
    /// <returns>false nếu hệ điều hành từ chối — không ném exception.</returns>
    public static bool ApplyProcessPriority(ProcessPriorityMode mode)
    {
        try
        {
            using var process = global::System.Diagnostics.Process.GetCurrentProcess();
            process.PriorityClass = mode == ProcessPriorityMode.High
                ? global::System.Diagnostics.ProcessPriorityClass.High
                : global::System.Diagnostics.ProcessPriorityClass.Normal;
            return true;
        }
        catch (Exception ex) when (ex is global::System.ComponentModel.Win32Exception or InvalidOperationException
                                       or PlatformNotSupportedException or NotSupportedException)
        {
            return false;
        }
    }

    private void ApplyPriority()
    {
        var mode = _settings.Current.ProcessPriority;
        if (ApplyProcessPriority(mode)) _logger.LogInformation("Mức ưu tiên tiến trình: {Mode}.", mode);
        else _logger.LogWarning("Windows từ chối đặt mức ưu tiên {Mode}.", mode);
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started) return;
        _started = true;

        _settings.Current.PropertyChanged += OnSettingChanged;
        _hotkeys.RightButtonChanged += OnRightButtonChanged;
        _hotkeys.LeftButtonChanged += OnLeftButtonChanged;
        _cursor.VisibilityChanged += OnCursorVisibilityChanged;
        _autoSwitcher.CrosshairTestStateChanged += OnCrosshairTestStateChanged;
        _overlay.VisibilityChanged += OnOverlayVisibilityChanged;

        _overlay.SetFrameRateLimit(_settings.Current.OverlayFpsLimit);
        ApplyPriority();
        ApplyInputSources();
        Update();
        UpdatePixelShift();
    }

    /// <summary>
    /// Độ dịch (physical pixel) ở bước thứ <paramref name="step"/> của vòng chống lưu ảnh: lên 1 → phải 1 →
    /// xuống 1 → trái 1 (về tâm). Không bao giờ lệch quá 1 pixel mỗi trục.
    /// </summary>
    internal static (int X, int Y) PixelShiftOffset(int step) => (((step % 4) + 4) % 4) switch
    {
        0 => (0, -1),
        1 => (1, -1),
        2 => (1, 0),
        _ => (0, 0),
    };

    /// <summary>Quy tắc ẩn tạm thời, tách thành hàm thuần để kiểm thử.</summary>
    /// <param name="inCrosshairTest">
    /// Đang test tâm ngắm trên Notepad: con trỏ luôn hiện ở đó, nên bỏ qua quy tắc con trỏ — nếu không,
    /// nút Test sẽ không bao giờ cho thấy gì.
    /// </param>
    internal static bool ShouldSuppress(
        bool hideOnRightClick, bool rightHeld, bool onlyWhenCursorHidden, bool cursorVisible, bool inCrosshairTest) =>
        (hideOnRightClick && rightHeld) || (onlyWhenCursorHidden && cursorVisible && !inCrosshairTest);

    private void OnSettingChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(AppSettings.OverlayFpsLimit):
                _overlay.SetFrameRateLimit(_settings.Current.OverlayFpsLimit);
                break;

            case nameof(AppSettings.UseHardwareAcceleration):
                ApplyRenderMode(_settings.Current.UseHardwareAcceleration);
                _logger.LogInformation("Tăng tốc phần cứng: {State}.", _settings.Current.UseHardwareAcceleration ? "bật" : "tắt");
                break;

            case nameof(AppSettings.ProcessPriority):
                ApplyPriority();
                break;

            case nameof(AppSettings.HideOnRightClick):
            case nameof(AppSettings.ShowOnlyWhenCursorHidden):
                ApplyInputSources();
                Update();
                break;

            case nameof(AppSettings.ChangeColorWhileFiring):
                ApplyInputSources();
                UpdateFiringColor();
                break;

            case nameof(AppSettings.FiringColor):
                // Đổi màu ngay cả khi đang giữ chuột (người dùng chỉnh bằng bàn phím trong lúc giữ).
                UpdateFiringColor();
                break;

            case nameof(AppSettings.EnableOledPixelShift):
                UpdatePixelShift();
                break;
        }
    }

    private void ApplyInputSources()
    {
        _hotkeys.TrackRightButton = _settings.Current.HideOnRightClick;
        if (!_settings.Current.HideOnRightClick) _rightHeld = false;

        _hotkeys.TrackLeftButton = _settings.Current.ChangeColorWhileFiring;
        if (!_settings.Current.ChangeColorWhileFiring) _leftHeld = false;

        if (_settings.Current.ShowOnlyWhenCursorHidden) _cursor.Start();
        else _cursor.Stop();
    }

    private void OnRightButtonChanged(object? sender, bool held)
    {
        _rightHeld = held;
        Update();
    }

    private void OnLeftButtonChanged(object? sender, bool held)
    {
        _leftHeld = held;
        UpdateFiringColor();
    }

    private void UpdateFiringColor() =>
        _overlay.SetColorOverride(
            _settings.Current.ChangeColorWhileFiring && _leftHeld ? _settings.Current.FiringColor : null);

    private void OnOverlayVisibilityChanged(object? sender, OverlayVisibilityChangedEventArgs e) => UpdatePixelShift();

    /// <summary>
    /// Timer chỉ chạy khi tính năng bật VÀ overlay đang hiện. Tắt tính năng thì về tâm ngay; overlay ẩn thì
    /// chỉ dừng đếm, giữ nguyên bước để hiện lại là đi tiếp vòng.
    /// </summary>
    private void UpdatePixelShift()
    {
        var enabled = _settings.Current.EnableOledPixelShift;

        if (!enabled)
        {
            _pixelShiftTimer?.Stop();
            if (_pixelShiftStep != 0 || _pixelShiftTimer is not null) _overlay.SetPixelShift(0, 0);
            _pixelShiftStep = 0;
            return;
        }

        if (_pixelShiftTimer is null)
        {
            // Background: dịch 1 pixel không gấp, không được chen trước input hay khung hình.
            _pixelShiftTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = PixelShiftInterval };
            _pixelShiftTimer.Tick += OnPixelShiftTick;
            _logger.LogInformation("Chống lưu ảnh OLED: bật, dịch 1 px mỗi {Minutes:0} phút.", PixelShiftInterval.TotalMinutes);
        }

        if (_overlay.IsVisible) _pixelShiftTimer.Start();
        else _pixelShiftTimer.Stop();
    }

    /// <summary>Timer dịch pixel đang đếm (cho kiểm thử).</summary>
    internal bool IsPixelShiftRunning => _pixelShiftTimer?.IsEnabled == true;

    private void OnPixelShiftTick(object? sender, EventArgs e) => StepPixelShift();

    /// <summary>Một nhịp của timer; tách riêng để kiểm thử không phải đợi 2 phút.</summary>
    internal void StepPixelShift()
    {
        var (x, y) = PixelShiftOffset(_pixelShiftStep);
        _pixelShiftStep = (_pixelShiftStep + 1) % 4;
        _overlay.SetPixelShift(x, y);
    }

    private void OnCursorVisibilityChanged(object? sender, bool visible) => Update();

    private void OnCrosshairTestStateChanged(object? sender, EventArgs e) => Update();

    private void Update() =>
        _overlay.SetSuppressed(ShouldSuppress(
            _settings.Current.HideOnRightClick,
            _rightHeld,
            _settings.Current.ShowOnlyWhenCursorHidden,
            _cursor.IsCursorVisible,
            _autoSwitcher.IsShowingCrosshairTest));

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (!_started) return;
        _settings.Current.PropertyChanged -= OnSettingChanged;
        _hotkeys.RightButtonChanged -= OnRightButtonChanged;
        _hotkeys.LeftButtonChanged -= OnLeftButtonChanged;
        _cursor.VisibilityChanged -= OnCursorVisibilityChanged;
        _autoSwitcher.CrosshairTestStateChanged -= OnCrosshairTestStateChanged;
        _overlay.VisibilityChanged -= OnOverlayVisibilityChanged;

        if (_pixelShiftTimer is not null)
        {
            _pixelShiftTimer.Stop();
            _pixelShiftTimer.Tick -= OnPixelShiftTick;
            _pixelShiftTimer = null;
        }

        // Thoát app: trả overlay về đúng tâm. Overlay có thể đã được giải phóng trước (thứ tự dọn của
        // container) — khi đó cửa sổ cũng không còn, không có gì để trả về.
        try
        {
            _overlay.SetColorOverride(null);
            _overlay.SetPixelShift(0, 0);
        }
        catch (ObjectDisposedException)
        {
        }
    }
}
