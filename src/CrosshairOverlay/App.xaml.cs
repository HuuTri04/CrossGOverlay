using System.Runtime;
using System.Windows;
using CrosshairOverlay.Composition;
using CrosshairOverlay.Core;
using CrosshairOverlay.Core.Abstractions;
using CrosshairOverlay.Core.Models;
using CrosshairOverlay.Localization;
using CrosshairOverlay.Services.Input;
using CrosshairOverlay.Services.Logging;
using CrosshairOverlay.Services.Storage;
using CrosshairOverlay.Services.Updates;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CrosshairOverlay;

/// <summary>
/// Composition root: chỉ lo TRÌNH TỰ khởi động. Việc cái gì phụ thuộc cái gì nằm ở
/// <see cref="ServiceRegistration"/>.
/// </summary>
public partial class App : Application
{
    private AppLogging? _logging;
    private ServiceProvider? _provider;
    private ILogger<App>? _log;
    private bool _fatalReported;
    private StartupArguments _arguments = StartupArguments.None;

    /// <summary>
    /// Bật lên ngay khi quy trình thoát bắt đầu.
    /// </summary>
    /// <remarks>
    /// Cửa sổ Settings chặn sự kiện đóng để thu nhỏ xuống khay hoặc để hỏi xác nhận. Không có
    /// cờ này thì lệnh Thoát từ menu khay sẽ đi đóng cửa sổ Settings, gặp đúng đoạn chặn đó, và
    /// ứng dụng không bao giờ thoát được — hoặc tệ hơn, hỏi lại "bạn có chắc muốn thoát?" ngay
    /// sau khi người dùng vừa bấm Thoát.
    /// </remarks>
    public static bool IsShuttingDown { get; private set; }

    /// <summary>
    /// Đường thoát DUY NHẤT của ứng dụng. Mọi nơi muốn đóng app đều phải gọi hàm này thay vì
    /// <see cref="Application.Shutdown()"/>, để cờ <see cref="IsShuttingDown"/> luôn đúng.
    /// </summary>
    public static void RequestShutdown()
    {
        if (IsShuttingDown) return;

        IsShuttingDown = true;
        Current?.Shutdown();
    }

    /// <summary>
    /// Từ lúc Windows tạo tiến trình tới lúc dòng mã đầu tiên của ứng dụng chạy.
    /// </summary>
    /// <remarks>
    /// Khoảng này KHÔNG thuộc quyền ứng dụng: nạp .NET runtime, nạp các assembly của WPF, dựng
    /// đối tượng Application và đọc App.xaml. Đo được mới biết phần còn lại đáng tối ưu tới đâu.
    /// </remarks>
    private static readonly double RuntimeStartupMs = MeasureRuntimeStartup();

    private static double MeasureRuntimeStartup()
    {
        try
        {
            using var process = global::System.Diagnostics.Process.GetCurrentProcess();
            return (DateTime.Now - process.StartTime).TotalMilliseconds;
        }
        catch (Exception)
        {
            return -1;
        }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _ = RuntimeStartupMs;
        _arguments = StartupArguments.Parse(e.Args);

        // Một đợt thu gom gen2 gây khựng sẽ thành micro-stutter nhìn thấy được trong game.
        // SustainedLowLatency yêu cầu GC tránh những đợt đó, đổi lại heap có thể lớn hơn đôi
        // chút — đánh đổi đúng cho tiến trình chỉ cấp phát khi người dùng đổi preset.
        GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;

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
            paths.EnsureCreated();

            _logging = AppLogging.Create(paths);
            boot.Mark("log");

            _provider = ServiceRegistration.Build(paths, _logging.Factory);
            _log = _provider.GetRequiredService<ILogger<App>>();
            boot.Mark("DI");

            _log.LogInformation(
                "CrosshairOverlay {Version} khởi động. Dữ liệu: {Root}",
                typeof(App).Assembly.GetName().Version, paths.RootDirectory);

            // Khởi động lại (vd khôi phục cài đặt gốc): chờ instance cũ thoát hẳn TRƯỚC khi giành
            // quyền chạy duy nhất — nếu không, instance này sẽ tưởng đã có app chạy và tự thoát.
            if (_arguments.WaitForProcessId is { } previous) WaitForPreviousInstance(previous);

            if (!AcquireSingleInstance()) return;

            if (_arguments.FactoryReset) PerformFactoryReset(paths);

            var settings = _provider.GetRequiredService<IAppSettingsService>();
            await settings.LoadAsync().ConfigureAwait(true);
            _logging.SetLevel(settings.Current.LogLevel);
            boot.Mark("cài đặt");

            // Áp ngôn ngữ TRƯỚC khi dựng bất kỳ cửa sổ hay menu khay nào.
            LanguageCatalog.Apply(settings.Current.Language);

            // Chế độ render cũng phải có trước cửa sổ đầu tiên.
            Services.Overlay.OverlayBehaviorController.ApplyRenderMode(settings.Current.UseHardwareAcceleration);

            await StartOverlayAsync(settings, boot).ConfigureAwait(true);

            StartTray();
            boot.Mark("khay");

            WarnIfStorageFallback(paths);
            StartHotkeys(settings);
            var autoSwitcher = _provider.GetRequiredService<IProfileAutoSwitcher>();
            autoSwitcher.ExclusiveFullscreenDetected += OnExclusiveFullscreenDetected;
            autoSwitcher.Start();
            boot.Mark("phím tắt + theo dõi game");

            // Áp quy tắc hiện/ẩn ngay từ đầu, kể cả khi cửa sổ foreground lúc khởi động là của chính
            // ứng dụng (Settings) — trường hợp bộ tự đổi không xử lý.
            autoSwitcher.ApplyVisibility();

            // Sau hotkey (dùng chung Raw Input) và bộ tự đổi (biết phiên test tâm ngắm).
            _provider.GetRequiredService<Services.Overlay.OverlayBehaviorController>().Start();

            // Bật/tắt overlay (menu khay, phím tắt, Settings) và hai tuỳ chọn của game profile đều
            // chỉ ghi vào cài đặt; phản ứng tập trung tại đây để không nơi nào tự bật overlay thẳng.
            settings.Current.PropertyChanged += OnVisibilitySettingChanged;

            if (settings.Current.StartMinimizedToTray)
            {
                _provider.GetRequiredService<ITrayIconController>()
                    .ShowNotification(Core.AppInfo.DisplayName, Tr.Get("Tray_RunningInTray"));
            }
            else
            {
                _provider.GetRequiredService<IDialogService>().ShowSettingsWindow();
            }

            boot.Mark(settings.Current.StartMinimizedToTray ? "thông báo khay" : "cửa sổ Settings");

            RefreshTrayState();
            _log.LogInformation(
                "Khởi động hoàn tất: {Runtime:0} ms nạp runtime + {Total:0} ms trong app — nặng nhất {Summary}",
                RuntimeStartupMs, boot.TotalMs, boot.Summary());

            ScheduleImageCleanup();

            ScheduleUpdateCheck(settings);
        }
        catch (Exception ex)
        {
            _log?.LogCritical(ex, "Khởi động thất bại.");
            ReportFatal(ex, Tr.Get("Error_StartupFailed"));
            IsShuttingDown = true;
            Shutdown(1);
        }
    }

    // ------------------------------------------------------------------ các bước khởi động

    /// <summary>
    /// Thư mục dữ liệu tuỳ chỉnh không truy cập được (ổ đã rút, ổ mạng mất kết nối): nói rõ cho người dùng,
    /// thay vì để họ tưởng toàn bộ preset đã mất. Con trỏ giữ nguyên để lần sau cắm lại là dùng lại được.
    /// </summary>
    private void WarnIfStorageFallback(AppPathProvider paths)
    {
        if (paths.StorageFallbackFrom is not { } missing) return;

        _log!.LogWarning("Không truy cập được thư mục dữ liệu {Missing}, tạm dùng {Root}.", missing, paths.RootDirectory);
        _provider!.GetRequiredService<ITrayIconController>()
            .ShowNotification(Core.AppInfo.DisplayName, Tr.Format("Storage_FallbackNotice", missing), isWarning: true);
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
            RequestShutdown();
            return false;
        }

        guard.SecondInstanceLaunched += (_, _) =>
            _provider!.GetRequiredService<IDialogService>().ShowSettingsWindow();

        return true;
    }

    private async Task StartOverlayAsync(IAppSettingsService settings, Core.Diagnostics.BootTimeline boot)
    {
        // Bắt vào biến cục bộ: sau mỗi lời gọi phương thức, phân tích nullable phải đặt lại
        // trạng thái của field, nên dùng _provider! lặp lại sẽ sinh cảnh báo ở mọi dòng sau.
        var provider = _provider!;
        var overlay = provider.GetRequiredService<IOverlayController>();
        var library = provider.GetRequiredService<IPresetLibrary>();

        overlay.Initialize();
        boot.Mark("overlay");

        // Menu khay phải theo trạng thái THẬT của overlay: ngoài bật/tắt thủ công, game profile
        // cũng có thể ẩn/hiện overlay (HideOverlay, "chỉ hiện trong game đã khớp").
        overlay.VisibilityChanged += (_, _) => RefreshTrayState();

        overlay.SetMonitorSelection(
            settings.Current.MonitorSelectionMode,
            settings.Current.TargetMonitorDeviceName);

        // Nối thư viện với overlay ở đây chứ không trong ViewModel: preset đổi được cả khi
        // cửa sổ Settings đang đóng (hotkey, tray, auto-switch theo game).
        library.ActiveChanged += OnActivePresetChanged;

        await library.InitializeAsync(settings.Current.ActivePresetId).ConfigureAwait(true);
        boot.Mark("preset");
        // KHÔNG hiện overlay ở đây: quy tắc hiện/ẩn (ApplyVisibility) chạy ngay sau khi bộ tự đổi theo
        // game khởi động. Hiện trước sẽ làm crosshair nháy lên trên desktop một khoảnh khắc ở chế độ
        // "chỉ hiện trong game" rồi mới bị ẩn.
    }

    private void StartTray()
    {
        var tray = _provider!.GetRequiredService<ITrayIconController>();
        tray.Initialize();

        tray.OpenSettingsRequested += (_, _) =>
            _provider!.GetRequiredService<IDialogService>().ShowSettingsWindow();
        tray.ToggleOverlayRequested += (_, _) => ToggleOverlay();
        tray.ExitRequested += (_, _) => RequestShutdown();
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
                var update = await updates.CheckAsync().ConfigureAwait(false);
                if (update is null) return;

                await Dispatcher.InvokeAsync(() => OfferUpdateAsync(updates, update))
                    .Task.Unwrap().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _log?.LogDebug(ex, "Kiểm tra cập nhật nền thất bại.");
            }
        });
    }

    private async Task OfferUpdateAsync(IUpdateService updates, UpdateInfo update)
    {
        if (_provider is null) return;

        var dialogs = _provider.GetRequiredService<IDialogService>();
        if (await UpdatePrompt.OfferAsync(updates, dialogs, update)) RequestShutdown();
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
                _provider.GetRequiredService<IPresetLibrary>().StepActive(+1);
                break;

            case HotkeyAction.PreviousPreset:
                _provider.GetRequiredService<IPresetLibrary>().StepActive(-1);
                break;

            case HotkeyAction.ShowSettingsWindow:
                _provider.GetRequiredService<IDialogService>().ShowSettingsWindow();
                break;

            case HotkeyAction.ExitApplication:
                RequestShutdown();
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

        if (e.PropertyName is not (nameof(AppSettings.OverlayEnabled)
            or nameof(AppSettings.ShowOnlyInMatchedGames)
            or nameof(AppSettings.AutoSwitchByGameProfile)))
        {
            return;
        }

        _provider.GetRequiredService<IProfileAutoSwitcher>().ApplyVisibility();
        RefreshTrayState();
    }

    private void OnActivePresetChanged(object? sender, EventArgs e)
    {
        if (_provider is null) return;

        var library = _provider.GetRequiredService<IPresetLibrary>();
        if (library.Active is not { } active) return;

        _provider.GetRequiredService<IOverlayController>().SetProfile(active);

        var settings = _provider.GetRequiredService<IAppSettingsService>();
        settings.Current.ActivePresetId = active.Id;
        settings.RequestSave();

        RefreshTrayState();
    }

    /// <summary>
    /// Không có cách nào vẽ đè lên Exclusive Fullscreen từ usermode, và cũng không được phép
    /// tìm cách bypass. Việc duy nhất đúng đắn là nói cho người dùng biết.
    /// </summary>
    private void OnExclusiveFullscreenDetected(object? sender, ForegroundWindowInfo window) =>
        _provider?.GetRequiredService<ITrayIconController>().ShowNotification(
            Tr.Get("Fullscreen_WarnTitle"),
            Tr.Format("Fullscreen_WarnBody", window.ProcessName),
            isWarning: true);

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
        DispatcherUnhandledException += (_, args) =>
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
            MainWindow,
            context,
            $"{exception.Message}{logHint}",
            Views.Dialogs.DialogKind.Error);
    }

    // ------------------------------------------------------------------ dọn dẹp

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            if (_provider is not null)
            {
                // Ghi nốt phần còn treo trong hàng đợi debounce TRƯỚC khi dispose.
                _provider.GetRequiredService<IPresetLibrary>().FlushAsync().GetAwaiter().GetResult();
                _provider.GetRequiredService<IAppSettingsService>().FlushAsync().GetAwaiter().GetResult();

                _provider.GetRequiredService<IPresetLibrary>().ActiveChanged -= OnActivePresetChanged;
                _provider.GetRequiredService<IHotkeyService>().HotkeyPressed -= OnHotkeyPressed;

                _log?.LogInformation("CrosshairOverlay đang thoát.");
                _provider.Dispose();
            }
        }
        catch (Exception ex)
        {
            _log?.LogError(ex, "Lỗi khi dọn dẹp lúc thoát.");
        }
        finally
        {
            _logging?.Dispose();
        }

        base.OnExit(e);
    }
}
