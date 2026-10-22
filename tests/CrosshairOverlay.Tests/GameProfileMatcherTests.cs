using System.Windows;
using System.Collections.ObjectModel;
using CrosshairOverlay.Core.Abstractions;
using CrosshairOverlay.Core.Models;
using CrosshairOverlay.Services.Process;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CrosshairOverlay.Tests;

/// <summary>
/// So khớp profile game là logic thuần, không đụng Win32 — phần dễ test nhất của toàn dự án,
/// và cũng là phần mà một lỗi lặng lẽ sẽ khiến preset không bao giờ tự đổi.
/// </summary>
public class GameProfileMatcherTests
{
    private readonly GameProfileMatcher _matcher = new();

    private static ForegroundWindowInfo Window(
        string processName = "cs2.exe",
        string title = "Counter-Strike 2",
        string? path = @"C:\Games\cs2\cs2.exe") =>
        new(
            Handle: 1234,
            ProcessId: 42,
            ProcessName: processName,
            ExecutablePath: path,
            WindowTitle: title,
            Bounds: new Rect(0, 0, 1920, 1080),
            Fullscreen: FullscreenKind.Borderless);

    private static GameProfile Profile(
        string pattern,
        ProcessMatchMode mode = ProcessMatchMode.ProcessName,
        bool enabled = true,
        int priority = 0,
        string name = "rule") =>
        new() { Name = name, Pattern = pattern, MatchMode = mode, Enabled = enabled, Priority = priority };

    [Fact]
    public void KhopTenTienTrinh()
    {
        var match = _matcher.Match(Window(), [Profile("cs2.exe")]);
        Assert.NotNull(match);
    }

    [Fact]
    public void TenTienTrinh_KhongPhanBietHoaThuong()
    {
        var match = _matcher.Match(Window(processName: "CS2.EXE"), [Profile("cs2.exe")]);
        Assert.NotNull(match);
    }

    [Fact]
    public void TenTienTrinh_BoQuaKhoangTrangThua()
    {
        // Người dùng gõ tay vào ô Pattern thì rất dễ dính dấu cách ở đầu/cuối.
        var match = _matcher.Match(Window(), [Profile("  cs2.exe  ")]);
        Assert.NotNull(match);
    }

    [Fact]
    public void TenTienTrinh_KhongKhopMotPhan()
    {
        // "cs2" không được khớp "cs2.exe" — khớp một phần sẽ gây dương tính giả hàng loạt.
        var match = _matcher.Match(Window(), [Profile("cs2")]);
        Assert.Null(match);
    }

    [Fact]
    public void KhopTieuDeCuaSo_ChiCanChua()
    {
        var match = _matcher.Match(
            Window(title: "Counter-Strike 2 — Deathmatch"),
            [Profile("Counter-Strike", ProcessMatchMode.WindowTitleContains)]);

        Assert.NotNull(match);
    }

    [Fact]
    public void KhopDuongDanDayDu()
    {
        var match = _matcher.Match(
            Window(),
            [Profile(@"c:\games\cs2\CS2.EXE", ProcessMatchMode.ExecutablePath)]);

        Assert.NotNull(match);
    }

    [Fact]
    public void BoQuaProfileDaTat()
    {
        var match = _matcher.Match(Window(), [Profile("cs2.exe", enabled: false)]);
        Assert.Null(match);
    }

    [Fact]
    public void BoQuaPatternRong()
    {
        // Rule vừa tạo bằng nút "Thêm trống" có Pattern rỗng — không được khớp mọi thứ.
        var match = _matcher.Match(Window(), [Profile("   ")]);
        Assert.Null(match);
    }

    [Fact]
    public void CuaSoKhongHopLe_KhongKhopGiCa()
    {
        var match = _matcher.Match(ForegroundWindowInfo.Empty, [Profile("cs2.exe")]);
        Assert.Null(match);
    }

    [Fact]
    public void NhieuRuleKhop_LayPriorityNhoNhat()
    {
        var thap = Profile("cs2.exe", priority: 10, name: "thap");
        var cao = Profile("Counter-Strike", ProcessMatchMode.WindowTitleContains, priority: 1, name: "cao");

        var match = _matcher.Match(Window(), [thap, cao]);

        Assert.NotNull(match);
        Assert.Equal("cao", match.Name);
    }

    [Fact]
    public void KhongDocDuocTenExe_VanKhopDuocTheoTieuDe()
    {
        // Game chạy quyền admin: Windows từ chối đọc tên file thực thi, nhưng tiêu đề vẫn đọc được.
        var window = Window(processName: string.Empty, path: null);

        Assert.Null(_matcher.Match(window, [Profile("cs2.exe")]));
        Assert.NotNull(_matcher.Match(
            window, [Profile("Counter-Strike", ProcessMatchMode.WindowTitleContains)]));
    }

    [Fact]
    public void DanhSachRong_TraVeNull()
    {
        Assert.Null(_matcher.Match(Window(), []));
    }
}

/// <summary>Regression for a game whose metadata appears after its first foreground event.</summary>
public sealed class GameProfileActivationTests
{
    [Fact]
    public void StreamSuppression_HidesAndRestoresMatchedGameOverlay()
    {
        var watcher = new Watcher();
        var preset = CrosshairProfile.CreateDefault();
        var overlay = new Overlay();
        var settings = new Settings();
        settings.Current.ShowOnlyInMatchedGames = true;
        settings.Current.GameProfiles.Add(new GameProfile { Pattern = "game.exe", PresetId = preset.Id });
        using var switcher = new ProfileAutoSwitcher(watcher, new GameProfileMatcher(),
            new Library(preset), overlay, settings, NullLogger<ProfileAutoSwitcher>.Instance);
        switcher.Start();
        watcher.Publish(new ForegroundWindowInfo(1234, 9999, "game.exe", @"C:\Games\game.exe", "Game",
            new Rect(0, 0, 1280, 720), FullscreenKind.None));

        Assert.True(overlay.IsVisible);
        switcher.SetStreamSuppressed(true);
        Assert.False(overlay.IsVisible);
        switcher.SetStreamSuppressed(false);
        Assert.True(overlay.IsVisible);
    }

    [Fact]
    public void MetadataArrivesLate_ShowsPresetAndRaisesOverlayWithoutAltTab()
    {
        var watcher = new Watcher();
        var preset = CrosshairProfile.CreateDefault();
        var library = new Library(preset);
        var overlay = new Overlay();
        var settings = new Settings();
        settings.Current.ShowOnlyInMatchedGames = true;
        settings.Current.GameProfiles.Add(new GameProfile
        {
            Pattern = "game.exe",
            PresetId = preset.Id,
        });
        using var switcher = new ProfileAutoSwitcher(watcher, new GameProfileMatcher(), library,
            overlay, settings, NullLogger<ProfileAutoSwitcher>.Instance);
        switcher.Start();

        var loading = new ForegroundWindowInfo(1234, 9999, string.Empty, null, string.Empty,
            new Rect(0, 0, 1280, 720), FullscreenKind.None);
        watcher.Publish(loading);
        Assert.False(overlay.IsVisible);

        var ready = loading with { ProcessName = "game.exe", ExecutablePath = @"C:\Games\game.exe" };
        Assert.True(ForegroundWindowWatcher.HasChanged(loading, ready));
        watcher.Publish(ready); // Same HWND and PID: the watcher must publish changed metadata.
        Assert.Same(preset, library.Active);
        Assert.True(overlay.IsVisible);
        Assert.Equal(1, overlay.TopmostCalls);

        watcher.Publish(ready with { WindowTitle = "Game ready" });
        Assert.Equal(1, overlay.TopmostCalls); // No redundant z-order churn for title changes.
    }

    private sealed class Watcher : IForegroundWindowWatcher
    {
        public ForegroundWindowInfo Current { get; private set; } = ForegroundWindowInfo.Empty;
        public ForegroundWindowInfo LastExternal { get; private set; } = ForegroundWindowInfo.Empty;
        public event EventHandler<ForegroundWindowChangedEventArgs>? ForegroundChanged;
        public event EventHandler<ForegroundWindowInfo>? TrackedWindowClosed { add { } remove { } }
        public void Start() { }
        public void Stop() { }
        public void Track(ForegroundWindowInfo window) { }
        public void Dispose() { }
        public void Publish(ForegroundWindowInfo window)
        {
            var previous = Current;
            Current = window;
            LastExternal = window;
            ForegroundChanged?.Invoke(this, new ForegroundWindowChangedEventArgs(previous, window));
        }
    }

    private sealed class Library : IPresetLibrary
    {
        public Library(CrosshairProfile preset) =>
            Presets = new ReadOnlyObservableCollection<CrosshairProfile>(new ObservableCollection<CrosshairProfile> { preset });
        public ReadOnlyObservableCollection<CrosshairProfile> Presets { get; }
        public CrosshairProfile? Active { get; private set; }
        public event EventHandler? ActiveChanged;
        public event EventHandler? OrderChanged { add { } remove { } }
        public void SetActive(CrosshairProfile profile) { Active = profile; ActiveChanged?.Invoke(this, EventArgs.Empty); }
        public Task InitializeAsync(Guid id, IReadOnlyList<Guid>? order = null, CancellationToken token = default) => Task.CompletedTask;
        public void StepActive(int direction) => throw new NotSupportedException();
        public Task<CrosshairProfile> CreateAsync(CancellationToken token = default) => throw new NotSupportedException();
        public Task<CrosshairProfile> AddAsync(CrosshairProfile profile, CancellationToken token = default) => throw new NotSupportedException();
        public Task<CrosshairProfile> DuplicateAsync(CrosshairProfile profile, CancellationToken token = default) => throw new NotSupportedException();
        public bool Move(CrosshairProfile profile, int index) => throw new NotSupportedException();
        public Task DeleteAsync(CrosshairProfile profile, CancellationToken token = default) => throw new NotSupportedException();
        public Task FlushAsync(CancellationToken token = default) => Task.CompletedTask;
        public void Dispose() { }
    }

    private sealed class Settings : IAppSettingsService
    {
        public AppSettings Current { get; } = AppSettings.CreateDefault();
        public Task LoadAsync(CancellationToken token = default) => Task.CompletedTask;
        public Task SaveAsync(CancellationToken token = default) => Task.CompletedTask;
        public void RequestSave() { }
        public Task FlushAsync(CancellationToken token = default) => Task.CompletedTask;
        public void Dispose() { }
    }

    private sealed class Overlay : IOverlayController, IOverlayZOrder
    {
        public bool IsVisible { get; private set; }
        public int TopmostCalls { get; private set; }
        public CrosshairProfile? CurrentProfile { get; private set; }
        public MonitorInfo? CurrentMonitor => null;
        public event EventHandler<OverlayVisibilityChangedEventArgs>? VisibilityChanged;
        public void SetVisible(bool visible)
        {
            IsVisible = visible;
            VisibilityChanged?.Invoke(this, new OverlayVisibilityChangedEventArgs(visible));
        }
        public void ReassertTopmost() => TopmostCalls++;
        public bool SetCaptureExclusion(bool enabled) => true;
        public void SetProfile(CrosshairProfile profile) => CurrentProfile = profile;
        public void Initialize() { }
        public void Toggle() => SetVisible(!IsVisible);
        public void MoveToMonitor(MonitorInfo monitor) { }
        public void SetMonitorSelection(MonitorSelectionMode mode, string? name) { }
        public void Invalidate() { }
        public void SetSuppressed(bool suppressed) { }
        public void SetFrameRateLimit(int fps) { }
        public void SetGlobalScale(double scale) { }
        public void SetColorOverride(System.Windows.Media.Color? color) { }
        public void SetPixelShift(int x, int y) { }
        public void Dispose() { }
    }
}
