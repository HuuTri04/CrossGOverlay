using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using System.Windows.Interop;
using CrosshairOverlay.Core.Abstractions;
using CrosshairOverlay.Core.Models;
using CrosshairOverlay.Controls;
using CrosshairOverlay.Localization;
using CrosshairOverlay.Services.System;
using CrosshairOverlay.ViewModels;
using CrosshairOverlay.Views.Dialogs;
using Microsoft.Extensions.Logging;

namespace CrosshairOverlay.Views;

/// <summary>Cửa sổ Settings — editor crosshair, thư viện preset và tuỳ chọn chung.</summary>
public partial class SettingsWindow : Window
{
    private void OnApplyObsPassword(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm)
            vm.General.ApplyObsPassword();
    }

    private void OnObsPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm && sender is PasswordBox passwordBox)
            vm.General.ObsPassword = passwordBox.Password;
    }

    private void OnObsPasswordVisibilityChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm ||
            sender is not System.Windows.Controls.Primitives.ToggleButton toggle ||
            toggle.Tag is not PasswordBox passwordBox)
            return;

        if (toggle.IsChecked == true)
            vm.General.ObsPassword = passwordBox.Password;
        else if (passwordBox.Password != vm.General.ObsPassword)
            passwordBox.Password = vm.General.ObsPassword;
    }
    private readonly SettingsViewModel _viewModel;
    private readonly ITrayIconController _tray;
    private readonly Microsoft.Extensions.Logging.ILogger<SettingsWindow> _logger;
    private PerformanceMonitorService? _performanceMonitor;
    private DispatcherTimer? _statsTimer;
    private bool _backgroundWasRenderable;
    private Brush? _solidWindowBackground;
    private Brush? _normalWindowForeground;
    private bool _micaEnabled;
    private bool _highContrastBackgroundApplied;

    public PerformanceMonitorService PerformanceMonitor =>
        _performanceMonitor ??= new PerformanceMonitorService(Dispatcher);

    /// <summary>Chỉ nhắc "app vẫn đang chạy" một lần mỗi phiên, không nhắc mỗi lần ẩn.</summary>
    private bool _hintShown;

    /// <summary>Tab "Thư viện" đã được dựng và bắt đầu nạp dữ liệu hay chưa.</summary>
    private bool _libraryStarted;
    private bool _initialRevealLogged;

    public SettingsWindow(
        SettingsViewModel viewModel, ITrayIconController tray, Microsoft.Extensions.Logging.ILogger<SettingsWindow> logger)
    {
        InitializeComponent();

        // Benchmark-only A/B for the backdrop surface. Production keeps the cache.
        if (Environment.GetEnvironmentVariable("CROSSGOVERLAY_BENCHMARK_DISABLE_BACKGROUND_CACHE") == "1")
            AppBackgroundImageLayer.CacheMode = null;


        _viewModel = viewModel;
        _tray = tray;
        _logger = logger;
        DataContext = viewModel;
        _viewModel.SetPresetPresentationActive(false);
        _solidWindowBackground = Background;
        _normalWindowForeground = Foreground;
        SourceInitialized += (_, _) => UpdateMicaBackdrop();
        PropertyChangedEventManager.AddHandler(_viewModel.General, OnBackgroundAvailabilityChanged,
            nameof(GeneralSettingsViewModel.HasAppBackground));
        SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
        // Bubble phase: label/slider/button handlers run first. Only a click that reached a
        // non-interactive surface moves keyboard focus away from the previous input control.
        MouseDown += OnBackgroundMouseDown;
        AddHandler(Mouse.PreviewMouseWheelEvent,
            new MouseWheelEventHandler(OnWindowPreviewMouseWheel), handledEventsToo: true);
        AddHandler(ScrollViewer.ScrollChangedEvent,
            new ScrollChangedEventHandler(OnWindowScrollChanged), handledEventsToo: true);
        Loaded += (_, _) => UpdateVisibleTabServices();
        IsVisibleChanged += (_, _) => UpdateVisibleTabServices();
        StateChanged += (_, _) => UpdateVisibleTabServices();

        // ViewModel đăng ký sự kiện lên các service singleton (thư viện preset, màn hình,
        // theo dõi foreground). Cửa sổ này được tạo mới mỗi lần mở, nên không giải phóng thì
        // mỗi lần mở lại là một ViewModel nữa bị giữ sống mãi.
        Closed += (_, _) =>
        {
            PropertyChangedEventManager.RemoveHandler(_viewModel.General, OnBackgroundAvailabilityChanged,
                nameof(GeneralSettingsViewModel.HasAppBackground));
            SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;
            _performanceMonitor?.Dispose();
            if (_statsTimer is not null)
            {
                _statsTimer.Stop();
                _statsTimer.Tick -= OnStatsTick;
            }
            (DataContext as IDisposable)?.Dispose();
        };
        if (Core.Diagnostics.StartupProfiler.Enabled)
        {
            PreviewMouseDown += OnFirstBenchmarkMouseDown;
            _logger.LogInformation("BenchmarkTimeline SettingsInitialized {Elapsed:0.###} ms.",
                Core.Diagnostics.StartupProfiler.ElapsedMs());
        }
    }

    private void OnBackgroundAvailabilityChanged(object? sender, PropertyChangedEventArgs e) => UpdateMicaBackdrop();

    private void OnSystemParametersChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.HighContrast)) UpdateMicaBackdrop();
    }

    internal static bool CanUseMica(int build, bool highContrast, bool hasCustomBackground) =>
        build >= 22621 && !highContrast && !hasCustomBackground;

    private void UpdateMicaBackdrop()
    {
        if (!Dispatcher.CheckAccess())
        {
            if (!Dispatcher.HasShutdownStarted)
                Dispatcher.BeginInvoke(UpdateMicaBackdrop, DispatcherPriority.Normal);
            return;
        }
        if (PresentationSource.FromVisual(this) is not HwndSource source || source.CompositionTarget is null)
            return;

        var highContrast = SystemParameters.HighContrast;
        var build = Environment.OSVersion.Version.Build;
        var enable = CanUseMica(build, highContrast,
            _viewModel.General.HasAppBackground);
        if (enable == _micaEnabled && highContrast == _highContrastBackgroundApplied) return;

        var hwnd = source.Handle;
        const int systemBackdropType = 38; // DWMWA_SYSTEMBACKDROP_TYPE, Windows 11 22H2+
        const int immersiveDarkMode = 20;  // DWMWA_USE_IMMERSIVE_DARK_MODE
        const int attributeSize = sizeof(int);
        if (enable)
        {
            var mica = 2; // DWMSBT_MAINWINDOW
            if (DwmSetWindowAttribute(hwnd, systemBackdropType, ref mica, attributeSize) < 0) return;
            var dark = 1;
            _ = DwmSetWindowAttribute(hwnd, immersiveDarkMode, ref dark, attributeSize);
            var fullClient = new DwmMargins(-1, -1, -1, -1);
            if (DwmExtendFrameIntoClientArea(hwnd, ref fullClient) < 0)
            {
                var none = 1; // DWMSBT_NONE
                _ = DwmSetWindowAttribute(hwnd, systemBackdropType, ref none, attributeSize);
                return;
            }
            source.CompositionTarget.BackgroundColor = Colors.Transparent;
            if (_highContrastBackgroundApplied)
                BackgroundLayer.ClearValue(VisibilityProperty);
            Background = Brushes.Transparent;
            SettingsTabs.Background = Brushes.Transparent;
            _micaEnabled = true;
            _highContrastBackgroundApplied = false;
            _logger.LogInformation("Mica State: Active");
            return;
        }

        if (_micaEnabled)
        {
            var disabled = 1;
            _ = DwmSetWindowAttribute(hwnd, systemBackdropType, ref disabled, attributeSize);
            var noFrame = new DwmMargins(0, 0, 0, 0);
            _ = DwmExtendFrameIntoClientArea(hwnd, ref noFrame);
            SettingsTabs.ClearValue(BackgroundProperty);
        }

        if (build >= 22000)
        {
            // Do not force a dark title bar over the system High Contrast palette.
            var dark = highContrast ? 0 : 1;
            _ = DwmSetWindowAttribute(hwnd, immersiveDarkMode, ref dark, attributeSize);
        }

        // High Contrast must also cover a custom image. A local Visibility value
        // temporarily overrides the image layer's normal binding-based style.
        if (highContrast)
        {
            BackgroundLayer.Visibility = Visibility.Collapsed;
            Background = SystemColors.WindowBrush;
            Foreground = SystemColors.WindowTextBrush;
        }
        else
        {
            if (_highContrastBackgroundApplied)
                BackgroundLayer.ClearValue(VisibilityProperty);
            Background = _solidWindowBackground;
            if (_highContrastBackgroundApplied)
                Foreground = _normalWindowForeground;
        }
        if (Background is SolidColorBrush solid)
            source.CompositionTarget.BackgroundColor = solid.Color;
        _micaEnabled = false;
        _highContrastBackgroundApplied = highContrast;
        _logger.LogInformation(highContrast ? "HighContrast Fallback: Active" : "Mica State: Inactive");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DwmMargins(int left, int right, int top, int bottom)
    {
        public int Left = left;
        public int Right = right;
        public int Top = top;
        public int Bottom = bottom;
    }

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int valueSize);

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmExtendFrameIntoClientArea(nint hwnd, ref DwmMargins margins);

    private void OnFirstBenchmarkMouseDown(object sender, MouseButtonEventArgs e)
    {
        PreviewMouseDown -= OnFirstBenchmarkMouseDown;
        var receivedAt = global::System.Diagnostics.Stopwatch.GetTimestamp();
        _logger.LogInformation("BenchmarkTimeline FirstMouseInputQpc {Timestamp} at {Elapsed:0.###} ms.",
            receivedAt, Core.Diagnostics.StartupProfiler.ElapsedMs(receivedAt));
    }

    private void OnPreviewZoomLabelMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        PreviewZoomSlider.Focus();
        e.Handled = true;
    }


    private void OnBackgroundMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.Handled || e.ChangedButton != MouseButton.Left ||
            Keyboard.FocusedElement is null || ReferenceEquals(Keyboard.FocusedElement, MainRootGrid) ||
            e.OriginalSource is not DependencyObject clicked || IsInteractiveTarget(clicked))
            return;

        // Focusing a stable sink also raises LostFocus on the old TextBox/PasswordBox so
        // pending edits are committed. WPF updates IsMouseOver from the real pointer position.
        MainRootGrid.Focus();
    }

    private void OnWindowPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // Wheel events inside a dropdown's own HWND do not route through this window.
        if (e.OriginalSource is Visual source &&
            !ReferenceEquals(PresentationSource.FromVisual(source), PresentationSource.FromVisual(this))) return;
        if (e.Delta != 0 && PopupScrollGuard.CloseFor(this)) MainRootGrid.Focus();
    }

    private void OnWindowScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.VerticalChange == 0 && e.HorizontalChange == 0) return;
        if (e.OriginalSource is not ScrollViewer viewer ||
            !ReferenceEquals(PresentationSource.FromVisual(viewer), PresentationSource.FromVisual(this))) return;
        if (PopupScrollGuard.CloseFor(this)) MainRootGrid.Focus();
    }

    private bool IsInteractiveTarget(DependencyObject clicked)
    {
        for (DependencyObject? current = clicked;
             current is not null && !ReferenceEquals(current, MainRootGrid);
             current = current is Visual or Visual3D
                 ? VisualTreeHelper.GetParent(current) ?? LogicalTreeHelper.GetParent(current)
                 : LogicalTreeHelper.GetParent(current))
        {
            if (current is ButtonBase or TextBoxBase or PasswordBox or Slider or ScrollBar or
                ComboBox or ListBoxItem or MenuItem or Hyperlink ||
                current is Label { Target: not null })
                return true;
        }

        return false;
    }

    /// <summary>Dựng tab đang chọn trước khi Settings được lộ ra; tab phụ vẫn nạp theo yêu cầu.</summary>
    public void PrepareInitialContent()
    {
        var editor = (DataTemplate)FindResource("EditorPanel");
        if (!ReferenceEquals(EditorHost.ContentTemplate, editor))
            EditorHost.ContentTemplate = editor;
    }

    /// <summary>Ghi mốc lộ cửa sổ; đây không phải phép đo DWM Present.</summary>
    public void MarkSettingsRevealed()
    {
        if (_initialRevealLogged) return;
        _initialRevealLogged = true;
        LogSettingsRevealed();
        ScheduleBindingAudit();
        if (Core.Diagnostics.StartupProfiler.Enabled)
            Dispatcher.InvokeAsync(
                () => _logger.LogInformation(
                    "BenchmarkTimeline FirstInputPriorityDispatch {Elapsed:0.###} ms.",
                    Core.Diagnostics.StartupProfiler.ElapsedMs()),
                DispatcherPriority.Input);
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        if (Core.Diagnostics.StartupProfiler.Enabled)
        {
            _logger.LogInformation("BenchmarkTimeline SettingsFirstRender {Elapsed:0.###} ms.",
                Core.Diagnostics.StartupProfiler.ElapsedMs());
            _logger.LogInformation(
                "BenchmarkBackgroundPreload CacheHit={CacheHit} DiskRead_ms={DiskRead:0.###} Decode_ms={Decode:0.###} BlurProcessing_ms={Blur:0.###} DispatcherWait_ms={DispatcherWait:0.###} FirstFrameRender_ms={FirstFrame:0.###}.",
                Core.Diagnostics.StartupProfiler.BackgroundCacheHit,
                Core.Diagnostics.StartupProfiler.DiskReadMs,
                Core.Diagnostics.StartupProfiler.DecodeMs,
                Core.Diagnostics.StartupProfiler.BlurProcessingMs,
                Core.Diagnostics.StartupProfiler.DispatcherWaitMs,
                Core.Diagnostics.StartupProfiler.FirstFrameRenderMs);
        }
    }

    /// <summary>
    /// Dựng tab "Thư viện" và bắt đầu nạp dữ liệu vào ĐÚNG lần đầu người dùng mở tab đó.
    /// </summary>
    /// <remarks>
    /// Hai việc đều phải trễ: dựng cây giao diện của lưới thẻ, và đọc file <c>builtin_presets.json</c>. Người chỉ vào
    /// chỉnh tâm ngắm rồi thoát thì không trả một mili-giây nào cho thư viện. Đọc file chạy trên luồng nền, còn tab đã
    /// hiện sẵn dòng "Đang tải 500+ mẫu tâm ngắm…".
    /// </remarks>
    private async void OnTabChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        // TabControl phát sự kiện này cả khi ComboBox/ListBox bên trong đổi lựa chọn.
        if (!ReferenceEquals(e.OriginalSource, sender)) return;
        UpdatePresetPresentation();
        UpdatePerformanceMonitor();
        UpdateStatsTimer();
        if (PixelArtNavigationTab?.IsSelected == true && PixelArtHost.ContentTemplate is null)
        {
            PixelArtHost.ContentTemplate = (DataTemplate)FindResource("PixelArtPanel");
            PixelArtHost.Content = _viewModel.PixelArt;
        }
        if (_libraryStarted || !LibraryTab.IsSelected)
        {
            ScheduleBindingAudit();
            return;
        }

        _libraryStarted = true;
        try
        {
            LibraryHost.ContentTemplate = (DataTemplate)FindResource("LibraryPanel");
            LibraryHost.UpdateLayout();

            FindChild<PresetLibraryView>(LibraryHost)?.FocusSearch();

            await _viewModel.Library.LoadAsync();
            ScheduleBindingAudit();
        }
        catch (Exception ex)
        {
            _libraryStarted = false;
            _logger.LogError(ex, "Could not initialize the preset library tab.");
        }
    }

    private void ScheduleBindingAudit()
    {
        if (!Core.Diagnostics.BindingAudit.Enabled) return;
        Dispatcher.InvokeAsync(() =>
        {
            if (!IsVisible || App.IsShuttingDown) return;
            var errors = Core.Diagnostics.BindingAudit.Scan(this, _logger);
            _logger.LogInformation("BindingAudit completed: {Errors} materialized binding errors.", errors);
        }, DispatcherPriority.ApplicationIdle);
    }

    private void UpdatePerformanceMonitor()
    {
        if (IsVisible && WindowState != WindowState.Minimized && ShortcutsTab?.IsSelected == true)
            PerformanceMonitor.Start();
        else _performanceMonitor?.Stop();
    }

    private void UpdateVisibleTabServices()
    {
        var visible = IsVisible && WindowState != WindowState.Minimized;
        UpdatePresetPresentation();
        // Minimize keeps the existing frozen bitmap. Only Hide() starts the 30-second
        // cache expiry; taskbar restoration bypasses DialogService's preload path.
        _viewModel.General.SetBackgroundViewActive(Visibility == System.Windows.Visibility.Visible);
        if (visible && !_backgroundWasRenderable)
            _viewModel.General.RefreshBackgroundBinding();
        _backgroundWasRenderable = visible;
        _viewModel.Editor.SetPreviewBackgroundViewActive(visible);
        UpdatePerformanceMonitor();
        UpdateStatsTimer();
    }

    internal void PreparePresetForReveal() =>
        _viewModel.SetPresetPresentationActive(SettingsTabs.SelectedIndex == 0);

    private void UpdatePresetPresentation() =>
        _viewModel.SetPresetPresentationActive(
            IsVisible && WindowState != WindowState.Minimized && SettingsTabs.SelectedIndex == 0);

    private void UpdateStatsTimer()
    {
        if (IsVisible && WindowState != WindowState.Minimized && StatsNavigationTab?.IsSelected == true)
        {
            if (_statsTimer is null)
            {
                _statsTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
                {
                    Interval = TimeSpan.FromSeconds(3),
                };
                _statsTimer.Tick += OnStatsTick;
            }
            _viewModel.Stats.Refresh();
            _statsTimer.Start();
        }
        else _statsTimer?.Stop();
    }

    private void OnStatsTick(object? sender, EventArgs e) => _viewModel.Stats.Refresh();

    private static T? FindChild<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T match) return match;

            if (FindChild<T>(child) is { } deeper) return deeper;
        }

        return null;
    }

    /// <summary>
    /// Cuộn tới preset vừa được chọn, kể cả khi mục chưa được hiện thực hóa bởi danh sách ảo hóa.
    /// </summary>
    private void OnPresetSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (sender is not System.Windows.Controls.ListBox { SelectedItem: { } item } list) return;

#if BENCHMARK
        if (Core.Diagnostics.BenchmarkPresetProbe.SkipBringIntoView) return;
        var benchmarkSwitchId = Core.Diagnostics.BenchmarkPresetProbe.CurrentSwitchId;
        Core.Diagnostics.BenchmarkPresetProbe.Mark("T4d.BringQueued", benchmarkSwitchId);
        Dispatcher.InvokeAsync(() =>
        {
            Core.Diagnostics.BenchmarkPresetProbe.Mark("T4d.BringStart", benchmarkSwitchId);
            list.ScrollIntoView(item);
            Core.Diagnostics.BenchmarkPresetProbe.Mark("T4d.BringEnd", benchmarkSwitchId);
        }, System.Windows.Threading.DispatcherPriority.Loaded);
#else
        // ScrollIntoView hiện thực hóa mục ngoài viewport khi danh sách đang ảo hóa.
        Dispatcher.InvokeAsync(
            () => list.ScrollIntoView(item),
            System.Windows.Threading.DispatcherPriority.Loaded);
#endif
    }

    private void LogSettingsRevealed()
    {
        try
        {
            using var process = global::System.Diagnostics.Process.GetCurrentProcess();
            Microsoft.Extensions.Logging.LoggerExtensions.LogInformation(
                _logger,
                "Cửa sổ Settings được lộ ra sau {Elapsed:0} ms kể từ khi tiến trình khởi động (chưa xác nhận DWM Present).",
                (DateTime.Now - process.StartTime).TotalMilliseconds);
        }
        catch (Exception)
        {
            // Chỉ là số đo; không đọc được giờ khởi động của tiến trình thì bỏ qua.
        }
    }

    /// <summary>
    /// Quyết định nút X làm gì: thu nhỏ xuống khay, hay hỏi rồi thoát hẳn.
    /// </summary>
    /// <remarks>
    /// Cả hai nhánh đều bắt đầu bằng <c>e.Cancel = true</c>. Ứng dụng chạy ở
    /// <c>ShutdownMode="OnExplicitShutdown"</c>, nên đóng cửa sổ này không tự làm gì tới tiến
    /// trình — nhưng nếu để cửa sổ đóng thật thì ViewModel bị giải phóng và lần mở sau phải
    /// dựng lại từ đầu, chưa kể mất hết trạng thái tab đang chọn.
    ///
    /// <para>
    /// Nhánh thoát KHÔNG tự đi dọn dẹp: <see cref="App.RequestShutdown"/> dẫn tới
    /// <c>App.OnExit</c>, nơi đã ghi nốt hàng đợi lưu file, gỡ hook foreground, gỡ hotkey và
    /// giải phóng toàn bộ container DI. Nhân đôi phần dọn dẹp ở đây chỉ tạo ra một đường thoát
    /// thứ hai để sau này quên cập nhật.
    /// </para>
    /// </remarks>
    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);

        // Ứng dụng đang thoát thật (khay, hotkey, hoặc chính lựa chọn "Có" bên dưới) — lần này
        // cửa sổ phải đóng được, nếu không sẽ kẹt lại mãi.
        if (App.IsShuttingDown) return;

        e.Cancel = true;

        if (_viewModel.General.Settings.MinimizeToTrayOnClose)
        {
            HideToTray();
            return;
        }

        if (AppDialogWindow.Confirm(
                this,
                Tr.Get("Exit_ConfirmTitle"),
                Tr.Get("Exit_ConfirmBody"),
                kind: DialogKind.Warning))
        {
            App.RequestShutdown();
        }
    }

    private void HideToTray()
    {
        Hide();

        if (_hintShown) return;
        _hintShown = true;

        // Isolate the cost of the one-shot toast in app-only GPU profiling. Normal
        // launches always show it; this flag is set only by the benchmark child.
        if (Environment.GetEnvironmentVariable("CROSSGOVERLAY_BENCHMARK_NO_TRAY_TOAST") == "1") return;

        // Cửa sổ biến mất mà overlay vẫn vẽ dễ khiến người dùng tưởng app đã thoát và đi tìm
        // cách tắt nó trong Task Manager.
        _tray.ShowNotification(Core.AppInfo.DisplayName, Tr.Get("Tray_HiddenToTray"),
            category: NotificationCategory.MinimizeToTray);
    }
}
