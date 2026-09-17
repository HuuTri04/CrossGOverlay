using CrosshairOverlay.Core.Abstractions;
using CrosshairOverlay.Services.Display;
using CrosshairOverlay.Services.Export;
using CrosshairOverlay.Services.Input;
using CrosshairOverlay.Services.Overlay;
using CrosshairOverlay.Services.Presets;
using CrosshairOverlay.Services.Process;
using CrosshairOverlay.Services.Rendering;
using CrosshairOverlay.Services.Storage;
using CrosshairOverlay.Services.System;
using CrosshairOverlay.Services.Updates;
using CrosshairOverlay.ViewModels;
using CrosshairOverlay.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CrosshairOverlay.Composition;

/// <summary>
/// Khai báo toàn bộ đồ thị phụ thuộc của ứng dụng.
/// </summary>
/// <remarks>
/// Tách khỏi <c>App.xaml.cs</c> để file đó chỉ còn lo TRÌNH TỰ khởi động (cái gì phải sẵn sàng
/// trước cái gì), còn việc cái gì phụ thuộc cái gì thì nằm gọn ở đây.
/// </remarks>
internal static class ServiceRegistration
{
    public static ServiceProvider Build(IAppPathProvider paths, ILoggerFactory loggerFactory)
    {
        var services = new ServiceCollection();

        services.AddSingleton(loggerFactory);
        services.AddLogging();

        // ---- hạ tầng ----
        services.AddSingleton(paths);
        services.AddSingleton<IAppSettingsService, AppSettingsService>();
        services.AddSingleton<IStorageLocationService, StorageLocationService>();
        services.AddSingleton<IPresetRepository, PresetRepository>();
        services.AddSingleton<IPresetLibrary, PresetLibrary>();
        services.AddSingleton<ICustomImageStore, CustomImageStore>();

        // ---- hiển thị ----
        services.AddSingleton<IMonitorService, MonitorService>();
        services.AddSingleton<IDisplayModeReader, DisplayModeReader>();
        services.AddSingleton<ICrosshairRenderer, CrosshairRenderer>();
        services.AddSingleton<IOverlayController, OverlayController>();

        // ---- nhập liệu & tiến trình ----
        services.AddSingleton<HotkeyMessageWindow>();
        services.AddSingleton<IHotkeyService, HotkeyService>();
        services.AddSingleton<IForegroundWindowWatcher, ForegroundWindowWatcher>();
        services.AddSingleton<IGameProfileMatcher, GameProfileMatcher>();
        services.AddSingleton<IRunningApplicationScanner, RunningApplicationScanner>();
        services.AddSingleton<IProfileAutoSwitcher, ProfileAutoSwitcher>();

        // ---- hệ thống ----
        services.AddSingleton<IStartupService, StartupService>();
        services.AddSingleton<ITrayIconController, TrayIconController>();
        services.AddSingleton<ISingleInstanceGuard, SingleInstanceGuard>();
        services.AddSingleton<IUpdateService, UpdateService>();
        services.AddSingleton<IProcessLauncher, ProcessLauncher>();
        services.AddSingleton<IAppRestartService>(provider => new AppRestartService(
            provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<AppRestartService>>(),
            App.RequestShutdown));
        services.AddSingleton<IHardwareInfoService, HardwareInfoService>();
        services.AddSingleton<ICursorVisibilityMonitor, CursorVisibilityMonitor>();
        services.AddSingleton<ICrosshairTranslator, CrosshairTranslatorService>();
        services.AddSingleton<OverlayBehaviorController>();

        // Vòng phụ thuộc có thật: SettingsViewModel cần IDialogService, mà DialogService lại
        // phải dựng được cửa sổ chứa ViewModel đó. Closure giải vòng vì nó chỉ resolve
        // ViewModel tại thời điểm người dùng mở cửa sổ, khi singleton đã tồn tại.
        services.AddSingleton<IDialogService>(provider => new DialogService(() => CreateSettingsWindow(provider)));

        // Transient: mỗi lần mở cửa sổ Settings là một ViewModel mới, vì cửa sổ cũ đã bị đóng
        // và các đăng ký sự kiện của nó không còn giá trị.
        services.AddTransient<SettingsViewModel>();

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
    }

    /// <summary>
    /// Dựng cửa sổ Settings và ghi lại chi phí từng phần.
    /// </summary>
    /// <remarks>
    /// Đây là bước nặng nhất của lần khởi động đầu tiên (đo được ~1,1 giây), nên phải tách được
    /// phần ViewModel với phần dựng giao diện thì mới biết tối ưu vào đâu.
    /// </remarks>
    private static SettingsWindow CreateSettingsWindow(IServiceProvider provider)
    {
        var watch = global::System.Diagnostics.Stopwatch.StartNew();

        var viewModel = provider.GetRequiredService<SettingsViewModel>();
        var viewModelMs = watch.Elapsed.TotalMilliseconds;

        var window = new SettingsWindow(
            viewModel,
            provider.GetRequiredService<ITrayIconController>(),
            provider.GetRequiredService<ILogger<SettingsWindow>>());

        provider.GetRequiredService<ILogger<SettingsWindow>>().LogDebug(
            "Dựng cửa sổ Settings: ViewModel {ViewModel:0} ms, giao diện {Ui:0} ms.",
            viewModelMs, watch.Elapsed.TotalMilliseconds - viewModelMs);

        return window;
    }
}
