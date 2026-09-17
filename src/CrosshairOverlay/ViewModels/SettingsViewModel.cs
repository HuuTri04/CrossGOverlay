using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CrosshairOverlay.Core.Abstractions;
using CrosshairOverlay.Core.Models;
using CrosshairOverlay.Localization;
using Microsoft.Extensions.Logging;

namespace CrosshairOverlay.ViewModels;

/// <summary>ViewModel gốc của cửa sổ Settings.</summary>
public sealed partial class SettingsViewModel : ObservableObject, IDisposable
{
    private readonly IPresetLibrary _library;
    private readonly IOverlayController _overlay;
    private readonly IAppSettingsService _settings;
    private readonly IDialogService _dialogs;
    private readonly ICustomImageStore _images;
    private readonly IProfileAutoSwitcher _autoSwitcher;
    private readonly IProcessLauncher _launcher;
    private readonly ICrosshairTranslator _translator;
    private readonly ILogger<SettingsViewModel> _logger;

    private bool _syncingSelection;
    private bool _disposed;

    public SettingsViewModel(
        IPresetLibrary library,
        IOverlayController overlay,
        IAppSettingsService settings,
        IMonitorService monitors,
        ICrosshairRenderer renderer,
        IDialogService dialogs,
        IHotkeyService hotkeys,
        IForegroundWindowWatcher watcher,
        IStartupService startup,
        IGameProfileMatcher matcher,
        IRunningApplicationScanner scanner,
        IUpdateService updates,
        ICustomImageStore images,
        IProfileAutoSwitcher autoSwitcher,
        IProcessLauncher launcher,
        IHardwareInfoService hardware,
        IAppPathProvider paths,
        IAppRestartService restart,
        IStorageLocationService storage,
        IDisplayModeReader displayModes,
        ICrosshairTranslator translator,
        ILogger<SettingsViewModel> logger)
    {
        _library = library;
        _overlay = overlay;
        _settings = settings;
        _dialogs = dialogs;
        _images = images;
        _autoSwitcher = autoSwitcher;
        _launcher = launcher;
        _translator = translator;
        _logger = logger;

        Editor = new CrosshairEditorViewModel(renderer, dialogs, images, settings);
        General = new GeneralSettingsViewModel(settings, monitors, overlay, startup, updates, dialogs, paths, launcher, restart, storage);
        Hotkeys = new HotkeysViewModel(settings, hotkeys);
        Games = new GameProfilesViewModel(settings, library, watcher, matcher, scanner);
        SystemInfo = new SystemInfoViewModel(hardware, monitors, displayModes);

        // Ô "Bật overlay" là lựa chọn của người dùng, không phải overlay có đang hiện hay không —
        // xem App.ToggleOverlay. Đồng bộ hai chiều với menu khay và phím tắt qua cài đặt.
        _overlayEnabled = settings.Current.OverlayEnabled;
        _settings.Current.PropertyChanged += OnSettingsChanged;
        TranslationSource.Instance.PropertyChanged += OnLanguageChanged;

        _library.ActiveChanged += OnLibraryActiveChanged;
        SyncFromLibrary();
    }

    public CrosshairEditorViewModel Editor { get; }

    public GeneralSettingsViewModel General { get; }

    public HotkeysViewModel Hotkeys { get; }

    public GameProfilesViewModel Games { get; }

    public SystemInfoViewModel SystemInfo { get; }

    public ReadOnlyObservableCollection<CrosshairProfile> Presets => _library.Presets;

    /// <summary>
    /// Kéo thả trong danh sách preset: đưa <paramref name="preset"/> tới vị trí <paramref name="newIndex"/> và
    /// giữ nó là mục đang chọn.
    /// </summary>
    public bool MovePreset(CrosshairProfile preset, int newIndex)
    {
        if (!_library.Move(preset, newIndex)) return false;

        // Move của ObservableCollection giữ lựa chọn, nhưng nếu người dùng kéo một mục CHƯA chọn thì chọn
        // luôn nó — mục vừa thả là mục họ đang để ý.
        SelectedPreset = preset;
        return true;
    }

    /// <summary>Lệnh cho <see cref="Controls.DragReorder"/> trên danh sách preset.</summary>
    [RelayCommand]
    private void MovePresetTo(Controls.ReorderRequest? request)
    {
        if (request is { Item: CrosshairProfile preset }) MovePreset(preset, request.NewIndex);
    }

    [ObservableProperty] private CrosshairProfile? _selectedPreset;

    [ObservableProperty] private bool _overlayEnabled;

    partial void OnSelectedPresetChanged(CrosshairProfile? value)
    {
        if (_syncingSelection || value is null) return;
        _library.SetActive(value);
    }

    /// <summary>Chỉ ghi cài đặt; App áp quy tắc hiện/ẩn (tôn trọng chế độ "chỉ hiện trong game").</summary>
    partial void OnOverlayEnabledChanged(bool value)
    {
        if (_settings.Current.OverlayEnabled == value) return;

        _settings.Current.OverlayEnabled = value;
        _settings.RequestSave();
    }

    /// <summary>
    /// Chú thích dưới ô "Bật overlay": overlay sẽ hiện ở đâu với chế độ đang chọn.
    /// </summary>
    /// <remarks>
    /// Đổi theo cài đặt chứ không cố định: câu "chỉ hiện trong game đúng profile" sẽ sai sự thật
    /// khi người dùng không bật tuỳ chọn chỉ-hiện-trong-game (lúc đó overlay hiện ở mọi nơi).
    /// </remarks>
    public string OverlayHint => Tr.Get(OverlayShowsOnlyInMatchedGames(
        _settings.Current.AutoSwitchByGameProfile, _settings.Current.ShowOnlyInMatchedGames)
            ? "Shell_OverlayHintMatchedOnly"
            : "Shell_OverlayHintEverywhere");

    /// <summary>Khớp quy tắc của ProfileAutoSwitcher: tắt tự đổi theo game thì không có khái niệm "khớp".</summary>
    internal static bool OverlayShowsOnlyInMatchedGames(bool autoSwitch, bool showOnlyInMatchedGames) =>
        autoSwitch && showOnlyInMatchedGames;

    /// <summary>Bật/tắt từ menu khay hay phím tắt thì ô tick trong cửa sổ đổi theo.</summary>
    private void OnSettingsChanged(object? sender, global::System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(AppSettings.OverlayEnabled):
                OverlayEnabled = _settings.Current.OverlayEnabled;
                break;
            case nameof(AppSettings.AutoSwitchByGameProfile):
            case nameof(AppSettings.ShowOnlyInMatchedGames):
                OnPropertyChanged(nameof(OverlayHint));
                break;
        }
    }

    private void OnLanguageChanged(object? sender, global::System.ComponentModel.PropertyChangedEventArgs e) =>
        OnPropertyChanged(nameof(OverlayHint));

    private void OnLibraryActiveChanged(object? sender, EventArgs e) => SyncFromLibrary();

    private void SyncFromLibrary()
    {
        var active = _library.Active;
        if (active is null) return;

        // Cờ này chặn vòng lặp: đặt SelectedPreset sẽ gọi lại SetActive.
        _syncingSelection = true;
        try
        {
            SelectedPreset = active;
        }
        finally
        {
            _syncingSelection = false;
        }

        // Chỉ đồng bộ phần UI. Việc đẩy preset xuống overlay và ghi ActivePresetId do
        // composition root lo, vì preset có thể đổi khi cửa sổ này đang đóng.
        Editor.Profile = active;
    }

    [RelayCommand]
    private async Task NewPresetAsync()
    {
        try
        {
            await _library.CreateAsync();
        }
        catch (Exception ex)
        {
            Report(ex, Tr.Get("Preset_ErrorCreate"));
        }
    }

    [RelayCommand]
    private async Task DuplicatePresetAsync()
    {
        if (SelectedPreset is not { } source) return;

        try
        {
            await _library.DuplicateAsync(source);
        }
        catch (Exception ex)
        {
            Report(ex, Tr.Get("Preset_ErrorDuplicate"));
        }
    }

    [RelayCommand]
    private async Task DeletePresetAsync()
    {
        if (SelectedPreset is not { } target) return;

        if (!_dialogs.Confirm(Tr.Get("Preset_DeleteTitle"), Tr.Format("Preset_DeleteConfirm", target.Name)))
            return;

        try
        {
            await _library.DeleteAsync(target);
        }
        catch (Exception ex)
        {
            Report(ex, Tr.Get("Preset_ErrorDelete"));
        }
    }

    /// <summary>Tạo preset từ mã chia sẻ: mã hình ảnh của ứng dụng, CS2 hoặc Valorant.</summary>
    [RelayCommand]
    private async Task ImportCodeAsync()
    {
        var profile = _dialogs.PromptForCrosshairCode();
        if (profile is null) return;

        if (profile.EmbeddedImage is not null && !await TryStoreSharedImageAsync(profile)) return;

        try
        {
            await _library.AddAsync(profile);
        }
        catch (Exception ex)
        {
            Report(ex, Tr.Get("Preset_ErrorFromCode"));
        }
    }

    /// <summary>
    /// Đưa ảnh đi kèm mã hình ảnh vào kho ảnh và trỏ preset tới nó.
    /// </summary>
    /// <remarks>
    /// Hộp thoại dán mã đã kiểm tra một lượt, nhưng kho ảnh vẫn kiểm tra lại (kích thước file, giải
    /// mã, số điểm ảnh) và việc ghi đĩa có thể lỗi. Mọi lỗi ở đây đều quy về MỘT thông báo dễ hiểu,
    /// không bao giờ để exception thoát ra ngoài.
    /// </remarks>
    private async Task<bool> TryStoreSharedImageAsync(CrosshairProfile profile)
    {
        var embedded = profile.EmbeddedImage!;
        profile.EmbeddedImage = null;

        try
        {
            profile.Image.FilePath = await Task.Run(() =>
                _images.ImportBytes(embedded.FileName, Convert.FromBase64String(embedded.Data)));
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Không lưu được ảnh từ mã tâm ngắm hình ảnh.");
            _dialogs.ShowMessage(Tr.Get("Import_Title"), Tr.Get("Import_ImageCodeInvalid"), isError: true);
            return false;
        }
    }

    /// <summary>Mở "Thư viện mẫu". Mẫu được thêm qua thư viện preset nên danh sách bên này tự cập nhật và chọn theo.</summary>
    [RelayCommand]
    private void OpenPresetLibrary() => _dialogs.ShowPresetLibrary();

    /// <summary>
    /// Mở hộp thoại "Xuất mã tâm ngắm": dịch preset đang chọn ra mã Valorant, CS2 và mã nội bộ.
    /// </summary>
    /// <remarks>
    /// Trước đây lệnh này chép thẳng MỘT mã vào clipboard. Người dùng chơi nhiều game, và mỗi định
    /// dạng giữ được một phần khác nhau của preset — nên đưa cả ba mã ra cùng lúc, kèm câu nói rõ
    /// mã nào mất gì, rồi để họ tự chọn.
    /// </remarks>
    [RelayCommand]
    private async Task ExportCodeAsync()
    {
        if (SelectedPreset is not { } preset) return;

        // Bản chụp: phần dựng mã ảnh chạy ở luồng nền, mà người dùng vẫn kéo thanh trượt được.
        var snapshot = preset.Clone();

        string? imageCode = null;
        string? imageError = null;

        if (snapshot.Type == CrosshairType.Image)
            (imageCode, imageError) = await BuildImageCodeAsync(snapshot);

        _dialogs.ShowExportCodes(_translator.BuildCodes(snapshot, imageCode, imageError));
    }

    /// <summary>
    /// Dựng mã tâm ngắm ảnh; trả về (mã, null) hoặc (null, lý do đọc được).
    /// </summary>
    /// <remarks>
    /// Đọc file, thu nhỏ, nén GZip và Base64 đều chạy trên luồng nền (<see cref="Task.Run(Action)"/>):
    /// ảnh GIF lớn mất hàng trăm mili giây, làm tại chỗ thì cửa sổ đứng hình ngay lúc bấm nút.
    /// Lệnh bất đồng bộ tự vô hiệu hoá nút trong lúc chạy nên không bấm chồng được.
    /// </remarks>
    private async Task<(string? Code, string? Error)> BuildImageCodeAsync(CrosshairProfile preset)
    {
        var path = _images.Resolve(preset.Image.FilePath);
        var image = preset.Image.Clone();
        var rotation = preset.Rotation;

        var (outcome, code, error, failure) = await Task.Run(() =>
        {
            if (path is null || !global::System.IO.File.Exists(path))
                return (ImageExportOutcome.NoImage, (string?)null, default(Services.Import.ImageCodeExportError), (Exception?)null);

            byte[] bytes;
            try
            {
                bytes = global::System.IO.File.ReadAllBytes(path);
            }
            catch (Exception ex) when (ex is global::System.IO.IOException or UnauthorizedAccessException)
            {
                return (ImageExportOutcome.ReadFailed, null, default, ex);
            }

            // Ảnh lớn được thu nhỏ rồi nén trước khi nhúng; Scale trong mã được bù để tâm ngắm của
            // người nhận hiện ra đúng kích thước này.
            return Services.Import.ImageCrosshairCode.TryEncode(bytes, image, rotation, out var encoded, out var encodeError)
                ? (ImageExportOutcome.Encoded, encoded, encodeError, null)
                : (ImageExportOutcome.EncodeFailed, null, encodeError, null);
        });

        switch (outcome)
        {
            case ImageExportOutcome.Encoded:
                return (code, null);

            case ImageExportOutcome.ReadFailed:
                _logger.LogWarning(failure, "Không đọc được ảnh của preset khi xuất mã.");
                return (null, Tr.Get("Preset_ImageCodeNoImage"));

            case ImageExportOutcome.EncodeFailed when error == Services.Import.ImageCodeExportError.TooLarge:
                return (null, Tr.Format("Preset_ImageCodeTooLarge", Services.Import.ImageCrosshairCode.MaxImageBytes / (1024 * 1024)));

            case ImageExportOutcome.EncodeFailed:
                return (null, Tr.Get("Preset_ImageCodeUnreadable"));

            default:
                return (null, Tr.Get("Preset_ImageCodeNoImage"));
        }
    }

    private enum ImageExportOutcome { NoImage, ReadFailed, EncodeFailed, Encoded }

    // ================================================================== test tâm ngắm

    /// <summary>Ứng dụng nền trắng mặc định để test; có sẵn trên hầu hết máy Windows.</summary>
    internal const string TestAppName = "notepad.exe";

    /// <summary>
    /// "Test tâm ngắm": bật overlay rồi mở Notepad làm nền trắng. Máy không có Notepad thì cho chọn
    /// một ứng dụng/game bất kỳ, mở nó, và tạo preset + game profile riêng cho ứng dụng đó.
    /// </summary>
    /// <remarks>
    /// Mọi lỗi đều quy về hộp thoại thông báo; không exception nào được thoát ra ngoài lệnh này.
    /// </remarks>
    [RelayCommand]
    private async Task TestCrosshairAsync()
    {
        try
        {
            // 1. Bật overlay. Ghi vào cài đặt: App áp quy tắc hiện/ẩn và cập nhật khay như mọi lần bật.
            OverlayEnabled = true;

            // 2. Mở Notepad. Phiên test phải bắt đầu TRƯỚC khi mở, để lần Notepad lên foreground đầu
            // tiên đã được nhận ra.
            _autoSwitcher.BeginCrosshairTest(TestAppName);
            try
            {
                _launcher.Launch(TestAppName);
                return;
            }
            catch (Exception ex) when (IsLaunchFailure(ex))
            {
                _autoSwitcher.EndCrosshairTest();
                _logger.LogWarning(ex, "Không mở được {App} để test tâm ngắm.", TestAppName);
            }

            // 3. Không có Notepad: cho chọn ứng dụng khác.
            if (!_dialogs.Confirm(Tr.Get("Test_Title"), Tr.Get("Test_NotepadMissing"))) return;

            var path = _dialogs.PickFileToOpen(Tr.Get("Test_ExeFilter"));
            if (path is null) return;

            await TestWithAppAsync(path);
        }
        catch (Exception ex)
        {
            // Lưới an toàn cuối: nút test không bao giờ được làm sập ứng dụng.
            Report(ex, Tr.Get("Test_Failed"));
        }
    }

    private async Task TestWithAppAsync(string path)
    {
        var appName = global::System.IO.Path.GetFileNameWithoutExtension(path);
        var processName = global::System.IO.Path.GetFileName(path);

        try
        {
            _launcher.Launch(path);
        }
        catch (global::System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            // Người dùng bấm "Không" ở hộp thoại UAC — không phải lỗi, không tạo gì cả.
            return;
        }
        catch (Exception ex) when (IsLaunchFailure(ex))
        {
            _logger.LogWarning(ex, "Không mở được {Path}.", path);
            _dialogs.ShowMessage(Tr.Get("Test_Title"), Tr.Format("Test_LaunchFailed", processName, ex.Message), isError: true);
            return;
        }

        var preset = await CreatePresetForAppAsync(appName);
        var rule = BindPresetToProcess(appName, processName, preset);

        // Ứng dụng có thể đã lên foreground trước khi rule kịp tồn tại.
        _autoSwitcher.Reevaluate();

        _dialogs.ShowMessage(Tr.Get("Test_Title"), Tr.Format("Test_ProfileCreated", preset.Name, rule.Pattern));
    }

    /// <summary>Sao chép thông số tâm ngắm đang chọn thành preset mới "Profile [tên app]", và chọn nó.</summary>
    private async Task<CrosshairProfile> CreatePresetForAppAsync(string appName)
    {
        var name = Tr.Format("Test_ProfileName", appName);

        CrosshairProfile preset;
        if ((SelectedPreset ?? _library.Active) is { } source)
        {
            preset = source.Clone(newIdentity: true);
            preset.Name = name;
            preset = await _library.AddAsync(preset);   // thêm vào danh sách VÀ đặt làm preset đang chọn
        }
        else
        {
            preset = await _library.CreateAsync();
            preset.Name = name;
        }

        return preset;
    }

    /// <summary>
    /// Gắn preset với tiến trình qua game profile — đúng cơ chế tự đổi preset theo game sẵn có, nên
    /// mỗi lần ứng dụng đó lên foreground, preset này tự được áp dụng.
    /// </summary>
    /// <remarks>Đã có rule cho tiến trình này thì trỏ nó sang preset mới thay vì tạo rule trùng.</remarks>
    internal GameProfile BindPresetToProcess(string appName, string processName, CrosshairProfile preset)
    {
        var rules = _settings.Current.GameProfiles;
        var rule = rules.FirstOrDefault(r =>
            r.MatchMode == ProcessMatchMode.ProcessName
            && string.Equals(r.Pattern.Trim(), processName, StringComparison.OrdinalIgnoreCase));

        if (rule is null)
        {
            rule = new GameProfile
            {
                Name = appName,
                MatchMode = ProcessMatchMode.ProcessName,
                Pattern = processName,
                Priority = rules.Count,
            };
            rules.Add(rule);
        }

        rule.PresetId = preset.Id;
        rule.Behavior = GameProfileBehavior.ShowPreset;
        rule.Enabled = true;

        _settings.RequestSave();
        return rule;
    }

    /// <summary>ERROR_CANCELLED: người dùng từ chối hộp thoại UAC.</summary>
    private const int ErrorCancelled = 1223;

    private static bool IsLaunchFailure(Exception ex) =>
        ex is global::System.ComponentModel.Win32Exception or InvalidOperationException
            or global::System.IO.FileNotFoundException or UnauthorizedAccessException or PlatformNotSupportedException;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _library.ActiveChanged -= OnLibraryActiveChanged;
        _settings.Current.PropertyChanged -= OnSettingsChanged;
        TranslationSource.Instance.PropertyChanged -= OnLanguageChanged;

        Editor.Dispose();
        General.Dispose();
        Hotkeys.Dispose();
        Games.Dispose();
        SystemInfo.Dispose();
    }

    private void Report(Exception ex, string message)
    {
        _logger.LogError(ex, "{Message}", message);
        _dialogs.ShowMessage(Core.AppInfo.DisplayName, $"{message}\n\n{ex.Message}");
    }
}
