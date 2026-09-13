using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CrosshairOverlay.Core.Abstractions;
using CrosshairOverlay.Core.Models;
using CrosshairOverlay.Localization;

namespace CrosshairOverlay.ViewModels;

/// <summary>ViewModel cho tab "Chung": chọn màn hình, ngôn ngữ và các tuỳ chọn ứng dụng.</summary>
public sealed partial class GeneralSettingsViewModel : ObservableObject, IDisposable
{
    private readonly IAppSettingsService _settings;
    private readonly IMonitorService _monitors;
    private readonly IOverlayController _overlay;
    private readonly IStartupService _startup;
    private readonly IUpdateService _updates;
    private readonly IDialogService _dialogs;

    private bool _disposed;

    public GeneralSettingsViewModel(
        IAppSettingsService settings,
        IMonitorService monitors,
        IOverlayController overlay,
        IStartupService startup,
        IUpdateService updates,
        IDialogService dialogs)
    {
        _settings = settings;
        _monitors = monitors;
        _overlay = overlay;
        _startup = startup;
        _updates = updates;
        _dialogs = dialogs;

        _monitorMode = settings.Current.MonitorSelectionMode;
        _languageCode = settings.Current.Language;
        MonitorModes = BuildMonitorModes();
        Languages = LanguageCatalog.All;

        RefreshMonitors();

        _monitors.DisplayConfigurationChanged += OnDisplayConfigurationChanged;
        TranslationSource.Instance.PropertyChanged += OnLanguageChanged;
    }

    /// <summary>View bind thẳng vào đây cho các toggle đơn giản (checkbox).</summary>
    public AppSettings Settings => _settings.Current;

    /// <summary>Danh sách giữ nguyên khi đổi ngôn ngữ; từng mục tự báo nhãn đã đổi.</summary>
    public IReadOnlyList<LocalizedOption<MonitorSelectionMode>> MonitorModes { get; }

    public IReadOnlyList<LocalizedOption<string>> Languages { get; }

    [ObservableProperty] private IReadOnlyList<MonitorInfo> _availableMonitors = [];

    [ObservableProperty] private MonitorSelectionMode _monitorMode;

    [ObservableProperty] private MonitorInfo? _pinnedMonitor;

    [ObservableProperty] private string _startupHint = string.Empty;

    [ObservableProperty] private string _languageCode;

    [ObservableProperty] private string _updateStatus = string.Empty;

    /// <summary>
    /// Kiểm tra cập nhật theo yêu cầu. Hoàn toàn bất đồng bộ — nút không bao giờ làm treo
    /// giao diện, kể cả khi máy chủ không phản hồi.
    /// </summary>
    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        UpdateStatus = string.Empty;

        var update = await _updates.CheckAsync();

        if (update is null)
        {
            UpdateStatus = Tr.Format("Update_UpToDate", _updates.CurrentVersion);
            return;
        }

        if (await Services.Updates.UpdatePrompt.OfferAsync(_updates, _dialogs, update))
            App.RequestShutdown();
    }

    public bool ShowMonitorPicker => MonitorMode == MonitorSelectionMode.Specific;

    /// <summary>
    /// Registry là nguồn chân lý, không phải settings.json — người dùng có thể tắt mục khởi
    /// động qua Task Manager mà app không hề hay biết.
    /// </summary>
    public bool StartWithWindows
    {
        get => _startup.IsEnabled;
        set
        {
            if (_startup.SetEnabled(value))
            {
                StartupHint = Tr.Get(value ? "General_StartupEnabled" : "General_StartupDisabled");
                Settings.StartWithWindows = value;
                _settings.RequestSave();
            }
            else
            {
                StartupHint = Tr.Get("General_StartupFailed");
            }

            OnPropertyChanged();
        }
    }

    private static IReadOnlyList<LocalizedOption<MonitorSelectionMode>> BuildMonitorModes() =>
    [
        new(MonitorSelectionMode.FollowForegroundWindow, "MonitorMode_Follow"),
        new(MonitorSelectionMode.Primary, "MonitorMode_Primary"),
        new(MonitorSelectionMode.Specific, "MonitorMode_Specific"),
    ];

    partial void OnLanguageCodeChanged(string value)
    {
        Settings.Language = value;
        _settings.RequestSave();

        // Áp dụng ngay: mọi nhãn bind qua {loc:Loc} cập nhật lập tức.
        LanguageCatalog.Apply(value);
    }

    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs e)
    {
        foreach (var mode in MonitorModes) mode.Refresh();
        LanguageCatalog.RefreshLabels();

        StartupHint = string.Empty;
    }

    private void OnDisplayConfigurationChanged(object? sender, EventArgs e) => RefreshMonitors();

    partial void OnMonitorModeChanged(MonitorSelectionMode value)
    {
        OnPropertyChanged(nameof(ShowMonitorPicker));
        Apply();
    }

    partial void OnPinnedMonitorChanged(MonitorInfo? value)
    {
        if (MonitorMode == MonitorSelectionMode.Specific) Apply();
    }

    private void Apply()
    {
        var deviceName = MonitorMode == MonitorSelectionMode.Specific
            ? PinnedMonitor?.DeviceName
            : null;

        // Ghim vào màn hình cụ thể mà chưa chọn màn hình nào thì chưa áp — tránh nhảy về
        // màn hình chính ngay khi người dùng vừa đổi mode và chưa kịp chọn.
        if (MonitorMode == MonitorSelectionMode.Specific && deviceName is null) return;

        _overlay.SetMonitorSelection(MonitorMode, deviceName);

        Settings.MonitorSelectionMode = MonitorMode;
        Settings.TargetMonitorDeviceName = deviceName;
        _settings.RequestSave();
    }

    [RelayCommand]
    private void RefreshMonitors()
    {
        AvailableMonitors = _monitors.GetMonitors();

        var savedName = Settings.TargetMonitorDeviceName;
        PinnedMonitor = AvailableMonitors.Cast<MonitorInfo?>().FirstOrDefault(
            m => m!.Value.DeviceName == savedName) ?? AvailableMonitors.FirstOrDefault();
    }

    /// <summary>Gọi khi một checkbox bind thẳng vào <see cref="Settings"/> vừa đổi.</summary>
    [RelayCommand]
    private void PersistSettings() => _settings.RequestSave();

    /// <summary>
    /// Gỡ đăng ký khỏi các service singleton. ViewModel chết theo cửa sổ Settings, còn chúng
    /// sống suốt phiên — không gỡ thì mỗi lần mở lại cửa sổ là một ViewModel nữa bị giữ sống.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _monitors.DisplayConfigurationChanged -= OnDisplayConfigurationChanged;
        TranslationSource.Instance.PropertyChanged -= OnLanguageChanged;
    }
}
