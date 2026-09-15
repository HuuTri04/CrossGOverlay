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
    private readonly IAppPathProvider _paths;
    private readonly IProcessLauncher _launcher;
    private readonly IAppRestartService _restart;

    private bool _disposed;

    public GeneralSettingsViewModel(
        IAppSettingsService settings,
        IMonitorService monitors,
        IOverlayController overlay,
        IStartupService startup,
        IUpdateService updates,
        IDialogService dialogs,
        IAppPathProvider paths,
        IProcessLauncher launcher,
        IAppRestartService restart)
    {
        _paths = paths;
        _launcher = launcher;
        _restart = restart;
        _settings = settings;
        _monitors = monitors;
        _overlay = overlay;
        _startup = startup;
        _updates = updates;
        _dialogs = dialogs;

        _monitorMode = settings.Current.MonitorSelectionMode;
        _languageCode = settings.Current.Language;
        MonitorModes = BuildMonitorModes();
        FpsLimits = BuildFpsLimits();
        PriorityModes = BuildPriorityModes();
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

    /// <summary>Các mức giới hạn FPS của overlay; 0 là không giới hạn.</summary>
    public IReadOnlyList<LocalizedOption<int>> FpsLimits { get; }

    /// <summary>
    /// Giới hạn FPS đang chọn (60/120/144, 0 = không giới hạn). Ghi thẳng vào cài đặt và lưu;
    /// OverlayBehaviorController áp nó lên overlay.
    /// </summary>
    public int OverlayFpsLimit
    {
        get => _settings.Current.OverlayFpsLimit;
        set
        {
            if (_settings.Current.OverlayFpsLimit == value) return;
            _settings.Current.OverlayFpsLimit = value;
            _settings.RequestSave();
            OnPropertyChanged();
        }
    }

    /// <summary>Các mức ưu tiên tiến trình cho ComboBox.</summary>
    public IReadOnlyList<LocalizedOption<ProcessPriorityMode>> PriorityModes { get; }

    /// <summary>Mức ưu tiên CPU của ứng dụng; OverlayBehaviorController áp nó lên tiến trình.</summary>
    public ProcessPriorityMode ProcessPriority
    {
        get => _settings.Current.ProcessPriority;
        set
        {
            if (_settings.Current.ProcessPriority == value) return;
            _settings.Current.ProcessPriority = value;
            _settings.RequestSave();
            OnPropertyChanged();
        }
    }

    /// <summary>Thư mục chứa settings.json, preset và ảnh — hiện dưới nút mở thư mục.</summary>
    public string DataDirectory => _paths.RootDirectory;

    [RelayCommand]
    private void OpenDataFolder()
    {
        try
        {
            global::System.IO.Directory.CreateDirectory(_paths.RootDirectory);
            _launcher.OpenFolder(_paths.RootDirectory);
        }
        catch (Exception ex) when (ex is global::System.ComponentModel.Win32Exception or InvalidOperationException
                                       or global::System.IO.IOException or UnauthorizedAccessException)
        {
            _dialogs.ShowMessage(Tr.Get("Data_Header"), Tr.Format("Data_OpenFailed", _paths.RootDirectory, ex.Message), isError: true);
        }
    }

    /// <summary>
    /// Khôi phục cài đặt gốc: hỏi xác nhận, rồi khởi động lại ứng dụng ở chế độ xoá dữ liệu. Việc xoá
    /// làm ở instance mới, sau khi instance này đã thoát và ghi xong mọi thứ còn treo.
    /// </summary>
    [RelayCommand]
    private void FactoryReset()
    {
        if (!_dialogs.Confirm(Tr.Get("Data_ResetTitle"), Tr.Get("Data_ResetConfirm"))) return;

        if (!_restart.RestartWithFactoryReset(out var error))
            _dialogs.ShowMessage(Tr.Get("Data_ResetTitle"), Tr.Format("Data_ResetFailed", error ?? "?"), isError: true);
    }

    /// <summary>Tăng tốc phần cứng (GPU) cho việc vẽ của ứng dụng.</summary>
    public bool UseHardwareAcceleration
    {
        get => _settings.Current.UseHardwareAcceleration;
        set
        {
            if (_settings.Current.UseHardwareAcceleration == value) return;
            _settings.Current.UseHardwareAcceleration = value;
            _settings.RequestSave();
            OnPropertyChanged();
        }
    }

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

    private static IReadOnlyList<LocalizedOption<ProcessPriorityMode>> BuildPriorityModes() =>
    [
        new(ProcessPriorityMode.Normal, "Perf_PriorityNormal"),
        new(ProcessPriorityMode.High, "Perf_PriorityHigh"),
    ];

    private static IReadOnlyList<LocalizedOption<int>> BuildFpsLimits() =>
    [
        new(60, "Perf_Fps60"),
        new(120, "Perf_Fps120"),
        new(144, "Perf_Fps144"),
        new(0, "Perf_FpsUnlimited"),
    ];

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
        foreach (var limit in FpsLimits) limit.Refresh();
        foreach (var priority in PriorityModes) priority.Refresh();
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
