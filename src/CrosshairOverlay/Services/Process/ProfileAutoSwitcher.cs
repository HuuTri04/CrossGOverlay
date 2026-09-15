using CrosshairOverlay.Core.Abstractions;
using CrosshairOverlay.Core.Models;
using Microsoft.Extensions.Logging;

namespace CrosshairOverlay.Services.Process;

/// <inheritdoc cref="IProfileAutoSwitcher"/>
/// <remarks>
/// Nơi DUY NHẤT ghép watcher + matcher + thư viện preset + overlay. Các thành phần kia đều
/// không biết gì về nhau.
/// </remarks>
public sealed class ProfileAutoSwitcher : IProfileAutoSwitcher
{
    private readonly IForegroundWindowWatcher _watcher;
    private readonly IGameProfileMatcher _matcher;
    private readonly IPresetLibrary _library;
    private readonly IOverlayController _overlay;
    private readonly IAppSettingsService _settings;
    private readonly ILogger<ProfileAutoSwitcher> _logger;

    /// <summary>
    /// Preset người dùng chọn tay, để trả về khi rời khỏi game. Nếu không nhớ, thoát game xong
    /// sẽ mắc kẹt ở preset của game đó.
    /// </summary>
    private Guid _presetBeforeMatch;

    /// <summary>
    /// PID của tiến trình đang khớp, để phân biệt "game vẫn chạy, người dùng chỉ Alt-Tab sang
    /// cửa sổ Settings" với "game đã thoát hẳn".
    /// </summary>
    private int _matchedProcessId;

    /// <summary>Đã cảnh báo Exclusive Fullscreen cho tiến trình nào rồi — mỗi game chỉ báo một lần.</summary>
    private readonly HashSet<string> _warnedProcesses = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Ứng dụng của phiên "Test tâm ngắm" đang mở, null nếu không có phiên nào.</summary>
    private string? _testProcessName;

    /// <summary>PID của cửa sổ test đã thấy ở foreground, để biết khi nào nó đóng.</summary>
    private int _testProcessId;

    /// <summary>Foreground ngoài gần nhất là cửa sổ test.</summary>
    private bool _inTestWindow;

    private bool _started;
    private bool _disposed;

    public ProfileAutoSwitcher(
        IForegroundWindowWatcher watcher,
        IGameProfileMatcher matcher,
        IPresetLibrary library,
        IOverlayController overlay,
        IAppSettingsService settings,
        ILogger<ProfileAutoSwitcher> logger)
    {
        _watcher = watcher;
        _matcher = matcher;
        _library = library;
        _overlay = overlay;
        _settings = settings;
        _logger = logger;
    }

    public bool IsEnabled
    {
        get => _settings.Current.AutoSwitchByGameProfile;
        set
        {
            _settings.Current.AutoSwitchByGameProfile = value;
            _settings.RequestSave();

            if (!value) RestoreManualPreset();
        }
    }

    public GameProfile? ActiveGameProfile { get; private set; }

    public bool IsShowingCrosshairTest => _inTestWindow;

    public event EventHandler? CrosshairTestStateChanged;

    private void SetInTestWindow(bool value)
    {
        if (_inTestWindow == value) return;
        _inTestWindow = value;
        CrosshairTestStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler<ForegroundWindowInfo>? ExclusiveFullscreenDetected;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started) return;

        _started = true;
        _watcher.ForegroundChanged += OnForegroundChanged;
        _watcher.TrackedWindowClosed += OnTrackedWindowClosed;
        _watcher.Start();
    }

    public void Stop()
    {
        if (!_started) return;

        _started = false;
        _watcher.ForegroundChanged -= OnForegroundChanged;
        _watcher.TrackedWindowClosed -= OnTrackedWindowClosed;
        _watcher.Stop();
    }

    private void OnForegroundChanged(object? sender, ForegroundWindowChangedEventArgs e)
    {
        var window = e.Current;

        // Cửa sổ của chính ứng dụng, thường là Settings. GIỮ NGUYÊN profile game đang áp dụng:
        // người dùng cần thấy crosshair đúng như trong game khi đang chỉnh nó. Chỉ trả về preset
        // thủ công nếu tiến trình game đã thoát hẳn — kiểm tra một lần tại đây, không polling.
        if (window.IsOwnProcess)
        {
            if (ActiveGameProfile is not null && !IsMatchedProcessAlive()) ClearMatch();
            return;
        }

        WarnIfExclusiveFullscreen(window);

        if (IsTestTarget(window))
        {
            // Test tâm ngắm: người dùng muốn thấy preset ĐANG CHỈNH. Không đổi preset theo game
            // profile nào (máy có sẵn profile cho Notepad thì test sẽ hiện nhầm preset khác), và hiện
            // kể cả ở chế độ "chỉ hiện trong game".
            ActiveGameProfile = null;
            _matchedProcessId = 0;
            SetInTestWindow(true);
            _testProcessId = window.ProcessId;
            _watcher.Track(window);
            ApplyVisibility();
            return;
        }

        SetInTestWindow(false);

        if (!IsEnabled) return;

        var match = _matcher.Match(window, _settings.Current.GameProfiles);

        if (match is null)
        {
            ClearMatch();
            return;
        }

        ActiveGameProfile = match;
        _matchedProcessId = window.ProcessId;

        // Để ý riêng cửa sổ này: đóng game xong Windows có thể không trao foreground cho ai, và
        // nếu chỉ chờ sự kiện foreground thì crosshair nằm lại tới khi người dùng Alt-Tab.
        _watcher.Track(window);

        // Ghi nhớ lựa chọn thủ công ngay trước lần khớp ĐẦU TIÊN.
        if (_presetBeforeMatch == Guid.Empty && _library.Active is { } current)
            _presetBeforeMatch = current.Id;

        if (match.Behavior == GameProfileBehavior.HideOverlay)
        {
            _logger.LogDebug("Game profile '{Name}' yêu cầu ẩn overlay.", match.Name);
            ApplyVisibility();
            return;
        }

        var preset = _library.Presets.FirstOrDefault(p => p.Id == match.PresetId);
        if (preset is null)
        {
            _logger.LogWarning(
                "Game profile '{Name}' trỏ tới preset không còn tồn tại ({PresetId}).",
                match.Name, match.PresetId);
            return;
        }

        _logger.LogInformation(
            "{Process} → áp preset '{Preset}' theo profile '{Profile}'.",
            window.ProcessName, preset.Name, match.Name);

        _library.SetActive(preset);
        ApplyVisibility();
    }

    /// <summary>Cửa sổ game đang khớp vừa đóng, hoặc tiến trình game đã thoát.</summary>
    /// <remarks>
    /// Nếu foreground đổi theo, watcher báo ngay sau đây và mọi thứ đi qua
    /// <see cref="OnForegroundChanged"/>. Chỗ cần xử lý riêng là khi foreground KHÔNG đổi vì đang
    /// là cửa sổ Settings: nhánh đó giữ nguyên profile khi tiến trình game còn sống, mà lúc cửa sổ
    /// vừa đóng thì tiến trình thường chưa kịp thoát.
    /// </remarks>
    private void OnTrackedWindowClosed(object? sender, ForegroundWindowInfo closed)
    {
        if (_testProcessName is not null && closed.ProcessId != 0 && closed.ProcessId == _testProcessId)
        {
            _logger.LogInformation("Đóng cửa sổ test {Process}, kết thúc phiên test tâm ngắm.", closed.ProcessName);
            EndCrosshairTest();
            return;
        }

        if (ActiveGameProfile is null || closed.ProcessId != _matchedProcessId) return;

        _logger.LogDebug("Cửa sổ của {Process} đã đóng, bỏ profile '{Profile}'.",
            closed.ProcessName, ActiveGameProfile.Name);
        ClearMatch();
    }

    private void ClearMatch()
    {
        ActiveGameProfile = null;
        _matchedProcessId = 0;
        HandleNoMatch();
    }

    /// <summary>
    /// Tiến trình đã khớp còn chạy hay không. Dùng <c>GetProcessById</c> chứ không phải
    /// <c>OpenProcess</c>: game chạy quyền admin sẽ từ chối mở handle, và ta sẽ tưởng nhầm
    /// là nó đã thoát.
    /// </summary>
    private bool IsMatchedProcessAlive()
    {
        if (_matchedProcessId == 0) return false;

        try
        {
            using var process = global::System.Diagnostics.Process.GetProcessById(_matchedProcessId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            // Không còn tiến trình nào mang PID đó.
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (global::System.ComponentModel.Win32Exception)
        {
            // Tiến trình CÓ tồn tại nhưng từ chối cho hỏi trạng thái (game chạy quyền admin, được
            // anti-cheat bảo vệ). Coi là còn sống; lúc nó thoát thật, watcher sẽ báo.
            return true;
        }
    }

    private void HandleNoMatch()
    {
        // Chế độ "chỉ hiện trong game": overlay sắp ẩn, giữ nguyên preset để lần vào game kế tiếp
        // không bị nháy qua preset thủ công.
        if (!_settings.Current.ShowOnlyInMatchedGames) RestoreManualPreset();

        ApplyVisibility();
    }

    public void ApplyVisibility() =>
        _overlay.SetVisible(ShouldShowOverlay(
            _settings.Current.OverlayEnabled,
            IsEnabled,
            _settings.Current.ShowOnlyInMatchedGames,
            ActiveGameProfile,
            _inTestWindow));

    public void BeginCrosshairTest(string processName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(processName);

        _testProcessName = processName.Trim();
        _testProcessId = 0;
        _logger.LogInformation("Bắt đầu phiên test tâm ngắm với {Process}.", _testProcessName);
    }

    public void EndCrosshairTest()
    {
        if (_testProcessName is null && !_inTestWindow) return;

        _testProcessName = null;
        _testProcessId = 0;
        SetInTestWindow(false);
        ApplyVisibility();
    }

    public void Reevaluate()
    {
        if (!_started) return;

        var current = _watcher.Current;
        OnForegroundChanged(this, new ForegroundWindowChangedEventArgs(current, current));
    }

    private bool IsTestTarget(ForegroundWindowInfo window) =>
        _testProcessName is not null
        && window.IsValid
        && string.Equals(window.ProcessName, _testProcessName, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Quy tắc hiện/ẩn overlay, tách thành hàm thuần để kiểm thử từng tổ hợp.
    /// </summary>
    /// <param name="overlayEnabled">Người dùng có bật overlay không (menu khay, phím tắt, Settings).</param>
    /// <param name="autoSwitchEnabled">Có tự đổi theo game không. Tắt thì không có khái niệm "khớp".</param>
    /// <param name="showOnlyInMatchedGames">Chỉ hiện khi foreground khớp một game profile.</param>
    /// <param name="activeProfile">Profile đang khớp với cửa sổ foreground, null nếu không khớp.</param>
    /// <param name="inTestWindow">Foreground là cửa sổ của phiên "Test tâm ngắm".</param>
    internal static bool ShouldShowOverlay(
        bool overlayEnabled, bool autoSwitchEnabled, bool showOnlyInMatchedGames, GameProfile? activeProfile,
        bool inTestWindow = false)
    {
        if (!overlayEnabled) return false;

        // Phiên test luôn hiện: đó là mục đích duy nhất của nó.
        if (inTestWindow) return true;

        // Không tự đổi theo game thì không có game nào "khớp" được: tuỳ chọn chỉ-hiện-trong-game
        // lẫn profile ẩn overlay đều không còn nghĩa, bật là hiện.
        if (!autoSwitchEnabled) return true;

        if (activeProfile is { Behavior: GameProfileBehavior.HideOverlay }) return false;
        if (activeProfile is not null) return true;

        return !showOnlyInMatchedGames;
    }

    private void RestoreManualPreset()
    {
        if (_presetBeforeMatch == Guid.Empty) return;

        var preset = _library.Presets.FirstOrDefault(p => p.Id == _presetBeforeMatch);
        _presetBeforeMatch = Guid.Empty;

        if (preset is null) return;

        _logger.LogDebug("Rời game, trả về preset '{Preset}'.", preset.Name);
        _library.SetActive(preset);
    }

    private void WarnIfExclusiveFullscreen(ForegroundWindowInfo window)
    {
        if (!_settings.Current.WarnOnExclusiveFullscreen) return;
        if (window.Fullscreen != FullscreenKind.LikelyExclusive) return;
        if (string.IsNullOrEmpty(window.ProcessName)) return;

        // Mỗi tiến trình chỉ cảnh báo một lần mỗi phiên chạy — nếu không, mỗi lần Alt-Tab
        // về game lại bật một thông báo.
        if (!_warnedProcesses.Add(window.ProcessName)) return;

        _logger.LogInformation(
            "Phát hiện {Process} nhiều khả năng đang chạy Exclusive Fullscreen.", window.ProcessName);

        ExclusiveFullscreenDetected?.Invoke(this, window);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }
}
