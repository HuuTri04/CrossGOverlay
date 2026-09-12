using System.Runtime;
using System.Windows;
using CrosshairOverlay.Composition;
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

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Một đợt thu gom gen2 gây khựng sẽ thành micro-stutter nhìn thấy được trong game.
        // SustainedLowLatency yêu cầu GC tránh những đợt đó, đổi lại heap có thể lớn hơn đôi
        // chút — đánh đổi đúng cho tiến trình chỉ cấp phát khi người dùng đổi preset.
        GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;

        InstallGlobalExceptionHandlers();
        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        try
        {
            var paths = new AppPathProvider();
            paths.EnsureCreated();

            _logging = AppLogging.Create(paths);
            _provider = ServiceRegistration.Build(paths, _logging.Factory);
            _log = _provider.GetRequiredService<ILogger<App>>();

            _log.LogInformation(
                "CrosshairOverlay {Version} khởi động. Dữ liệu: {Root}",
                typeof(App).Assembly.GetName().Version, paths.RootDirectory);

            if (!AcquireSingleInstance()) return;

            var settings = _provider.GetRequiredService<IAppSettingsService>();
            await settings.LoadAsync().ConfigureAwait(true);
            _logging.SetLevel(settings.Current.LogLevel);

            // Áp ngôn ngữ TRƯỚC khi dựng bất kỳ cửa sổ hay menu khay nào.
            LanguageCatalog.Apply(settings.Current.Language);

            await StartOverlayAsync(settings).ConfigureAwait(true);

            StartTray();
            StartHotkeys(settings);
            var autoSwitcher = _provider.GetRequiredService<IProfileAutoSwitcher>();
            autoSwitcher.ExclusiveFullscreenDetected += OnExclusiveFullscreenDetected;
            autoSwitcher.Start();

            if (settings.Current.StartMinimizedToTray)
            {
                _provider.GetRequiredService<ITrayIconController>()
                    .ShowNotification(Core.AppInfo.DisplayName, Tr.Get("Tray_RunningInTray"));
            }
            else
            {
                _provider.GetRequiredService<IDialogService>().ShowSettingsWindow();
            }

            RefreshTrayState();
            _log.LogInformation("Khởi động hoàn tất.");

            ScheduleUpdateCheck(settings);
        }
        catch (Exception ex)
        {
            _log?.LogCritical(ex, "Khởi động thất bại.");
            ReportFatal(ex, Tr.Get("Error_StartupFailed"));
            Shutdown(1);
        }
    }

    // ------------------------------------------------------------------ các bước khởi động

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
            Shutdown();
            return false;
        }

        guard.SecondInstanceLaunched += (_, _) =>
            _provider!.GetRequiredService<IDialogService>().ShowSettingsWindow();

        return true;
    }

    private async Task StartOverlayAsync(IAppSettingsService settings)
    {
        // Bắt vào biến cục bộ: sau mỗi lời gọi phương thức, phân tích nullable phải đặt lại
        // trạng thái của field, nên dùng _provider! lặp lại sẽ sinh cảnh báo ở mọi dòng sau.
        var provider = _provider!;
        var overlay = provider.GetRequiredService<IOverlayController>();
        var library = provider.GetRequiredService<IPresetLibrary>();

        overlay.Initialize();
        overlay.SetMonitorSelection(
            settings.Current.MonitorSelectionMode,
            settings.Current.TargetMonitorDeviceName);

        // Nối thư viện với overlay ở đây chứ không trong ViewModel: preset đổi được cả khi
        // cửa sổ Settings đang đóng (hotkey, tray, auto-switch theo game).
        library.ActiveChanged += OnActivePresetChanged;

        await library.InitializeAsync(settings.Current.ActivePresetId).ConfigureAwait(true);
        overlay.SetVisible(settings.Current.OverlayEnabled);
    }

    private void StartTray()
    {
        var tray = _provider!.GetRequiredService<ITrayIconController>();
        tray.Initialize();

        tray.OpenSettingsRequested += (_, _) =>
            _provider!.GetRequiredService<IDialogService>().ShowSettingsWindow();
        tray.ToggleOverlayRequested += (_, _) => ToggleOverlay();
        tray.ExitRequested += (_, _) => Shutdown();
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
        if (await UpdatePrompt.OfferAsync(updates, dialogs, update)) Shutdown();
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
                Shutdown();
                break;
        }
    }

    private void ToggleOverlay()
    {
        if (_provider is null) return;

        var overlay = _provider.GetRequiredService<IOverlayController>();
        var settings = _provider.GetRequiredService<IAppSettingsService>();

        overlay.Toggle();
        settings.Current.OverlayEnabled = overlay.IsVisible;
        settings.RequestSave();

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
            _provider.GetRequiredService<IOverlayController>().IsVisible,
            _provider.GetRequiredService<IPresetLibrary>().Active?.Name ?? "—");
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
