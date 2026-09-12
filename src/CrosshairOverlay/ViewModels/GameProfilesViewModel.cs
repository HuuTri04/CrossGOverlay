using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CrosshairOverlay.Core.Abstractions;
using CrosshairOverlay.Core.Models;
using CrosshairOverlay.Localization;

namespace CrosshairOverlay.ViewModels;

/// <summary>ViewModel cho tab "Game": liên kết tiến trình game với preset.</summary>
public sealed partial class GameProfilesViewModel : ObservableObject, IDisposable
{
    /// <summary>Tiến trình dùng làm bia tập bắn khi thử tính năng tự đổi preset.</summary>
    private const string MockProcessName = "notepad.exe";

    private readonly IAppSettingsService _settings;
    private readonly IPresetLibrary _library;
    private readonly IForegroundWindowWatcher _watcher;
    private readonly IGameProfileMatcher _matcher;
    private readonly IRunningApplicationScanner _scanner;

    private bool _disposed;

    public GameProfilesViewModel(
        IAppSettingsService settings,
        IPresetLibrary library,
        IForegroundWindowWatcher watcher,
        IGameProfileMatcher matcher,
        IRunningApplicationScanner scanner)
    {
        _settings = settings;
        _library = library;
        _watcher = watcher;
        _matcher = matcher;
        _scanner = scanner;

        Profiles = settings.Current.GameProfiles;
        _selectedProfile = Profiles.FirstOrDefault();
        MatchModes = BuildMatchModes();
        Behaviors = BuildBehaviors();

        _watcher.ForegroundChanged += OnForegroundChanged;
        TranslationSource.Instance.PropertyChanged += OnLanguageChanged;

        UpdateDiagnostics(_watcher.LastExternal);
    }

    public ObservableCollection<GameProfile> Profiles { get; }

    public ReadOnlyObservableCollection<CrosshairProfile> Presets => _library.Presets;

    /// <summary>Danh sách giữ nguyên khi đổi ngôn ngữ; từng mục tự báo nhãn đã đổi.</summary>
    public IReadOnlyList<LocalizedOption<ProcessMatchMode>> MatchModes { get; }

    public IReadOnlyList<LocalizedOption<GameProfileBehavior>> Behaviors { get; }

    [ObservableProperty] private GameProfile? _selectedProfile;

    [ObservableProperty] private string _hint = string.Empty;

    // ---- khung tạo profile mới, mở ngay trong tab ----

    [ObservableProperty] private bool _isCreatingProfile;

    /// <summary>Bản nháp đang soạn; chỉ vào danh sách thật khi người dùng bấm Lưu.</summary>
    [ObservableProperty] private GameProfile? _draft;

    [ObservableProperty] private IReadOnlyList<RunningApplication> _runningApps = [];

    [ObservableProperty] private RunningApplication? _selectedRunningApp;

    // ---- bảng chẩn đoán, cập nhật mỗi khi cửa sổ foreground đổi ----

    [ObservableProperty] private string _detectedProcess = "—";
    [ObservableProperty] private string _detectedTitle = "—";
    [ObservableProperty] private string _detectedDisplayMode = "—";
    [ObservableProperty] private string _detectedMatch = "—";

    /// <summary>Không đọc được tên file thực thi — gần như luôn vì tiến trình chạy quyền admin.</summary>
    [ObservableProperty] private bool _processNameBlocked;

    public bool AutoSwitchEnabled
    {
        get => _settings.Current.AutoSwitchByGameProfile;
        set
        {
            if (_settings.Current.AutoSwitchByGameProfile == value) return;

            _settings.Current.AutoSwitchByGameProfile = value;
            _settings.RequestSave();
            OnPropertyChanged();
        }
    }

    public bool ShowOnlyInMatchedGames
    {
        get => _settings.Current.ShowOnlyInMatchedGames;
        set
        {
            if (_settings.Current.ShowOnlyInMatchedGames == value) return;

            _settings.Current.ShowOnlyInMatchedGames = value;
            _settings.RequestSave();
            OnPropertyChanged();
        }
    }

    private static IReadOnlyList<LocalizedOption<ProcessMatchMode>> BuildMatchModes() =>
    [
        new(ProcessMatchMode.ProcessName, "MatchMode_ProcessName"),
        new(ProcessMatchMode.WindowTitleContains, "MatchMode_WindowTitle"),
        new(ProcessMatchMode.ExecutablePath, "MatchMode_ExecutablePath"),
    ];

    private static IReadOnlyList<LocalizedOption<GameProfileBehavior>> BuildBehaviors() =>
    [
        new(GameProfileBehavior.ShowPreset, "Behavior_ShowPreset"),
        new(GameProfileBehavior.HideOverlay, "Behavior_HideOverlay"),
    ];

    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs e)
    {
        foreach (var mode in MatchModes) mode.Refresh();
        foreach (var behavior in Behaviors) behavior.Refresh();

        Hint = string.Empty;
        UpdateDiagnostics(_watcher.LastExternal);
    }

    private void OnForegroundChanged(object? sender, ForegroundWindowChangedEventArgs e)
    {
        // Bảng này để soi GAME. Cửa sổ Settings của chính app giành focus không phải thông tin
        // hữu ích, và sẽ xoá mất thứ người dùng đang muốn đọc.
        if (e.Current.IsOwnProcess) return;

        UpdateDiagnostics(e.Current);
    }

    private void UpdateDiagnostics(ForegroundWindowInfo window)
    {
        var none = Tr.Get("Games_None");

        if (!window.IsValid)
        {
            DetectedProcess = none;
            DetectedTitle = none;
            DetectedDisplayMode = none;
            DetectedMatch = none;
            ProcessNameBlocked = false;
            return;
        }

        ProcessNameBlocked = window.ProcessNameUnavailable;

        DetectedProcess = ProcessNameBlocked
            ? Tr.Get("Games_Blocked")
            : string.IsNullOrEmpty(window.ProcessName) ? none : window.ProcessName;

        DetectedTitle = string.IsNullOrEmpty(window.WindowTitle) ? none : window.WindowTitle;

        DetectedDisplayMode = Tr.Get(window.Fullscreen switch
        {
            FullscreenKind.Borderless => "Display_Borderless",
            FullscreenKind.LikelyExclusive => "Display_LikelyExclusive",
            _ => "Display_Windowed",
        });

        var match = _matcher.Match(window, _settings.Current.GameProfiles);
        DetectedMatch = match?.Name ?? Tr.Get("Games_NoMatch");
    }

    // ------------------------------------------------------------------ tạo profile mới

    /// <summary>
    /// Mở khung soạn thảo ngay trong tab, kèm danh sách ứng dụng đang chạy — người dùng chọn
    /// trong danh sách thay vì phải tự biết tên file thực thi của game.
    /// </summary>
    [RelayCommand]
    private void BeginNewProfile()
    {
        Draft = new GameProfile
        {
            Name = Tr.Get("Games_NewName"),
            MatchMode = ProcessMatchMode.ProcessName,
            Pattern = string.Empty,
            PresetId = _library.Active?.Id ?? Guid.Empty,
            Priority = Profiles.Count,
        };

        Hint = string.Empty;
        IsCreatingProfile = true;
        ScanRunningApps();
    }

    [RelayCommand]
    private void ScanRunningApps()
    {
        RunningApps = _scanner.Scan();
        SelectedRunningApp = null;
    }

    /// <summary>Điền bản nháp từ ứng dụng được chọn trong danh sách.</summary>
    [RelayCommand]
    private void UseSelectedRunningApp()
    {
        if (SelectedRunningApp is not { } app || Draft is null) return;

        Draft.Name = string.IsNullOrWhiteSpace(app.WindowTitle) ? app.ProcessName : app.WindowTitle;

        if (app.ExecutablePath is null)
        {
            // Không đọc được tên file ⇒ tiến trình chạy quyền cao hơn. So khớp theo tiêu đề
            // cửa sổ là cách duy nhất còn dùng được ở quyền thường.
            Draft.MatchMode = ProcessMatchMode.WindowTitleContains;
            Draft.Pattern = app.WindowTitle;
            Hint = Tr.Get("Games_HintAdminTitle");
            return;
        }

        Draft.MatchMode = ProcessMatchMode.ProcessName;
        Draft.Pattern = app.ProcessName;
        Hint = string.Empty;
    }

    [RelayCommand]
    private void SaveNewProfile()
    {
        if (Draft is not { } draft) return;

        if (string.IsNullOrWhiteSpace(draft.Pattern))
        {
            Hint = Tr.Get("Games_NeedPattern");
            return;
        }

        Profiles.Add(draft);
        SelectedProfile = draft;
        _settings.RequestSave();

        IsCreatingProfile = false;
        Draft = null;
        Hint = Tr.Format("Games_HintAdded", draft.Pattern);
    }

    [RelayCommand]
    private void CancelNewProfile()
    {
        IsCreatingProfile = false;
        Draft = null;
        Hint = string.Empty;
    }

    // ------------------------------------------------------------------ lối tắt

    /// <summary>
    /// Thêm một profile trỏ tới Notepad để thử tính năng tự đổi preset mà không phải mở game
    /// nặng: bật Notepad lên là preset phải đổi, chuyển đi là phải trả về preset cũ.
    /// </summary>
    [RelayCommand]
    private void AddMockTarget()
    {
        var existing = Profiles.FirstOrDefault(
            p => string.Equals(p.Pattern, MockProcessName, StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
        {
            SelectedProfile = existing;
            Hint = Tr.Format("Games_HintMockExists", MockProcessName);
            return;
        }

        var profile = new GameProfile
        {
            Name = Tr.Get("Games_MockName"),
            MatchMode = ProcessMatchMode.ProcessName,
            Pattern = MockProcessName,
            PresetId = PickDistinctPreset(),
            Priority = Profiles.Count,
        };

        Profiles.Add(profile);
        SelectedProfile = profile;
        _settings.RequestSave();

        var presetName = Presets.FirstOrDefault(p => p.Id == profile.PresetId)?.Name ?? "?";
        Hint = Tr.Format("Games_HintMockAdded", MockProcessName, presetName);
    }

    /// <summary>Chọn một preset KHÁC preset đang dùng, để thấy rõ việc chuyển đổi có xảy ra.</summary>
    private Guid PickDistinctPreset()
    {
        var activeId = _library.Active?.Id;
        var other = Presets.FirstOrDefault(p => p.Id != activeId);
        return (other ?? Presets.FirstOrDefault())?.Id ?? Guid.Empty;
    }

    /// <summary>
    /// Điền sẵn từ cửa sổ ngoài gần nhất — thường đúng là game người dùng vừa Alt-Tab rời khỏi.
    /// </summary>
    [RelayCommand]
    private void AddFromCurrentWindow()
    {
        var window = _watcher.LastExternal;

        if (!window.IsValid)
        {
            Hint = Tr.Get("Games_HintNoWindow");
            return;
        }

        if (window.ProcessNameUnavailable)
        {
            AddTitleProfile(window);
            return;
        }

        if (string.IsNullOrEmpty(window.ProcessName))
        {
            Hint = Tr.Get("Games_HintNoProcessName");
            return;
        }

        var existing = Profiles.FirstOrDefault(
            p => string.Equals(p.Pattern, window.ProcessName, StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
        {
            SelectedProfile = existing;
            Hint = Tr.Format("Games_HintExists", window.ProcessName);
            return;
        }

        var profile = new GameProfile
        {
            Name = string.IsNullOrWhiteSpace(window.WindowTitle) ? window.ProcessName : window.WindowTitle,
            MatchMode = ProcessMatchMode.ProcessName,
            Pattern = window.ProcessName,
            PresetId = _library.Active?.Id ?? Guid.Empty,
            Priority = Profiles.Count,
        };

        Profiles.Add(profile);
        SelectedProfile = profile;
        _settings.RequestSave();

        Hint = Tr.Format("Games_HintAdded", window.ProcessName);
    }

    private void AddTitleProfile(ForegroundWindowInfo window)
    {
        if (string.IsNullOrWhiteSpace(window.WindowTitle))
        {
            Hint = Tr.Get("Games_HintAdminNoTitle");
            return;
        }

        var profile = new GameProfile
        {
            Name = window.WindowTitle,
            MatchMode = ProcessMatchMode.WindowTitleContains,
            Pattern = window.WindowTitle,
            PresetId = _library.Active?.Id ?? Guid.Empty,
            Priority = Profiles.Count,
        };

        Profiles.Add(profile);
        SelectedProfile = profile;
        _settings.RequestSave();

        Hint = Tr.Get("Games_HintAdminTitle");
    }

    [RelayCommand]
    private void RemoveProfile()
    {
        if (SelectedProfile is not { } target) return;

        Profiles.Remove(target);
        SelectedProfile = Profiles.FirstOrDefault();
        _settings.RequestSave();
    }

    [RelayCommand]
    private void PersistSettings() => _settings.RequestSave();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Watcher là singleton sống suốt phiên; ViewModel thì chết theo cửa sổ.
        _watcher.ForegroundChanged -= OnForegroundChanged;
        TranslationSource.Instance.PropertyChanged -= OnLanguageChanged;
    }
}
