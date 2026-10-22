using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using System.IO;
using System.Windows.Media.Imaging;
using System.Security.Principal;
using CommunityToolkit.Mvvm.Input;
using CrosshairOverlay.Core.Abstractions;
using CrosshairOverlay.Core.Models;
using CrosshairOverlay.Localization;
using CrosshairOverlay.Services.Display;
using CrosshairOverlay.Services.Storage;
using CrosshairOverlay.Services.System;

namespace CrosshairOverlay.ViewModels;

/// <summary>ViewModel cho tab "Chung": chọn màn hình, ngôn ngữ và các tuỳ chọn ứng dụng.</summary>
public sealed partial class GeneralSettingsViewModel : ObservableObject, IDisposable
{
    private readonly IAppSettingsService _settings;
    private readonly IMonitorService _monitors;
    private readonly DisplayAnalyzerService _displayAnalyzer;
    private readonly ITrayIconController _tray;
    private readonly IOverlayController _overlay;
    private readonly IStartupService _startup;
    private readonly IUpdateService _updates;
    private readonly ToastViewModel _toast;
    private readonly IDialogService _dialogs;
    private readonly IAppPathProvider _paths;
    private readonly IProcessLauncher _launcher;
    private readonly IAppRestartService _restart;
    private readonly IStorageLocationService _storage;
    private readonly IPresetLibrary? _library;
    private readonly ObsWebSocketService? _obs;
    private int _backgroundLoadVersion;
    private int _backgroundRenderVersion;
    private CancellationTokenSource? _backgroundRenderCancellation;
    private BitmapSource? _sourceBackgroundImage;
    private bool _backgroundViewActive;

    private bool _disposed;

    public GeneralSettingsViewModel(
        IAppSettingsService settings,
        IMonitorService monitors,
        DisplayAnalyzerService displayAnalyzer,
        ITrayIconController tray,
        IOverlayController overlay,
        IStartupService startup,
        IUpdateService updates,
        IDialogService dialogs,
        IAppPathProvider paths,
        IProcessLauncher launcher,
        IAppRestartService restart,
        IStorageLocationService storage,
        ToastViewModel toast,
        ObsWebSocketService? obs = null,
        IPresetLibrary? library = null)
    {
        _obs = obs;
        _toast = toast;
        _library = library;
        _storage = storage;
        _paths = paths;
        _launcher = launcher;
        _restart = restart;
        _settings = settings;
        _monitors = monitors;
        _displayAnalyzer = displayAnalyzer;
        _tray = tray;
        _overlay = overlay;
        _startup = startup;
        _updates = updates;
        _dialogs = dialogs;
        IsAdminRunning = CheckAdminRights();
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

    /// <summary>Frozen, pre-blurred bitmap bound to the backdrop Image.</summary>
    [ObservableProperty] private BitmapSource? _appBackgroundImage;

    public bool HasAppBackground => AppBackgroundImage is not null && _settings.Current.AppBgOpacity > 0d;

    /// <summary>Release decoded and processed bitmaps while the settings window is not displayed.</summary>
    public void SetBackgroundViewActive(bool active)
    {
        if (_disposed || _backgroundViewActive == active) return;
        _backgroundViewActive = active;
        if (active)
        {
            _ = LoadSavedAppBackgroundAsync(_settings.Current.CustomAppBgPath);
            return;
        }

        Interlocked.Increment(ref _backgroundLoadVersion);
        CancelBackgroundRender();
        _sourceBackgroundImage = null;
        AppBackgroundImage = null;
    }

    partial void OnAppBackgroundImageChanged(BitmapSource? value) =>
        OnPropertyChanged(nameof(HasAppBackground));

    public double AppBgOpacityPercent
    {
        get => _settings.Current.AppBgOpacity * 100d;
        set
        {
            var opacity = AppSettings.AppBgOpacityPercentRange.Clamp(value) / 100d;
            if (Math.Abs(_settings.Current.AppBgOpacity - opacity) < 0.0001d) return;
            _settings.Current.AppBgOpacity = opacity;
            _settings.RequestSave();
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasAppBackground));
        }
    }

    public double AppBgBlurRadius
    {
        get => _settings.Current.AppBgBlurRadius;
        set
        {
            var radius = AppSettings.AppBgBlurRadiusRange.Clamp(value);
            if (Math.Abs(_settings.Current.AppBgBlurRadius - radius) < 0.0001d) return;
            _settings.Current.AppBgBlurRadius = radius;
            _settings.RequestSave();
            OnPropertyChanged();
            _ = RenderBackgroundAsync(debounce: true, reportError: true);
        }
    }

    [RelayCommand]
    private async Task ChooseAppBackgroundAsync()
    {
        var path = _dialogs.PickFileToOpen(Tr.Get("AppBg_ImageFilter"),
            Path.GetDirectoryName(_settings.Current.CustomAppBgPath));
        if (path is null) return;

        // An explicit choice can also come from a ViewModel consumer without a visible window.
        _backgroundViewActive = true;
        var version = Interlocked.Increment(ref _backgroundLoadVersion);
        var image = await DecodeAppBackgroundAsync(path, reportError: true);
        if (image is null || _disposed || !_backgroundViewActive || version != _backgroundLoadVersion) return;

        CancelBackgroundRender();
        _sourceBackgroundImage = image;
        _settings.Current.CustomAppBgPath = path;
        _settings.RequestSave();
        await RenderBackgroundAsync(debounce: false, reportError: true);
    }

    [RelayCommand]
    private void ResetAppBackground()
    {
        Interlocked.Increment(ref _backgroundLoadVersion);
        CancelBackgroundRender();
        _sourceBackgroundImage = null;
        AppBackgroundImage = null;
        _settings.Current.CustomAppBgPath = null;
        AppBgOpacityPercent = AppSettings.DefaultAppBgOpacity * 100d;
        AppBgBlurRadius = AppSettings.DefaultAppBgBlurRadius;
        _settings.RequestSave();
    }

    private async Task LoadSavedAppBackgroundAsync(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        var version = _backgroundLoadVersion;
        var image = await DecodeAppBackgroundAsync(path, reportError: false);
        if (image is not null && !_disposed && _backgroundViewActive && version == _backgroundLoadVersion
            && _settings.Current.CustomAppBgPath == path)
        {
            _sourceBackgroundImage = image;
            await RenderBackgroundAsync(debounce: false, reportError: false);
        }
    }

    private async Task<BitmapSource?> DecodeAppBackgroundAsync(string path, bool reportError)
    {
        try { return await Task.Run(() => AppBackgroundImageLoader.Decode(path, maxDecodedSide: 1280)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException
                                   or ArgumentException or InvalidOperationException
                                   or global::System.Runtime.InteropServices.COMException)
        {
            if (reportError && !_disposed)
                _dialogs.ShowMessage(Tr.Get("AppBg_ImageErrorTitle"),
                    Tr.Format("AppBg_ImageError", ex.Message), isError: true);
            return null;
        }
    }

    private async Task RenderBackgroundAsync(bool debounce, bool reportError)
    {
        var source = _sourceBackgroundImage;
        if (_disposed || !_backgroundViewActive || source is null) return;

        var version = Interlocked.Increment(ref _backgroundRenderVersion);
        var cancellation = new CancellationTokenSource();
        Interlocked.Exchange(ref _backgroundRenderCancellation, cancellation)?.Cancel();
        try
        {
            if (debounce) await Task.Delay(200, cancellation.Token);
            var rendered = await ImageBlurHelper.CreatePreBlurredBitmapAsync(
                source, _settings.Current.AppBgBlurRadius, cancellation.Token);
            if (!_disposed && _backgroundViewActive && !cancellation.IsCancellationRequested
                && version == _backgroundRenderVersion && ReferenceEquals(source, _sourceBackgroundImage))
                AppBackgroundImage = rendered;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (reportError && !_disposed && version == _backgroundRenderVersion)
                _dialogs.ShowMessage(Tr.Get("AppBg_ImageErrorTitle"),
                    Tr.Format("AppBg_ImageError", ex.Message), isError: true);
        }
        finally
        {
            Interlocked.CompareExchange(ref _backgroundRenderCancellation, null, cancellation);
            cancellation.Dispose();
        }
    }

    private void CancelBackgroundRender()
    {
        Interlocked.Increment(ref _backgroundRenderVersion);
        Interlocked.Exchange(ref _backgroundRenderCancellation, null)?.Cancel();
    }

    /// <summary>OBS credential entered for this window/session; never persisted to AppSettings.</summary>
    [ObservableProperty] private string _obsPassword = string.Empty;

    [ObservableProperty] private bool _isPasswordVisible;

    public void ApplyObsPassword() => _obs?.SetPassword(ObsPassword);

    public bool IsAdminRunning { get; }

    private static bool CheckAdminRights()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch (Exception)
        {
            return false;
        }
    }

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

    /// <summary>Scale áp dụng chung cho overlay; người dùng vẫn có thể chỉnh lại sau khi quét.</summary>
    public double GlobalScale
    {
        get => _settings.Current.GlobalScale;
        set
        {
            if (Math.Abs(_settings.Current.GlobalScale - value) < 0.0001d) return;
            _settings.Current.GlobalScale = value;
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

    /// <summary>Thư mục chứa settings.json, preset và ảnh đang dùng trong phiên này.</summary>
    public string DataDirectory => _paths.RootDirectory;

    /// <summary>Đang sao chép dữ liệu sang thư mục mới — khoá nút và hiện trạng thái.</summary>
    [ObservableProperty] private bool _isChangingStorage;

    [ObservableProperty] private bool _isHandlingBackup;

    [RelayCommand]
    private async Task BackupDataAsync()
    {
        if (IsHandlingBackup) return;
        var path = _dialogs.PickFileToSave("ZIP archive (*.zip)|*.zip", "CrossGOverlay-backup.zip");
        if (path is null) return;

        IsHandlingBackup = true;
        try
        {
            if (_library is not null) await _library.FlushAsync();
            await _settings.FlushAsync();
            // A fresh install may still have only in-memory defaults and no settings.json.
            await _settings.SaveAsync();
            await Task.Run(() => DataBackupArchive.Create(_paths, path));
            _toast.Show(Tr.Get("Data_BackupDone"));
        }
        catch (Exception ex) when (ex is global::System.IO.IOException or UnauthorizedAccessException
                                   or global::System.IO.InvalidDataException or global::System.Text.Json.JsonException
                                   or NotSupportedException or OverflowException)
        {
            _dialogs.ShowMessage(Tr.Get("Data_Header"), ex.Message, isError: true);
        }
        finally { IsHandlingBackup = false; }
    }

    [RelayCommand]
    private async Task RestoreDataAsync()
    {
        if (IsHandlingBackup) return;
        var path = _dialogs.PickFileToOpen("ZIP archive (*.zip)|*.zip");
        if (path is null) return;

        IsHandlingBackup = true;
        try
        {
            await Task.Run(() => DataBackupArchive.Validate(path));
            if (!_dialogs.Confirm(Tr.Get("Data_Restore"), Tr.Get("Data_RestoreConfirm"))) return;
            if (!_restart.RestartWithRestore(path, out var error))
                _dialogs.ShowMessage(Tr.Get("Data_Restore"), error ?? "?", isError: true);
        }
        catch (Exception ex) when (ex is global::System.IO.IOException or UnauthorizedAccessException
                                   or global::System.IO.InvalidDataException or global::System.Text.Json.JsonException
                                   or OverflowException or NotSupportedException)
        {
            _dialogs.ShowMessage(Tr.Get("Data_Restore"), ex.Message, isError: true);
        }
        finally { IsHandlingBackup = false; }
    }

    /// <summary>
    /// Đổi thư mục dữ liệu: chọn thư mục → hỏi có chép dữ liệu hiện tại không → chép (nếu có) → ghi con trỏ
    /// → khởi động lại. Dữ liệu ở thư mục cũ được giữ nguyên, phòng khi cần quay lại.
    /// </summary>
    [RelayCommand]
    private async Task ChangeStorageLocationAsync()
    {
        var title = Tr.Get("Storage_Title");

        var picked = _dialogs.PickFolder(Tr.Get("Storage_PickTitle"), _paths.RootDirectory);
        if (picked is null) return;

        var target = _storage.ResolveTarget(picked);
        switch (_storage.Validate(target))
        {
            case Services.Storage.StorageLocation.Problem.SameAsCurrent:
                _dialogs.ShowMessage(title, Tr.Get("Storage_SameFolder"));
                return;
            case Services.Storage.StorageLocation.Problem.InsideCurrent:
                _dialogs.ShowMessage(title, Tr.Get("Storage_InsideCurrent"), isError: true);
                return;
            case Services.Storage.StorageLocation.Problem.NotWritable:
                _dialogs.ShowMessage(title, Tr.Format("Storage_NotWritable", target), isError: true);
                return;
        }

        var choice = _dialogs.Ask(title, Tr.Format("Storage_CopyQuestion", target),
            Tr.Get("Dialog_Yes"), Tr.Get("Dialog_No"), Tr.Get("Dialog_Cancel"));
        if (choice == DialogChoice.Cancel) return;

        StorageChangeResult result;
        IsChangingStorage = true;
        try
        {
            result = await _storage.ChangeAsync(target, copyExistingData: choice == DialogChoice.Primary);
        }
        finally
        {
            IsChangingStorage = false;
        }

        if (!result.Success)
        {
            _dialogs.ShowMessage(title, Tr.Format("Storage_Failed", result.Error ?? "?"), isError: true);
            return;
        }

        var done = choice == DialogChoice.Primary
            ? Tr.Format("Storage_DoneCopied", target, result.CopiedFiles)
            : Tr.Format("Storage_DoneEmpty", target);
        _dialogs.ShowMessage(title, done);

        if (!_restart.Restart(out var error))
            _dialogs.ShowMessage(title, Tr.Format("Storage_RestartManually", error ?? "?"), isError: true);
    }

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

    /// <summary>Chống lưu ảnh OLED: dịch overlay 1 pixel theo chu kỳ. Lưu ngay vào cài đặt.</summary>
    public bool EnableOledPixelShift
    {
        get => _settings.Current.EnableOledPixelShift;
        set
        {
            if (_settings.Current.EnableOledPixelShift == value) return;
            _settings.Current.EnableOledPixelShift = value;
            _settings.RequestSave();
            OnPropertyChanged();
        }
    }

    public bool EnableSmartContrast
    {
        get => _settings.Current.EnableSmartContrast;
        set
        {
            if (_settings.Current.EnableSmartContrast == value) return;
            _settings.Current.EnableSmartContrast = value;
            _settings.RequestSave();
            OnPropertyChanged();
        }
    }

    [ObservableProperty] private IReadOnlyList<MonitorInfo> _availableMonitors = [];

    [ObservableProperty] private MonitorSelectionMode _monitorMode;

    [ObservableProperty] private MonitorInfo? _pinnedMonitor;

    [ObservableProperty] private string _startupHint = string.Empty;

    [ObservableProperty] private string _languageCode;

    /// <summary>
    /// Kiểm tra cập nhật theo yêu cầu. Hoàn toàn bất đồng bộ — nút không bao giờ làm treo
    /// giao diện, kể cả khi máy chủ không phản hồi.
    /// </summary>
    /// <remarks>
    /// Có bản mới thì mở hộp thoại; còn lại chỉ là một thông báo nhỏ tự tắt. "Đã mới nhất" và "không hỏi được máy
    /// chủ" là hai câu KHÁC nhau: mất mạng mà báo "bạn đang dùng bản mới nhất" là nói sai.
    /// </remarks>
    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        var result = await _updates.CheckAsync();

        if (result.Status == UpdateCheckStatus.UpdateAvailable && result.Update is { } update)
        {
            if (_dialogs.ShowUpdate(update)) App.RequestShutdown();
            return;
        }

        _toast.Show(result.Status == UpdateCheckStatus.UpToDate
            ? Tr.Format("Update_UpToDate", Core.AppInfo.Short(_updates.CurrentVersion))
            : Tr.Get("Update_CheckFailed"));
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
        new(30, "Perf_Fps30"),
        new(60, "Perf_Fps60"),
        new(120, "Perf_Fps120"),
        new(140, "Perf_Fps140"),
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

    private void RefreshMonitors()
    {
        AvailableMonitors = _monitors.GetMonitors();

        var savedName = Settings.TargetMonitorDeviceName;
        PinnedMonitor = AvailableMonitors.Cast<MonitorInfo?>().FirstOrDefault(
            m => m!.Value.DeviceName == savedName) ?? AvailableMonitors.FirstOrDefault();
    }

    [RelayCommand]
    private async Task RefreshMonitorsAndOptimizeAsync()
    {
        AvailableMonitors = _monitors.RefreshMonitors();
        RefreshMonitors();

        var recommendation = _displayAnalyzer.AnalyzeDisplayAndGetRecommendations(
            MonitorMode, Settings.TargetMonitorDeviceName);

        OverlayFpsLimit = recommendation.RecommendedFps;
        GlobalScale = recommendation.RecommendedScale;
        _overlay.SetMonitorSelection(MonitorMode, Settings.TargetMonitorDeviceName);

        // AppSettingsService serializes through AtomicFile; await this explicit user action.
        await _settings.SaveAsync();

        _tray.ShowNotification(
            Tr.Get("Display_OptimizedTitle"),
            Tr.Format("Display_OptimizedMessage",
                recommendation.RecommendedFps,
                recommendation.RecommendedScale * 100d,
                recommendation.DisplayLabel,
                recommendation.RefreshRate > 0 ? $" {recommendation.RefreshRate}Hz" : string.Empty));
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
        Interlocked.Increment(ref _backgroundLoadVersion);
        CancelBackgroundRender();
        _sourceBackgroundImage = null;
        AppBackgroundImage = null;
        ObsPassword = string.Empty;
        IsPasswordVisible = false;

        _monitors.DisplayConfigurationChanged -= OnDisplayConfigurationChanged;
        TranslationSource.Instance.PropertyChanged -= OnLanguageChanged;
    }
}
