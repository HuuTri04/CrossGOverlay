using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Windows;
using CrosshairOverlay.Core.Abstractions;
using CrosshairOverlay.Core.Models;
using CrosshairOverlay.Localization;
using CrosshairOverlay.Services.Export;
using CrosshairOverlay.Services.Process;
using CrosshairOverlay.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CrosshairOverlay.Tests;

/// <summary>
/// Nút "Test tâm ngắm": bật overlay, mở Notepad; không có Notepad thì cho chọn app bất kỳ và tạo
/// preset + game profile cho app đó. Và phiên test trong ProfileAutoSwitcher.
/// </summary>
[Collection("Localization")]
public sealed class TestCrosshairTests : IDisposable
{
    private readonly CultureInfo _previousCulture = TranslationSource.Instance.CurrentCulture;

    public TestCrosshairTests() => TranslationSource.Instance.CurrentCulture = new CultureInfo("vi");

    public void Dispose() => TranslationSource.Instance.CurrentCulture = _previousCulture;

    // ================================================================== phiên test trong auto-switcher

    private sealed class SwitcherRig
    {
        public readonly FakeWatcher Watcher = new();
        public readonly FakeOverlay Overlay = new();
        public readonly FakeSettings Settings = new();
        public readonly FakeLibrary Library = new();
        public readonly ProfileAutoSwitcher Switcher;
        public readonly CrosshairProfile Editing = new() { Name = "Đang chỉnh" };
        public readonly CrosshairProfile NotepadPreset = new() { Name = "Preset của Notepad" };

        public SwitcherRig()
        {
            Library.Add(Editing);
            Library.Add(NotepadPreset);
            Library.SetActive(Editing);

            Settings.Current.OverlayEnabled = true;
            Settings.Current.AutoSwitchByGameProfile = true;
            Settings.Current.ShowOnlyInMatchedGames = true;

            // Máy người dùng có sẵn profile cho Notepad trỏ tới preset KHÁC — test không được hiện nhầm nó.
            Settings.Current.GameProfiles.Add(new GameProfile
            {
                Name = "MOCK — Notepad", Pattern = "notepad.exe", PresetId = NotepadPreset.Id,
            });

            Switcher = new ProfileAutoSwitcher(Watcher, new GameProfileMatcher(), Library, Overlay, Settings,
                NullLogger<ProfileAutoSwitcher>.Instance);
            Switcher.Start();
        }
    }

    private static ForegroundWindowInfo Window(nint handle, int pid, string process) =>
        new(handle, pid, process, @"C:\Apps\" + process, process, new Rect(0, 0, 800, 600), FullscreenKind.None);

    [Fact]
    public void PhienTest_NotepadForeground_HienDungPresetDangChinh_DuCheDoChiTrongGame()
    {
        var rig = new SwitcherRig();
        rig.Switcher.BeginCrosshairTest("notepad.exe");

        rig.Watcher.MoveTo(Window(0x10, 4242, "Notepad.exe"));

        Assert.True(rig.Overlay.IsVisible);
        Assert.Same(rig.Editing, rig.Library.Active);   // KHÔNG đổi sang preset của profile Notepad
        Assert.Null(rig.Switcher.ActiveGameProfile);
        Assert.Equal(4242, rig.Watcher.Tracked.ProcessId);
    }

    [Fact]
    public void KhongCoPhienTest_NotepadVanTheoProfileBinhThuong()
    {
        var rig = new SwitcherRig();

        rig.Watcher.MoveTo(Window(0x10, 4242, "notepad.exe"));

        Assert.Same(rig.NotepadPreset, rig.Library.Active);
    }

    [Fact]
    public void DongNotepad_KetThucPhienTest_AnTheoCheDoChiTrongGame()
    {
        var rig = new SwitcherRig();
        rig.Switcher.BeginCrosshairTest("notepad.exe");
        rig.Watcher.MoveTo(Window(0x10, 4242, "notepad.exe"));

        rig.Watcher.CloseTracked();

        Assert.False(rig.Overlay.IsVisible);

        // Mở Notepad lần sau (không bấm Test) thì về lại quy tắc thường.
        rig.Watcher.MoveTo(Window(0x11, 5000, "notepad.exe"));
        Assert.Same(rig.NotepadPreset, rig.Library.Active);
    }

    [Fact]
    public void TrongPhienTest_ChuyenSangAppKhac_ApQuyTacThuong()
    {
        var rig = new SwitcherRig();
        rig.Switcher.BeginCrosshairTest("notepad.exe");
        rig.Watcher.MoveTo(Window(0x10, 4242, "notepad.exe"));

        rig.Watcher.MoveTo(Window(0x20, 777, "chrome.exe"));
        Assert.False(rig.Overlay.IsVisible);

        rig.Watcher.MoveTo(Window(0x10, 4242, "notepad.exe"));
        Assert.True(rig.Overlay.IsVisible);
    }

    [Fact]
    public void TatOverlay_PhienTestCungKhongHien()
    {
        var rig = new SwitcherRig();
        rig.Settings.Current.OverlayEnabled = false;
        rig.Switcher.BeginCrosshairTest("notepad.exe");

        rig.Watcher.MoveTo(Window(0x10, 4242, "notepad.exe"));

        Assert.False(rig.Overlay.IsVisible);
    }

    [Fact]
    public void Reevaluate_ThemProfileSauKhiAppDaLenForeground_ApNgay()
    {
        var rig = new SwitcherRig();
        var gamePreset = new CrosshairProfile { Name = "Profile cs2" };
        rig.Library.Add(gamePreset);
        rig.Watcher.MoveTo(Window(0x30, 999, "cs2.exe"));
        Assert.False(rig.Overlay.IsVisible);

        rig.Settings.Current.GameProfiles.Add(new GameProfile { Pattern = "cs2.exe", PresetId = gamePreset.Id });
        rig.Switcher.Reevaluate();

        Assert.True(rig.Overlay.IsVisible);
        Assert.Same(gamePreset, rig.Library.Active);
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(false, true, false)]   // người dùng tắt overlay thì test cũng không ép bật
    public void QuyTacHienAn_CuaSoTest(bool enabled, bool inTest, bool expected)
    {
        Assert.Equal(expected, ProfileAutoSwitcher.ShouldShowOverlay(enabled, true, true, null, inTest));
    }

    // ================================================================== lệnh TestCrosshair

    private sealed class VmRig : IDisposable
    {
        public readonly FakeSettings Settings = new();
        public readonly FakeLibrary Library = new();
        public readonly ScriptedDialogs Dialogs = new();
        public readonly FakeLauncher Launcher = new();
        public readonly RecordingSwitcher Switcher = new();
        public readonly SettingsViewModel Vm;
        public readonly CrosshairProfile Current;

        public VmRig()
        {
            Current = CrosshairProfile.CreateDefault();
            Current.Name = "Tactical T";
            Current.InnerLines.Length = 17;
            Library.Add(Current);
            Library.SetActive(Current);
            Settings.Current.OverlayEnabled = false;

            Vm = new SettingsViewModel(
                Library, Stub<IOverlayController>(), Settings, Stub<IMonitorService>(),
                new CrosshairOverlay.Services.Display.DisplayAnalyzerService(Stub<IMonitorService>(), Stub<IDisplayModeReader>()),
                Stub<ITrayIconController>(), Stub<ICrosshairRenderer>(),
                Dialogs.Proxy, Stub<IHotkeyService>(), Stub<IForegroundWindowWatcher>(), Stub<IStartupService>(),
                Stub<IGameProfileMatcher>(), Stub<IRunningApplicationScanner>(), Stub<IUpdateService>(),
                Stub<ICustomImageStore>(), Switcher, Launcher, Stub<IHardwareInfoService>(), Stub<IAppPathProvider>(), Stub<IAppRestartService>(), Stub<IStorageLocationService>(), Stub<IDisplayModeReader>(), new CrosshairTranslatorService(),
                new PresetLibraryViewModel(Stub<IPresetCatalogService>(), Library, Stub<ICrosshairRenderer>(), NullLogger<PresetLibraryViewModel>.Instance),
                NullLogger<SettingsViewModel>.Instance);
        }

        public void Dispose() => Vm.Dispose();
    }

    [Fact]
    public void HiddenSettingsDefersEditorRebindingUntilReveal()
    {
        using var rig = new VmRig();
        var next = CrosshairProfile.CreateDefault();
        rig.Library.Add(next);
        Assert.Same(rig.Current, rig.Vm.Editor.Profile);

        rig.Vm.SetPresetPresentationActive(false);
        rig.Library.SetActive(next);
        Assert.Same(rig.Current, rig.Vm.Editor.Profile);
        Assert.Same(rig.Current, rig.Vm.SelectedPreset);

        rig.Vm.SetPresetPresentationActive(true);
        Assert.Same(next, rig.Vm.Editor.Profile);
        Assert.Same(next, rig.Vm.SelectedPreset);
    }

    [Fact]
    public async Task CoNotepad_BatOverlay_MoNotepad_KhongHoiGiThem()
    {
        using var rig = new VmRig();

        await rig.Vm.TestCrosshairCommand.ExecuteAsync(null);

        Assert.True(rig.Settings.Current.OverlayEnabled);
        Assert.Equal(new[] { "notepad.exe" }, rig.Launcher.Launched);
        Assert.Equal(new[] { "begin:notepad.exe" }, rig.Switcher.Calls);
        Assert.Empty(rig.Dialogs.Calls);
        Assert.Single(rig.Library.Presets);
    }

    [Fact]
    public async Task KhongCoNotepad_NguoiDungChonKhong_KhongLamGiThem()
    {
        using var rig = new VmRig();
        rig.Launcher.FailFor("notepad.exe", 2);
        rig.Dialogs.ConfirmAnswer = false;

        await rig.Vm.TestCrosshairCommand.ExecuteAsync(null);

        Assert.Equal(new[] { "begin:notepad.exe", "end" }, rig.Switcher.Calls);
        var confirm = Assert.Single(rig.Dialogs.Calls);
        Assert.Contains("Không tìm thấy Notepad. Bạn có muốn chọn một ứng dụng/game bất kỳ để kiểm tra không?", confirm);
        Assert.Single(rig.Library.Presets);
    }

    [Fact]
    public async Task KhongCoNotepad_ChonCs2_MoCs2_TaoPresetSaoChep_GanProfile_VaChonNo()
    {
        using var rig = new VmRig();
        rig.Launcher.FailFor("notepad.exe", 2);
        rig.Dialogs.ConfirmAnswer = true;
        rig.Dialogs.PickedFile = @"D:\Games\Counter-Strike\cs2.exe";

        await rig.Vm.TestCrosshairCommand.ExecuteAsync(null);

        Assert.Contains("PickFileToOpen:Executable Files (*.exe)|*.exe", rig.Dialogs.Calls);
        Assert.Equal(new[] { "notepad.exe", @"D:\Games\Counter-Strike\cs2.exe" }, rig.Launcher.Launched);

        var created = Assert.Single(rig.Library.Presets, p => p.Name == "Profile cs2");
        Assert.NotEqual(rig.Current.Id, created.Id);
        Assert.Equal(17, created.InnerLines.Length);          // sao chép thông số đang dùng
        Assert.Same(created, rig.Library.Active);             // được chọn
        Assert.Same(created, rig.Vm.SelectedPreset);

        var rule = Assert.Single(rig.Settings.Current.GameProfiles);
        Assert.Equal("cs2.exe", rule.Pattern);
        Assert.Equal(ProcessMatchMode.ProcessName, rule.MatchMode);
        Assert.Equal(created.Id, rule.PresetId);
        Assert.True(rule.Enabled);
        Assert.Contains("reevaluate", rig.Switcher.Calls);
        Assert.Contains(rig.Dialogs.Calls, c => c.Contains("Profile cs2") && c.Contains("cs2.exe"));
    }

    [Fact]
    public async Task DaCoProfileChoApp_TroSangPresetMoi_KhongTaoTrung()
    {
        using var rig = new VmRig();
        var old = new GameProfile { Name = "cs2 cũ", Pattern = "CS2.exe", PresetId = Guid.NewGuid(), Enabled = false };
        rig.Settings.Current.GameProfiles.Add(old);
        rig.Launcher.FailFor("notepad.exe", 2);
        rig.Dialogs.ConfirmAnswer = true;
        rig.Dialogs.PickedFile = @"D:\Games\cs2.exe";

        await rig.Vm.TestCrosshairCommand.ExecuteAsync(null);

        var rule = Assert.Single(rig.Settings.Current.GameProfiles);
        Assert.Same(old, rule);
        Assert.Equal(rig.Library.Active!.Id, rule.PresetId);
        Assert.True(rule.Enabled);
    }

    [Fact]
    public async Task AppChonKhongMoDuoc_BaoLoi_KhongTaoPreset()
    {
        using var rig = new VmRig();
        rig.Launcher.FailFor("notepad.exe", 2);
        rig.Launcher.FailFor(@"D:\Games\hong.exe", 193);   // ERROR_BAD_EXE_FORMAT
        rig.Dialogs.ConfirmAnswer = true;
        rig.Dialogs.PickedFile = @"D:\Games\hong.exe";

        await rig.Vm.TestCrosshairCommand.ExecuteAsync(null);

        Assert.Contains(rig.Dialogs.Calls, c => c.StartsWith("Error:", StringComparison.Ordinal) && c.Contains("hong.exe"));
        Assert.Single(rig.Library.Presets);
        Assert.Empty(rig.Settings.Current.GameProfiles);
    }

    [Fact]
    public async Task NguoiDungTuChoiUac_KhongBaoLoi_KhongTaoPreset()
    {
        using var rig = new VmRig();
        rig.Launcher.FailFor("notepad.exe", 2);
        rig.Launcher.FailFor(@"D:\Games\admin.exe", 1223);   // ERROR_CANCELLED
        rig.Dialogs.ConfirmAnswer = true;
        rig.Dialogs.PickedFile = @"D:\Games\admin.exe";

        await rig.Vm.TestCrosshairCommand.ExecuteAsync(null);

        Assert.DoesNotContain(rig.Dialogs.Calls, c => c.StartsWith("Error:", StringComparison.Ordinal));
        Assert.Single(rig.Library.Presets);
    }

    [Fact]
    public async Task LoiBatNgo_KhongLamSapApp()
    {
        using var rig = new VmRig();
        rig.Launcher.Throw = new NullReferenceException("bất ngờ");

        await rig.Vm.TestCrosshairCommand.ExecuteAsync(null);   // không được ném ra ngoài

        Assert.Contains(rig.Dialogs.Calls, c => c.Contains("Không chạy được Test tâm ngắm"));
    }

    // ================================================================== fakes

    /// <summary>Thể hiện rỗng của một interface: mọi lời gọi trả giá trị mặc định (collection rỗng).</summary>
    private static T Stub<T>() where T : class => DispatchProxy.Create<T, StubProxy>();

    /// <summary>Dùng lại cho test khác cần một interface rỗng.</summary>
    internal static T StubFor<T>() where T : class => Stub<T>();

    public class StubProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            var type = targetMethod?.ReturnType;
            if (type is null || type == typeof(void)) return null;
            if (type == typeof(string)) return string.Empty;
            if (type == typeof(Task)) return Task.CompletedTask;
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Task<>))
                return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(type.GetGenericArguments()[0])
                    .Invoke(null, [DefaultOf(type.GetGenericArguments()[0])]);
            return DefaultOf(type);
        }

        private static object? DefaultOf(Type type)
        {
            if (type.IsValueType) return Activator.CreateInstance(type);
            if (type.IsArray) return Array.CreateInstance(type.GetElementType()!, 0);
            if (typeof(IEnumerable).IsAssignableFrom(type) && type.IsGenericType)
                return Array.CreateInstance(type.GetGenericArguments()[0], 0);
            return null;
        }
    }

    public class ScriptedDialogs
    {
        public readonly List<string> Calls = [];
        public bool ConfirmAnswer;
        public string? PickedFile;
        public readonly IDialogService Proxy;

        public ScriptedDialogs()
        {
            Proxy = DispatchProxy.Create<IDialogService, DialogsProxy>();
            ((DialogsProxy)(object)Proxy).Owner = this;
        }
    }

    public class DialogsProxy : DispatchProxy
    {
        public ScriptedDialogs Owner = null!;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            switch (targetMethod!.Name)
            {
                case nameof(IDialogService.Confirm):
                    Owner.Calls.Add($"Confirm:{args![0]}|{args[1]}");
                    return Owner.ConfirmAnswer;
                case nameof(IDialogService.PickFileToOpen):
                    Owner.Calls.Add($"PickFileToOpen:{args![0]}");
                    return Owner.PickedFile;
                case nameof(IDialogService.ShowMessage):
                    var isError = args!.Length > 2 && args[2] is true;
                    Owner.Calls.Add($"{(isError ? "Error" : "Info")}:{args[0]}|{args[1]}");
                    return null;
                default:
                    return targetMethod.ReturnType == typeof(bool) ? false : null;
            }
        }
    }

    public sealed class FakeLauncher : IProcessLauncher
    {
        private readonly Dictionary<string, int> _failures = new(StringComparer.OrdinalIgnoreCase);
        public readonly List<string> Launched = [];
        public Exception? Throw;

        public void FailFor(string app, int win32Error) => _failures[app] = win32Error;

        public void OpenFolder(string path) => Launched.Add("folder:" + path);

        public void Launch(string fileNameOrPath)
        {
            Launched.Add(fileNameOrPath);
            if (Throw is not null) throw Throw;
            if (_failures.TryGetValue(fileNameOrPath, out var code)) throw new Win32Exception(code);
        }
    }

    public sealed class RecordingSwitcher : IProfileAutoSwitcher
    {
        public readonly List<string> Calls = [];
        public bool IsEnabled { get; set; } = true;
        public GameProfile? ActiveGameProfile => null;
        public event EventHandler<ForegroundWindowInfo>? ExclusiveFullscreenDetected { add { } remove { } }
        public void Start() { }
        public void Stop() { }
        public void ApplyVisibility() { }
        public void SetStreamSuppressed(bool suppressed) { }
        public void BeginCrosshairTest(string processName) => Calls.Add("begin:" + processName);
        public void EndCrosshairTest() => Calls.Add("end");
        public void Reevaluate() => Calls.Add("reevaluate");
        public bool IsShowingCrosshairTest => false;
        public event EventHandler? CrosshairTestStateChanged { add { } remove { } }
        public void Dispose() { }
    }

    public sealed class FakeWatcher : IForegroundWindowWatcher
    {
        public ForegroundWindowInfo Current { get; private set; } = ForegroundWindowInfo.Empty;
        public ForegroundWindowInfo LastExternal => Current;
        public ForegroundWindowInfo Tracked { get; private set; } = ForegroundWindowInfo.Empty;
        public event EventHandler<ForegroundWindowChangedEventArgs>? ForegroundChanged;
        public event EventHandler<ForegroundWindowInfo>? TrackedWindowClosed;
        public void Start() { }
        public void Stop() { }
        public void Track(ForegroundWindowInfo window) => Tracked = window;

        public void MoveTo(ForegroundWindowInfo window)
        {
            var previous = Current;
            Current = window;
            ForegroundChanged?.Invoke(this, new ForegroundWindowChangedEventArgs(previous, window));
        }

        public void CloseTracked()
        {
            var closed = Tracked;
            Tracked = ForegroundWindowInfo.Empty;
            TrackedWindowClosed?.Invoke(this, closed);
        }

        public void Dispose() { }
    }

    public sealed class FakeOverlay : IOverlayController
    {
        public bool IsVisible { get; private set; }
        public CrosshairProfile? CurrentProfile => null;
        public MonitorInfo? CurrentMonitor => null;
        public event EventHandler<OverlayVisibilityChangedEventArgs>? VisibilityChanged { add { } remove { } }
        public void SetVisible(bool visible) => IsVisible = visible;
        public bool SetCaptureExclusion(bool enabled) => true;
        public void Initialize() { }
        public void SetProfile(CrosshairProfile profile) { }
        public void Toggle() => IsVisible = !IsVisible;
        public void MoveToMonitor(MonitorInfo monitor) { }
        public void SetMonitorSelection(MonitorSelectionMode mode, string? targetDeviceName) { }
        public void Invalidate() { }
        public void SetSuppressed(bool suppressed) { }
        public void SetFrameRateLimit(int framesPerSecond) { }
        public void SetGlobalScale(double scale) { }
        public void SetColorOverride(System.Windows.Media.Color? color) { }
        public void SetPixelShift(int dx, int dy) { }
        public void Dispose() { }
    }

    public sealed class FakeSettings : IAppSettingsService
    {
        public AppSettings Current { get; } = new();
        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void RequestSave() { }
        public Task FlushAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Dispose() { }
    }

    public sealed class FakeLibrary : IPresetLibrary
    {
        private readonly ObservableCollection<CrosshairProfile> _items = [];

        public FakeLibrary() => Presets = new ReadOnlyObservableCollection<CrosshairProfile>(_items);

        public ReadOnlyObservableCollection<CrosshairProfile> Presets { get; }
        public CrosshairProfile? Active { get; private set; }
        public event EventHandler? ActiveChanged;

        public void Add(CrosshairProfile profile) => _items.Add(profile);

        public void SetActive(CrosshairProfile profile)
        {
            Active = profile;
            ActiveChanged?.Invoke(this, EventArgs.Empty);
        }

        public Task<CrosshairProfile> AddAsync(CrosshairProfile profile, CancellationToken cancellationToken = default)
        {
            _items.Add(profile);
            SetActive(profile);
            return Task.FromResult(profile);
        }

        public Task<CrosshairProfile> CreateAsync(CancellationToken cancellationToken = default) =>
            AddAsync(CrosshairProfile.CreateDefault(), cancellationToken);

        public Task InitializeAsync(Guid preferredActiveId, IReadOnlyList<Guid>? order = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public bool Move(CrosshairProfile profile, int newIndex) => false;
        public event EventHandler? OrderChanged { add { } remove { } }
        public void StepActive(int direction) { }
        public Task<CrosshairProfile> DuplicateAsync(CrosshairProfile source, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(CrosshairProfile profile, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task FlushAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Dispose() { }
    }
}
