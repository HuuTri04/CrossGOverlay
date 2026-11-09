using System.Windows;
using CrosshairOverlay.Composition;
using CrosshairOverlay.Core;
using CrosshairOverlay.Core.Abstractions;
using CrosshairOverlay.Core.Models;
using CrosshairOverlay.Localization;
using CrosshairOverlay.Services.Input;
using CrosshairOverlay.Services.Hardware;
using CrosshairOverlay.Services.Logging;
using CrosshairOverlay.Services.Storage;
using CrosshairOverlay.Services.System;
using CrosshairOverlay.Services.Updates;
using CrosshairOverlay.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CrosshairOverlay;

/// <summary>
/// Composition root: chỉ lo TRÌNH TỰ khởi động. Việc cái gì phụ thuộc cái gì nằm ở
/// <see cref="ServiceRegistration"/>.
/// </summary>
internal sealed class AppBootstrapper
{
    private readonly App _app;
    private AppLogging? _logging;
    private ServiceProvider? _provider;
    private ILogger<App>? _log;
    private bool _fatalReported;
    private StartupArguments _arguments = StartupArguments.None;
    private bool _overlayStarted;
    private bool _foregroundServicesStarted;
    private bool _behaviorStarted;
    private bool _backgroundServicesStarted;
    private bool _shutdownStarted;
    private bool _settingsReady;
    private bool _trayReady;
    private bool _trayEventsAttached;
    private bool _openSettingsWhenReady;
    private string? _restoreError;
    private RgbSyncService? _rgbSyncService;

    /// <summary>Từ hàm dựng tới OnStartup: đọc App.xaml (gồm các ResourceDictionary gộp) và khởi động vòng lặp.</summary>
    private double _appXamlMs;

    /// <summary>Loading window WPF, chỉ tồn tại trong lúc mở Settings lần đầu.</summary>
    private SplashWindow? _splash;

    private void OnSplashFirstFrame(object? sender, EventArgs e)
    {
        if (sender is SplashWindow splash) splash.FirstFrameRendered -= OnSplashFirstFrame;

        try
        {
            using var process = global::System.Diagnostics.Process.GetCurrentProcess();
            _log?.LogInformation(
                "Màn hình chờ có khung hình đầu sau {Elapsed:0} ms kể từ khi tiến trình khởi động.",
                (DateTime.Now - process.StartTime).TotalMilliseconds);
        }
        catch (Exception ex)
        {
            _log?.LogDebug(ex, "Không thể đo thời điểm khung hình đầu của splash.");
        }
    }

    /// <summary>
    /// Từ lúc Windows tạo tiến trình tới lúc hàm dựng của <see cref="App"/> chạy: nạp .NET runtime và
    /// các assembly cần để dựng đối tượng Application. Khoảng này KHÔNG thuộc quyền ứng dụng.
    /// </summary>
    private readonly double _runtimeStartupMs;

    /// <summary>Mốc hàm dựng chạy, để đo phần đọc App.xaml (Theme.xaml, TrayMenu.xaml) tới OnStartup.</summary>
    private readonly long _constructedAt;

    /// <remarks>
    /// Hàm Main do WPF sinh ra gọi hàm dựng này TRƯỚC <c>InitializeComponent</c>, nên hai mốc ở đây
    /// tách được "runtime nạp xong" với "App.xaml đọc xong".
    /// </remarks>
    public AppBootstrapper(App app, long constructedAt, double runtimeStartupMs)
    {
        _app = app;
        _constructedAt = constructedAt;
        _runtimeStartupMs = runtimeStartupMs;
    }

    public void Run(StartupEventArgs e)
    {
        Core.Diagnostics.StartupProfiler.Begin(_constructedAt, _runtimeStartupMs);
        _appXamlMs = global::System.Diagnostics.Stopwatch.GetElapsedTime(_constructedAt).TotalMilliseconds;
        _arguments = StartupArguments.Parse(e.Args);

        InstallGlobalExceptionHandlers();
        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        // Đo từng bước ngay từ đầu: "app khởi động chậm" chỉ sửa được khi biết chậm ở bước nào.
        var boot = new Core.Diagnostics.BootTimeline();

        try
        {
            var paths = AppPathProvider.FromStorageLocation();
            await Task.Run(paths.EnsureCreated, App.ShutdownToken);

            _logging = AppLogging.Create(paths);
            boot.Mark("log");

            _provider = RegisterServices(paths);
            _log = _provider.GetRequiredService<ILogger<App>>();
            boot.Mark("DI");
            if (Core.Diagnostics.StartupProfiler.Enabled)
                _log.LogInformation("BenchmarkTimeline AppInitialized {Elapsed:0.###} ms.",
                    Core.Diagnostics.StartupProfiler.ElapsedMs());

            _log.LogInformation(
                "CrosshairOverlay {Version} khởi động. Dữ liệu: {Root}",
                typeof(App).Assembly.GetName().Version, paths.RootDirectory);

            // Khởi động lại (vd khôi phục cài đặt gốc): chờ instance cũ thoát hẳn TRƯỚC khi giành
            // quyền chạy duy nhất — nếu không, instance này sẽ tưởng đã có app chạy và tự thoát.
            if (_arguments.WaitForProcessId is { } previous) WaitForPreviousInstance(previous);

            if (!AcquireSingleInstance()) return;

            if (_arguments.FactoryReset) PerformFactoryReset(paths);
            if (_arguments.RestoreArchive is { } archivePath)
            {
                try
                {
                    DataBackupArchive.Restore(paths, archivePath);
                    _log.LogInformation("Đã khôi phục dữ liệu từ ZIP trước khi nạp giao diện.");
                }
                catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException
                                           or System.IO.InvalidDataException or System.Text.Json.JsonException
                                           or OverflowException or NotSupportedException)
                {
                    _restoreError = ex.Message;
                    _log.LogError(ex, "Khôi phục ZIP thất bại; tiếp tục với dữ liệu hiện có.");
                }
            }

            var settings = _provider.GetRequiredService<IAppSettingsService>();
            await settings.LoadAsync(App.ShutdownToken).ConfigureAwait(true);
            UiAnimationFrameRate.TargetFps = settings.Current.UiTargetFps;
            _logging.SetLevel(settings.Current.LogLevel);
            boot.Mark("cài đặt");

            // Áp ngôn ngữ TRƯỚC khi dựng bất kỳ cửa sổ hay menu khay nào.
            LanguageCatalog.Apply(settings.Current.Language);

            // Chế độ render cũng phải có trước cửa sổ đầu tiên.
            Services.Overlay.OverlayBehaviorController.ApplyRenderMode(settings.Current.UseHardwareAcceleration);

            // Overlay là đường hiển thị quan trọng nhất. Nạp preset và áp toàn bộ quy tắc
            // hiện/ẩn trước khi dựng Splash/Settings; không cho Settings chiếm lượt render đầu.
            var showWindow = _arguments.RestoreArchive is not null || !settings.Current.StartMinimizedToTray;

            var library = _provider.GetRequiredService<IPresetLibrary>();
            await library.InitializeAsync(settings.Current.ActivePresetId, settings.Current.PresetOrder,
                App.ShutdownToken).ConfigureAwait(true);
            library.OrderChanged += OnPresetOrderChanged;
            boot.Mark("preset");

            StartOverlay(settings, library);
            boot.Mark("overlay");
            // Bộ thống kê phải nghe trước lần nhận diện foreground đầu tiên để không
            // bỏ sót preset/profile được áp ngay khi mở ứng dụng.
            await _provider.GetRequiredService<StatsService>().StartAsync();
            StartForegroundDetection();
            ApplyStreamCapturePolicy();

            // Áp quy tắc hiện/ẩn ngay từ đầu, kể cả khi cửa sổ foreground lúc khởi động là của chính
            // ứng dụng (Settings) — trường hợp bộ tự đổi không xử lý.
            _provider.GetRequiredService<IProfileAutoSwitcher>().ApplyVisibility();

            // Sau hotkey (dùng chung Raw Input) và bộ tự đổi (biết phiên test tâm ngắm).
            _behaviorStarted = true;
            _provider.GetRequiredService<Services.Overlay.OverlayBehaviorController>().Start();

            // Render có ưu tiên cao hơn Background. Đây là điểm tách đường vẽ overlay đầu tiên
            // khỏi chi phí dựng Visual Tree của Splash/Settings. Không ép UpdateLayout: nó chỉ
            // tính layout đồng bộ, không xác nhận DWM đã present khung hình.
            await global::System.Windows.Threading.Dispatcher.Yield(global::System.Windows.Threading.DispatcherPriority.Background);
            boot.Mark("chờ overlay vẽ");

            if (App.IsShuttingDown) return;

            if (showWindow)
            {
                // Yêu cầu mở Settings từ instance thứ hai có thể đến trong lúc bootstrap.
                // Không tạo một splash mới đè lên Settings đang hiển thị.
                if (!_app.Windows.OfType<SettingsWindow>().Any(window => window.IsVisible))
                {
                    _splash = new SplashWindow();
                    _splash.FirstFrameRendered += OnSplashFirstFrame;
                    _splash.Show();
                    boot.Mark("màn hình chờ");

                    await global::System.Windows.Threading.Dispatcher.Yield(
                        global::System.Windows.Threading.DispatcherPriority.Background);
                }

                if (App.IsShuttingDown) return;

                // Exit ở khay vẫn dùng được trong khi ảnh nền Settings đang giải mã.
                StartTray();
                boot.Mark("khay");
                if (App.IsShuttingDown) return;

                var openingSettings = _provider.GetRequiredService<IDialogService>().ShowSettingsWindowAsync(window =>
                {
                    // Gán MainWindow trước khi đóng Splash; OnExplicitShutdown giữ app sống trong tray.
                    _app.MainWindow = window;
                    if (_splash is { } splash)
                    {
                        splash.FirstFrameRendered -= OnSplashFirstFrame;
                        splash.CloseQuietly();
                        _splash = null;
                    }
                });
                // Isolated Release fixture: exercise the same RequestShutdown path as
                // the tray Exit command while the backdrop task is still pending.
                if (Environment.GetEnvironmentVariable("CROSSGOVERLAY_BENCHMARK_ROOT") is not null
                    && Environment.GetEnvironmentVariable("CROSSGOVERLAY_BENCHMARK_EXIT_DURING_PRELOAD") == "1")
                    App.RequestShutdown();
                await openingSettings;
                if (App.IsShuttingDown) return;
                boot.Mark("cửa sổ Settings");

                await global::System.Windows.Threading.Dispatcher.Yield(global::System.Windows.Threading.DispatcherPriority.Background);
                boot.Mark("chờ khung hình Settings");

                // Shell registration can fail while Splash is the only WPF window.
                // Retry once with the durable Settings HWND, without failing startup.
                if (!_trayReady) StartTray();
            }
            else
            {
                StartTray();
                boot.Mark("khay");
                if (!_trayReady)
                {
                    _log?.LogError("Không thể khởi động ẩn xuống khay vì Windows Shell chưa tạo được tray icon.");
                    App.RequestShutdown(1);
                    return;
                }
            }

            if (App.IsShuttingDown) return;
            InitializeBackgroundServices(settings);
            boot.Mark("phím tắt + dịch vụ nền");

            if (_trayReady)
            {
                var tray = _provider.GetRequiredService<ITrayIconController>();
                if (showWindow)
                    _ = _app.Dispatcher.InvokeAsync(() => tray.InitializeNotifications(),
                        global::System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                else
                    tray.InitializeNotifications(); // Overlay has already had a render turn.
            }

            // A second-instance signal can arrive while presets/services are still loading.
            // Queue that request until both overlay and tray are ready, then use the same
            // coalesced singleton window path as toast, hotkeys and the tray icon.
            _settingsReady = true;
            if (_openSettingsWhenReady)
            {
                _openSettingsWhenReady = false;
                RequestOpenSettings();
            }

            if (_restoreError is not null)
                _provider.GetRequiredService<IDialogService>().ShowMessage(
                    Tr.Get("Data_Restore"), _restoreError, isError: true);

            WarnIfStorageFallback(paths);

            // Bật/tắt overlay (menu khay, phím tắt, Settings) và hai tuỳ chọn của game profile đều
            // chỉ ghi vào cài đặt; phản ứng tập trung tại đây để không nơi nào tự bật overlay thẳng.
            settings.Current.PropertyChanged += OnVisibilitySettingChanged;

            if (!showWindow)
            {
                _provider.GetRequiredService<ITrayIconController>()
                    .ShowNotification(Core.AppInfo.DisplayName, Tr.Get("Tray_RunningInTray"),
                        category: NotificationCategory.MinimizeToTray);
                boot.Mark("thông báo khay");
            }

            RefreshTrayState();
            _log.LogInformation(
                "Khởi động hoàn tất: {Runtime:0} ms nạp runtime + {AppXaml:0} ms App.xaml + {Total:0} ms trong app — nặng nhất {Summary}",
                _runtimeStartupMs, _appXamlMs, boot.TotalMs, boot.Summary());

            ScheduleImageCleanup();

            ScheduleUpdateCheck(settings);
            Core.Diagnostics.BenchmarkScenarioRunner.TryStart(
                library, _provider.GetRequiredService<ITrayIconController>());
            Core.Diagnostics.StressScenarioRunner.TryStart(
                library, _provider.GetRequiredService<IDialogService>());
        }
        catch (OperationCanceledException) when (App.IsShuttingDown)
        {
            _splash?.CloseQuietly();
            _splash = null;
        }
        catch (Exception ex)
        {
            _log?.LogCritical(ex, "Khởi động thất bại.");

            // Không để màn hình chờ treo trên màn hình phía sau thông báo lỗi.
            _splash?.CloseQuietly();
            _splash = null;

            ReportFatal(ex, Tr.Get("Error_StartupFailed"));
            App.RequestShutdown(1);
        }
    }

    // ------------------------------------------------------------------ các bước khởi động

    private ServiceProvider RegisterServices(AppPathProvider paths) =>
        ServiceRegistration.Build(paths, _logging!.Factory);

    private void InitializeBackgroundServices(IAppSettingsService settings)
    {
        _backgroundServicesStarted = true;
        StartHotkeys(settings);
        _provider!.GetRequiredService<WeaponAutoSwitchService>().Start();
        var obs = _provider!.GetRequiredService<ObsWebSocketService>();
        obs.CaptureChanged += OnObsCaptureChanged;
        obs.Start();
        ApplyStreamCapturePolicy();
        // Update checks are scheduled later; optional RGB/audio services are resolved on first use.
    }

    private void StartForegroundDetection()
    {
        var autoSwitcher = _provider!.GetRequiredService<IProfileAutoSwitcher>();
        _foregroundServicesStarted = true;
        autoSwitcher.ExclusiveFullscreenDetected += OnExclusiveFullscreenDetected;
        autoSwitcher.Start();
    }

    /// <summary>
    /// Thư mục dữ liệu tuỳ chỉnh không truy cập được (ổ đã rút, ổ mạng mất kết nối): nói rõ cho người dùng,
    /// thay vì để họ tưởng toàn bộ preset đã mất. Con trỏ giữ nguyên để lần sau cắm lại là dùng lại được.
    /// </summary>
    private void WarnIfStorageFallback(AppPathProvider paths)
    {
        if (paths.StorageFallbackFrom is not { } missing) return;

        _log!.LogWarning("Không truy cập được thư mục dữ liệu {Missing}, tạm dùng {Root}.", missing, paths.RootDirectory);
        _provider!.GetRequiredService<ITrayIconController>()
            .ShowNotification(Tr.Get("Storage_FallbackTitle"), Tr.Format("Storage_FallbackNotice", missing), isWarning: true);
    }

    private void WaitForPreviousInstance(int processId)
    {
        try
        {
            using var previous = global::System.Diagnostics.Process.GetProcessById(processId);
            if (!previous.WaitForExit(TimeSpan.FromSeconds(15)))
                _log!.LogWarning("Instance cũ ({Pid}) chưa thoát sau 15 giây, vẫn tiếp tục.", processId);
        }
        catch (ArgumentException)
        {
            // Đã thoát từ trước.
        }
        catch (Exception ex) when (ex is InvalidOperationException or global::System.ComponentModel.Win32Exception)
        {
            _log!.LogDebug(ex, "Không chờ được instance cũ.");
        }
    }

    /// <summary>
    /// Xoá dữ liệu người dùng, TRƯỚC khi bất cứ thứ gì được nạp. Bỏ luôn khởi động cùng Windows để khớp
    /// với cài đặt gốc (mặc định tắt), nếu không registry vẫn chạy app mà cài đặt lại báo là tắt.
    /// </summary>
    private void PerformFactoryReset(AppPathProvider paths)
    {
        FactoryReset.Wipe(paths, _log!);
        _provider!.GetRequiredService<IStartupService>().SetEnabled(false);
    }

    /// <summary>
    /// Hai instance cùng chạy sẽ có hai overlay chồng nhau và tranh nhau đăng ký hotkey.
    /// Instance thứ hai đánh thức instance đang chạy rồi tự thoát.
    /// </summary>
    private bool AcquireSingleInstance()
    {
        var guard = _provider!.GetRequiredService<ISingleInstanceGuard>();

        if (!guard.TryAcquire())
        {
            guard.SignalExistingInstance();
            _log?.LogInformation("Đã có instance khác, thoát.");
            App.RequestShutdown();
            return false;
        }

        guard.SecondInstanceLaunched += (_, _) => RequestOpenSettings();

        return true;
    }

    private void RequestOpenSettings()
    {
        if (_shutdownStarted || App.IsShuttingDown || _app.Dispatcher.HasShutdownStarted) return;
        if (!_app.Dispatcher.CheckAccess())
        {
            try { _ = _app.Dispatcher.BeginInvoke(new Action(RequestOpenSettings)); }
            catch (InvalidOperationException) { } // The dispatcher began shutting down meanwhile.
            return;
        }

        if (!_settingsReady)
        {
            _openSettingsWhenReady = true;
            return;
        }

        _provider?.GetRequiredService<IDialogService>().ShowSettingsWindow();
    }

    /// <summary>
    /// Dựng cửa sổ overlay và nối nó với thư viện preset (đã nạp xong từ trước).
    /// </summary>
    private void StartOverlay(IAppSettingsService settings, IPresetLibrary library)
    {
        var overlay = _provider!.GetRequiredService<IOverlayController>();

        overlay.Initialize();
        _overlayStarted = true;

        // Menu khay phải theo trạng thái THẬT của overlay: ngoài bật/tắt thủ công, game profile
        // cũng có thể ẩn/hiện overlay (HideOverlay, "chỉ hiện trong game đã khớp").
        overlay.VisibilityChanged += (_, _) => RefreshTrayState();

        overlay.SetMonitorSelection(
            settings.Current.MonitorSelectionMode,
            settings.Current.TargetMonitorDeviceName);

        // Nối thư viện với overlay ở đây chứ không trong ViewModel: preset đổi được cả khi
        // cửa sổ Settings đang đóng (hotkey, tray, auto-switch theo game).
        library.ActiveChanged += OnActivePresetChanged;

        // Thư viện đã chọn preset hoạt động TRƯỚC khi overlay tồn tại,
        // nên sự kiện đó đã qua mà chưa ai nghe — đẩy preset hiện tại sang overlay một lần ở đây.
        if (library.Active is not null) OnActivePresetChanged(this, EventArgs.Empty);

        // KHÔNG hiện overlay ở đây: quy tắc hiện/ẩn (ApplyVisibility) chạy ngay sau khi bộ tự đổi theo
        // game khởi động. Hiện trước sẽ làm crosshair nháy lên trên desktop một khoảnh khắc ở chế độ
        // "chỉ hiện trong game" rồi mới bị ẩn.
    }

    private void StartTray()
    {
        if (_trayReady) return;
        var tray = _provider!.GetRequiredService<ITrayIconController>();
        if (!_trayEventsAttached)
        {
            // COM notification activation may arrive as Register() completes. Attach first.
            tray.OpenSettingsRequested += (_, _) => RequestOpenSettings();
            tray.ToggleOverlayRequested += (_, _) => ToggleOverlay();
            tray.ExitRequested += (_, _) => App.RequestShutdown();
            _trayEventsAttached = true;
        }
        try
        {
            tray.Initialize();
            _trayReady = true;
        }
        catch (InvalidOperationException ex)
        {
            _log?.LogWarning(ex, "Không tạo được biểu tượng khay; sẽ thử lại khi Settings hiển thị.");
        }
    }

    private void StartHotkeys(IAppSettingsService settings)
    {
        var provider = _provider!;
        var window = provider.GetRequiredService<HotkeyMessageWindow>();
        var hotkeys = provider.GetRequiredService<IHotkeyService>();

        hotkeys.Attach(window.Handle);
        hotkeys.HotkeyPressed += OnHotkeyPressed;

        var result = hotkeys.Apply(settings.Current.Hotkeys);
        if (result.AllSucceeded) return;

        // Không chặn khởi động: thường chỉ một tổ hợp bị app khác chiếm, số còn lại vẫn chạy.
        var names = string.Join(", ", result.Failures.Select(f => f.Binding.ToString()));
        provider.GetRequiredService<ITrayIconController>().ShowNotification(
            Tr.Get("Hotkeys_ConflictTitle"),
            Tr.Format("Hotkeys_ConflictBody", names),
            isWarning: true);
    }

    /// <summary>Ảnh chưa dùng tới trong khoảng này thì chưa bị dọn — đường lùi cho người dùng.</summary>
    private static readonly TimeSpan UnusedImageGracePeriod = TimeSpan.FromDays(3);

    /// <summary>
    /// Dọn ảnh trong kho không còn preset nào dùng, ở nền.
    /// </summary>
    /// <remarks>
    /// Danh sách ảnh đang dùng lấy trên luồng giao diện từ thư viện TRONG BỘ NHỚ, không đọc lại
    /// đĩa: thay đổi gần nhất có thể còn nằm trong hàng đợi lưu. Bỏ qua cả lượt dọn nếu có file
    /// preset hỏng — biết đâu file đó đang dùng một ảnh trong kho.
    /// </remarks>
    private void ScheduleImageCleanup()
    {
        var provider = _provider!;
        if (provider.GetRequiredService<IPresetRepository>().LastLoadSkippedFiles)
        {
            _log?.LogInformation("Có file preset không đọc được — bỏ qua lượt dọn kho ảnh.");
            return;
        }

        var referenced = provider.GetRequiredService<IPresetLibrary>().Presets
            .Select(p => p.Image.FilePath)
            .ToList();

        var images = provider.GetRequiredService<ICustomImageStore>();

        _ = Task.Run(() =>
        {
            try
            {
                images.CleanupUnused(referenced, UnusedImageGracePeriod);
            }
            catch (Exception ex)
            {
                _log?.LogDebug(ex, "Dọn kho ảnh thất bại.");
            }
        });
    }

    /// <summary>
    /// Kiểm tra cập nhật ở nền, sau khi ứng dụng đã sẵn sàng.
    /// </summary>
    /// <remarks>
    /// Chạy trên thread pool và KHÔNG await: người dùng vào game ngay, kết quả tới lúc nào hay
    /// lúc đó. Mất mạng hay máy chủ treo cũng chỉ dẫn tới một dòng log ở mức Debug.
    /// </remarks>
    private void ScheduleUpdateCheck(IAppSettingsService settings)
    {
        if (!settings.Current.CheckForUpdates) return;

        _ = Task.Run(async () =>
        {
            try
            {
                var updates = _provider!.GetRequiredService<IUpdateService>();
                var result = await updates.CheckAsync().ConfigureAwait(false);

                // Không có bản mới thì im lặng: người dùng vừa mở app để vào game, không cần biết.
                if (result.Status != UpdateCheckStatus.UpdateAvailable || result.Update is not { } update) return;

                if (!UpdateOfferPolicy.ShouldOfferAutomatically(update.Version, settings.Current.LastUpdateAttempt))
                {
                    _log?.LogWarning(
                        "Đã cập nhật lên {Attempted} nhưng ứng dụng vẫn là {Current} — gói phát hành có thể gắn sai "
                        + "phiên bản. Không tự nhắc nữa; nút \"Kiểm tra cập nhật\" vẫn dùng được.",
                        settings.Current.LastUpdateAttempt, updates.CurrentVersion);
                    return;
                }

                await _app.Dispatcher.InvokeAsync(() => OfferUpdate(update));
            }
            catch (Exception ex)
            {
                _log?.LogDebug(ex, "Kiểm tra cập nhật nền thất bại.");
            }
        });
    }

    private void OfferUpdate(UpdateInfo update)
    {
        if (_provider is null) return;

        if (_provider.GetRequiredService<IDialogService>().ShowUpdate(update)) App.RequestShutdown();
    }

    // ------------------------------------------------------------------ sự kiện

    private void OnHotkeyPressed(object? sender, HotkeyPressedEventArgs e)
    {
        if (_provider is null) return;

        switch (e.Action)
        {
            case HotkeyAction.ToggleOverlay:
                ToggleOverlay();
                break;

            case HotkeyAction.NextPreset:
#if BENCHMARK
                Core.Diagnostics.BenchmarkPresetProbe.RequestReceived("HotkeyNext");
#endif
                _provider.GetRequiredService<IPresetLibrary>().StepActive(+1);
                break;

            case HotkeyAction.PreviousPreset:
#if BENCHMARK
                Core.Diagnostics.BenchmarkPresetProbe.RequestReceived("HotkeyPrevious");
#endif
                _provider.GetRequiredService<IPresetLibrary>().StepActive(-1);
                break;

            case HotkeyAction.ShowSettingsWindow:
                RequestOpenSettings();
                break;

            case HotkeyAction.ExitApplication:
                App.RequestShutdown();
                break;
        }
    }

    /// <summary>
    /// Đảo lựa chọn bật/tắt overlay của người dùng.
    /// </summary>
    /// <remarks>
    /// Đảo CÀI ĐẶT chứ không đảo trạng thái hiện/ẩn của cửa sổ overlay. Ở chế độ "chỉ hiện trong
    /// game", overlay đang ẩn trên desktop dù vẫn bật; đảo trạng thái cửa sổ sẽ làm overlay hiện
    /// ra giữa desktop, trái với chính tuỳ chọn đó. Việc hiện hay ẩn thật do OnVisibilitySettingChanged
    /// quyết định.
    /// </remarks>
    private void ToggleOverlay()
    {
        if (_provider is null) return;

        var settings = _provider.GetRequiredService<IAppSettingsService>();
        settings.Current.OverlayEnabled = !settings.Current.OverlayEnabled;
        settings.RequestSave();
    }

    private void OnVisibilitySettingChanged(object? sender, global::System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_provider is null) return;

        if (e.PropertyName == nameof(AppSettings.UiTargetFps))
        {
            UiAnimationFrameRate.TargetFps =
                _provider.GetRequiredService<IAppSettingsService>().Current.UiTargetFps;
            return;
        }

        if (e.PropertyName == nameof(AppSettings.StreamModeEnabled))
        {
            ApplyStreamCapturePolicy();
            _provider.GetRequiredService<IProfileAutoSwitcher>().SetStreamSuppressed(
                _provider.GetRequiredService<ObsWebSocketService>().IsCapturing
                && _provider.GetRequiredService<IAppSettingsService>().Current.StreamModeEnabled);
            return;
        }

        if (e.PropertyName is not (nameof(AppSettings.OverlayEnabled)
            or nameof(AppSettings.ShowOnlyInMatchedGames)
            or nameof(AppSettings.AutoSwitchByGameProfile)))
        {
            return;
        }

        _provider.GetRequiredService<IProfileAutoSwitcher>().ApplyVisibility();
        RefreshTrayState();
    }

    private void OnObsCaptureChanged(object? sender, EventArgs e)
    {
        if (_provider is null) return;
        _provider.GetRequiredService<IProfileAutoSwitcher>().SetStreamSuppressed(
            _provider.GetRequiredService<ObsWebSocketService>().IsCapturing
            && _provider.GetRequiredService<IAppSettingsService>().Current.StreamModeEnabled);
    }

    private void ApplyStreamCapturePolicy()
    {
        if (_provider is null) return;
        var enabled = _provider.GetRequiredService<IAppSettingsService>().Current.StreamModeEnabled;
        if (enabled && !_provider.GetRequiredService<IOverlayController>().SetCaptureExclusion(true))
            _log?.LogWarning("Windows capture exclusion is unavailable for this overlay/window configuration.");
        if (!enabled) _provider.GetRequiredService<IOverlayController>().SetCaptureExclusion(false);
    }

    /// <summary>
    /// Ghi thứ tự preset vào settings.json. RequestSave ghi trên luồng nền, gom nhiều lần đổi liền nhau.
    /// </summary>
    private void OnPresetOrderChanged(object? sender, EventArgs e)
    {
        if (_provider is null) return;

        var library = _provider.GetRequiredService<IPresetLibrary>();
        var settings = _provider.GetRequiredService<IAppSettingsService>();

        // Gán danh sách MỚI — xem ghi chú ở AppSettings.PresetOrder.
        settings.Current.PresetOrder = [.. library.Presets.Select(p => p.Id)];
        settings.RequestSave();
    }

    private void OnActivePresetChanged(object? sender, EventArgs e)
    {
        if (_provider is null) return;

        var library = _provider.GetRequiredService<IPresetLibrary>();
        if (library.Active is not { } active) return;

#if BENCHMARK
        using var benchmarkScope = Core.Diagnostics.BenchmarkPresetProbe.Subscriber("T4a.Bootstrapper");
#endif
        _provider.GetRequiredService<IOverlayController>().SetProfile(active);
        (_rgbSyncService ??= _provider.GetRequiredService<RgbSyncService>()).SetColor(active.Color);
        _provider.GetRequiredService<AudioFeedbackService>().Play();

        var settings = _provider.GetRequiredService<IAppSettingsService>();
        settings.Current.ActivePresetId = active.Id;
        settings.RequestSave();

        RefreshTrayState();
        if (_settingsReady && settings.Current.NotifyOnPresetChanged)
            _provider.GetRequiredService<ITrayIconController>().ShowNotification(
                Core.AppInfo.DisplayName, Tr.Format("Tray_PresetChanged", active.Name),
                category: NotificationCategory.PresetChanged);
    }

    /// <summary>
    /// Không có cách nào vẽ đè lên Exclusive Fullscreen từ usermode, và cũng không được phép
    /// tìm cách bypass. Việc duy nhất đúng đắn là nói cho người dùng biết.
    /// </summary>
    private void OnExclusiveFullscreenDetected(object? sender, ForegroundWindowInfo window)
    {
        if (_provider is null || _shutdownStarted) return;
        var monitor = _provider.GetRequiredService<IMonitorService>().GetMonitorFromWindow(window.Handle);
        if (monitor is null) return;
        new FullscreenHintWindow(monitor.Value, Tr.Get("Fullscreen_BorderlessHint"))
            .ShowForFourSeconds();
    }

    private void RefreshTrayState()
    {
        if (_provider is null) return;

        _provider.GetRequiredService<ITrayIconController>().UpdateState(
            overlayEnabled: _provider.GetRequiredService<IAppSettingsService>().Current.OverlayEnabled,
            overlayVisible: _provider.GetRequiredService<IOverlayController>().IsVisible,
            activePresetName: _provider.GetRequiredService<IPresetLibrary>().Active?.Name ?? "—");
    }

    // ------------------------------------------------------------------ xử lý lỗi

    /// <remarks>
    /// Cài TRƯỚC mọi khởi tạo khác. Ở Phase 5 một exception trong continuation async đã giết
    /// tiến trình mà không để lại dấu vết nào ngoài Event Log của Windows — không thể để chế độ
    /// hỏng-im-lặng đó tồn tại.
    /// </remarks>
    private void InstallGlobalExceptionHandlers()
    {
        _app.DispatcherUnhandledException += (_, args) =>
        {
            _log?.LogError(args.Exception, "Lỗi không bắt được trên luồng giao diện.");
            ReportFatal(args.Exception, Tr.Get("Error_UiThread"));
            args.Handled = true;
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            _log?.LogError(args.Exception, "Lỗi không quan sát được trong tác vụ nền.");
            ReportFatal(args.Exception, Tr.Get("Error_Background"));
            args.SetObserved();
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is not Exception ex) return;

            _log?.LogCritical(ex, "Lỗi nghiêm trọng không bắt được.");
            ReportFatal(ex, Tr.Get("Error_Fatal"));
        };
    }

    private void ReportFatal(Exception exception, string context)
    {
        // Chỉ báo một lần: lỗi lặp lại trong vòng vẽ sẽ đẻ ra vô số hộp thoại.
        if (_fatalReported) return;
        _fatalReported = true;

        var logHint = _logging?.LogDirectory is { } dir
            ? "\n\n" + Tr.Format("Error_LogHint", dir)
            : string.Empty;

        // Dùng hộp thoại của app, không dùng MessageBox của Windows. Ở đây có thể chưa có cửa
        // sổ nào — hộp thoại tự phủ màn hình chính khi không có cửa sổ chủ.
        Views.Dialogs.AppDialogWindow.Show(
            _app.MainWindow,
            context,
            $"{exception.Message}{logHint}",
            Views.Dialogs.DialogKind.Error);
    }

    // ------------------------------------------------------------------ dọn dẹp

    /// <remarks>
    /// Gỡ hook (foreground, con trỏ), hotkey và Raw Input nằm trong <c>_provider.Dispose()</c>. Bước đó
    /// PHẢI chạy kể cả khi ghi file lúc thoát thất bại: trước đây hai lệnh ghi và lệnh Dispose chung một
    /// khối try, nên ổ đầy hay file bị khoá là nhảy thẳng xuống catch và bỏ qua toàn bộ việc gỡ hook.
    /// Mỗi bước giờ có lỗi riêng, không bước nào chặn được bước sau.
    /// </remarks>
    public void Shutdown(bool closeApplication = true)
    {
        if (!TryBeginShutdown(out var provider)) return;
        if (provider is not null)
        {
            // Fallback for a direct Application.Shutdown() that bypassed RequestShutdown.
            TryDuringExit("ghi preset", () => provider.GetRequiredService<IPresetLibrary>().FlushAsync().GetAwaiter().GetResult());
            TryDuringExit("ghi cài đặt", () => provider.GetRequiredService<IAppSettingsService>().FlushAsync().GetAwaiter().GetResult());
            TryDuringExit("ghi thống kê", () => provider.GetRequiredService<StatsService>().SaveNowAsync().GetAwaiter().GetResult());
            TryDuringExit("ghi nhật ký API cửa sổ", () => provider.GetRequiredService<AuditLoggerService>().Flush());
        }
        FinishShutdown(provider, closeApplication);
    }

    /// <summary>Normal tray/hotkey exit: disk flushes do not block the WPF dispatcher.</summary>
    public async Task ShutdownAsync(bool closeApplication = true)
    {
        if (!TryBeginShutdown(out var provider)) return;
        if (provider is not null)
        {
            await TryDuringExitAsync("ghi preset", () => provider.GetRequiredService<IPresetLibrary>().FlushAsync());
            await TryDuringExitAsync("ghi cài đặt", () => provider.GetRequiredService<IAppSettingsService>().FlushAsync());
            await TryDuringExitAsync("ghi thống kê", () => provider.GetRequiredService<StatsService>().SaveNowAsync());
            await TryDuringExitAsync("ghi nhật ký API cửa sổ", () => Task.Run(
                () => provider.GetRequiredService<AuditLoggerService>().Flush()));
        }
        FinishShutdown(provider, closeApplication);
    }

    private bool TryBeginShutdown(out ServiceProvider? provider)
    {
        provider = null;
        if (_shutdownStarted) return false;
        _shutdownStarted = true;
        provider = _provider;
        var services = provider;

        // Cancel image decode/blur before awaiting disk flushes. An unshown Settings
        // window can already own a ViewModel while its backdrop is being prepared.
        TryDuringExit("hủy preload Settings", () =>
        {
            foreach (var window in _app.Windows.OfType<SettingsWindow>().ToArray())
            {
                (window.DataContext as IDisposable)?.Dispose();
                window.Close();
            }
            _splash?.CloseQuietly();
            _splash = null;
        });

        if (services is not null)
        {
            // Stop producers before flushing. No new callbacks may enqueue writes after this point.
            if (_backgroundServicesStarted)
            {
                TryDuringExit("ngắt OBS", () =>
                {
                    var obs = services.GetRequiredService<ObsWebSocketService>();
                    obs.CaptureChanged -= OnObsCaptureChanged;
                    obs.Dispose();
                });
                TryDuringExit("gỡ hook vũ khí", () => services.GetRequiredService<WeaponAutoSwitchService>().Dispose());
            }
            if (_foregroundServicesStarted)
                TryDuringExit("dừng theo dõi game", () => services.GetRequiredService<IProfileAutoSwitcher>().Stop());
            if (_behaviorStarted)
                TryDuringExit("dừng timer hành vi", () => services.GetRequiredService<Services.Overlay.OverlayBehaviorController>().Dispose());
            if (_overlayStarted)
                TryDuringExit("dừng timer overlay", () => services.GetRequiredService<IOverlayController>().SetVisible(false));
            if (_backgroundServicesStarted)
                TryDuringExit("gỡ hotkey và Raw Input", () => services.GetRequiredService<IHotkeyService>().Dispose());

        }
        return true;
    }

    private void FinishShutdown(ServiceProvider? provider, bool closeApplication)
    {
        // Close while DI services are still alive. Closed disposes the Settings ViewModel
        // and its tab-scoped performance monitor before the container is torn down.
        TryDuringExit("đóng cửa sổ Settings", () =>
        {
            foreach (var window in _app.Windows.OfType<SettingsWindow>().ToArray())
                window.Close();
        });

        if (provider is not null)
        {
            if (_rgbSyncService is not null)
                TryDuringExit("đóng RGB", _rgbSyncService.Disconnect);

            TryDuringExit("gỡ đăng ký sự kiện", () =>
            {
                provider.GetRequiredService<IAppSettingsService>().Current.PropertyChanged -= OnVisibilitySettingChanged;
                provider.GetRequiredService<IPresetLibrary>().ActiveChanged -= OnActivePresetChanged;
                provider.GetRequiredService<IPresetLibrary>().OrderChanged -= OnPresetOrderChanged;
                provider.GetRequiredService<IProfileAutoSwitcher>().ExclusiveFullscreenDetected -= OnExclusiveFullscreenDetected;
                if (_backgroundServicesStarted)
                    provider.GetRequiredService<IHotkeyService>().HotkeyPressed -= OnHotkeyPressed;
            });

            _log?.LogInformation("CrosshairOverlay đang thoát.");
            TryDuringExit("giải phóng dịch vụ", provider.Dispose);
            _provider = null;
        }

        _splash?.CloseQuietly();
        _splash = null;
        _logging?.Dispose();
        _logging = null;
        if (closeApplication) _app.Shutdown();
    }

    private async Task TryDuringExitAsync(string step, Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex) { _log?.LogError(ex, "Lỗi khi thoát ở bước {Step}.", step); }
    }

    private void TryDuringExit(string step, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            _log?.LogError(ex, "Lỗi khi thoát ở bước {Step}.", step);
        }
    }
}
