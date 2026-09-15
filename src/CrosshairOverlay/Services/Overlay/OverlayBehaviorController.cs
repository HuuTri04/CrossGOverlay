using System.ComponentModel;
using System.Windows.Interop;
using System.Windows.Media;
using CrosshairOverlay.Core.Abstractions;
using CrosshairOverlay.Core.Models;
using Microsoft.Extensions.Logging;

namespace CrosshairOverlay.Services.Overlay;

/// <summary>
/// Áp các tuỳ chọn "Hiệu năng" và "Hành vi nâng cao" lên overlay: giới hạn FPS, tăng tốc phần cứng,
/// ẩn khi giữ chuột phải, chỉ hiện khi con trỏ bị ẩn.
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

    private bool _rightHeld;
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
        _cursor.VisibilityChanged += OnCursorVisibilityChanged;
        _autoSwitcher.CrosshairTestStateChanged += OnCrosshairTestStateChanged;

        _overlay.SetFrameRateLimit(_settings.Current.OverlayFpsLimit);
        ApplyPriority();
        ApplyInputSources();
        Update();
    }

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
        }
    }

    private void ApplyInputSources()
    {
        _hotkeys.TrackRightButton = _settings.Current.HideOnRightClick;
        if (!_settings.Current.HideOnRightClick) _rightHeld = false;

        if (_settings.Current.ShowOnlyWhenCursorHidden) _cursor.Start();
        else _cursor.Stop();
    }

    private void OnRightButtonChanged(object? sender, bool held)
    {
        _rightHeld = held;
        Update();
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
        _cursor.VisibilityChanged -= OnCursorVisibilityChanged;
        _autoSwitcher.CrosshairTestStateChanged -= OnCrosshairTestStateChanged;
    }
}
