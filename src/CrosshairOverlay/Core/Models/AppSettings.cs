using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CrosshairOverlay.Core.Models;

/// <summary>
/// Cấu hình toàn ứng dụng, lưu tại <c>%APPDATA%\CrosshairOverlay\settings.json</c>.
/// Preset crosshair nằm ở file riêng trong thư mục <c>presets\</c>, không nhét vào đây.
/// </summary>
public sealed partial class AppSettings : ObservableObject
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>Preset đang được chọn thủ công (khi không có game profile nào khớp).</summary>
    [ObservableProperty] private Guid _activePresetId;

    /// <summary>Trạng thái bật/tắt overlay do người dùng điều khiển (hotkey / tray).</summary>
    [ObservableProperty] private bool _overlayEnabled = true;

    /// <summary>Đăng ký khởi động cùng Windows. Mặc định TẮT theo yêu cầu.</summary>
    [ObservableProperty] private bool _startWithWindows;

    /// <summary>Khởi động vào thẳng system tray, không mở cửa sổ Settings.</summary>
    [ObservableProperty] private bool _startMinimizedToTray;

    /// <summary>Tự đổi preset theo tiến trình foreground.</summary>
    [ObservableProperty] private bool _autoSwitchByGameProfile = true;

    /// <summary>Chỉ hiện overlay khi foreground khớp một game profile; ngoài ra thì ẩn.</summary>
    [ObservableProperty] private bool _showOnlyInMatchedGames;

    [ObservableProperty] private MonitorSelectionMode _monitorSelectionMode = MonitorSelectionMode.FollowForegroundWindow;

    /// <summary>DeviceName của màn hình khi <see cref="MonitorSelectionMode"/> là Specific.</summary>
    [ObservableProperty] private string? _targetMonitorDeviceName;

    /// <summary>Hiện cảnh báo khi phát hiện game có thể đang chạy Exclusive Fullscreen.</summary>
    [ObservableProperty] private bool _warnOnExclusiveFullscreen = true;

    /// <summary>Mức log tối thiểu ghi ra file: Trace/Debug/Information/Warning/Error.</summary>
    [ObservableProperty] private string _logLevel = "Information";

    /// <summary>Ngôn ngữ giao diện: "auto" (theo hệ thống), "vi" hoặc "en".</summary>
    [ObservableProperty] private string _language = "auto";

    /// <summary>Tự kiểm tra bản cập nhật khi khởi động.</summary>
    [ObservableProperty] private bool _checkForUpdates = true;

    public ObservableCollection<HotkeyBinding> Hotkeys { get; set; } = [];

    public ObservableCollection<GameProfile> GameProfiles { get; set; } = [];

    public static AppSettings CreateDefault()
    {
        var settings = new AppSettings();
        foreach (var hotkey in HotkeyBinding.CreateDefaults())
            settings.Hotkeys.Add(hotkey);
        return settings;
    }
}
