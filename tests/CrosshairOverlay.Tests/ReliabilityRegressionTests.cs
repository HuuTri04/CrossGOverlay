using System.IO;
using System.Runtime.CompilerServices;
using System.Net;
using System.Net.Sockets;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CrosshairOverlay.Core.Abstractions;
using CrosshairOverlay.Core.Models;
using CrosshairOverlay.Controls;
using CrosshairOverlay.Services.Import;
using CrosshairOverlay.Services.Input;
using CrosshairOverlay.Services.Logging;
using CrosshairOverlay.Services.Overlay;
using CrosshairOverlay.Services.Process;
using CrosshairOverlay.Services.Rendering;
using CrosshairOverlay.Services.Storage;
using CrosshairOverlay.Services.System;
using CrosshairOverlay.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CrosshairOverlay.Tests;

public sealed class ReliabilityRegressionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "cgo-reliability-" + Guid.NewGuid().ToString("N"));

    public ReliabilityRegressionTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }

#if DEBUG
    [Fact]
    public void StaticOverlayDrawingDoesNotRequestAnotherRender()
    {
        RunSta(() =>
        {
            var host = new CrosshairVisualHost();
            var drawing = new GeometryDrawing();
            host.SetDrawing(drawing);
            var initial = host.DebugInvalidationCount;
            host.SetDrawing(drawing);
            host.SetAliasing(false);
            Assert.Equal(initial, host.DebugInvalidationCount);
        });
    }
#endif

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(999)]
    public async Task InvalidUiFpsInSettingsFallsBackToSafeDefault(int fps)
    {
        var paths = new AppPathProvider(_root);
        await File.WriteAllTextAsync(paths.SettingsFilePath, $"{{\"UiTargetFps\":{fps}}}");
        using var service = new AppSettingsService(paths, NullLogger<AppSettingsService>.Instance);

        await service.LoadAsync();

        Assert.Equal(144, service.Current.UiTargetFps);
    }

    [Fact]
    public async Task CorruptPrimaryLoadsBackup_AndCorruptBothUsesDefaults()
    {
        var paths = new AppPathProvider(_root);
        await File.WriteAllTextAsync(paths.SettingsFilePath, "{corrupt");
        await File.WriteAllTextAsync(paths.SettingsFilePath + ".bak", "{\"Language\":\"en\"}");
        using var service = new AppSettingsService(paths, NullLogger<AppSettingsService>.Instance);

        await service.LoadAsync();
        Assert.Equal("en", service.Current.Language);

        await File.WriteAllTextAsync(paths.SettingsFilePath + ".bak", "{corrupt");
        await service.LoadAsync();
        Assert.Equal(AppSettings.CreateDefault().Language, service.Current.Language);
        Assert.NotEmpty(service.Current.Hotkeys);
    }

    [Fact]
    public async Task NullEntriesInSettingsCollectionsAreIgnored()
    {
        var paths = new AppPathProvider(_root);
        await File.WriteAllTextAsync(paths.SettingsFilePath,
            "{\"Hotkeys\":[null],\"GameProfiles\":[null],\"OverlayEnabled\":false}");
        using var service = new AppSettingsService(paths, NullLogger<AppSettingsService>.Instance);

        await service.LoadAsync();

        Assert.False(service.Current.OverlayEnabled);
        Assert.NotEmpty(service.Current.Hotkeys);
        Assert.All(service.Current.Hotkeys, hotkey => Assert.NotNull(hotkey));
        Assert.Empty(service.Current.GameProfiles);
        Assert.All(service.Current.GameProfiles, profile => Assert.NotNull(profile));
    }

    [Fact]
    public async Task ConcurrentSaveRequestsFlushToOneValidAtomicFile()
    {
        var paths = new AppPathProvider(_root);
        using var service = new AppSettingsService(paths, NullLogger<AppSettingsService>.Instance);
        service.Current.Language = "en";

        var writes = Enumerable.Range(0, 20).Select(_ => service.SaveAsync());
        await Task.WhenAll(writes);
        Parallel.For(0, 100, _ => service.RequestSave());
        await service.FlushAsync();

        var loaded = await AtomicFile.LoadJsonAsync<AppSettings>(paths.SettingsFilePath);
        Assert.Equal("en", loaded?.Language);
        Assert.False(File.Exists(paths.SettingsFilePath + ".tmp"));
    }

    [Fact]
    public void MalformedPixelCodesNeverEscapeDecoder()
    {
        var inputs = new[] { null, "", "PX|INVALID_DATA|9999", "PX1|-1|!!!", "PX1|24|AQ==",
            new string('A', PixelArtCode.MaxCodeLength + 1) };
        foreach (var code in inputs)
        {
            Assert.False(PixelArtCode.TryDecode(code, out var size, out var pixels));
            Assert.Equal(0, size);
            Assert.Empty(pixels);
        }
    }

    [Fact]
    public void InvalidPixelGridSizeDoesNotCorruptBitmapOrDrawing()
    {
        RunSta(() =>
        {
            var vm = new PixelArtViewModel(
                TestCrosshairTests.StubFor<IPresetLibrary>(),
                TestCrosshairTests.StubFor<ICustomImageStore>(),
                TestCrosshairTests.StubFor<IOverlayController>());
            var original = vm.Bitmap;
            vm.GridSize = -1;
            vm.GridSize = 9999;
            Assert.Equal(24, vm.GridSize);
            Assert.Same(original, vm.Bitmap);
            vm.DrawLine(0, 0, 23, 23, erase: false);
            vm.FinishStroke();
            Assert.True(PixelArtCode.TryDecode(vm.ShareCode, out var size, out _));
            Assert.Equal(24, size);

            var imageBeforeInvalidCode = vm.Bitmap;
            vm.ShareCode = "PX|INVALID_DATA|9999";
            vm.LoadCodeCommand.Execute(null);
            Assert.Same(imageBeforeInvalidCode, vm.Bitmap);
            Assert.False(string.IsNullOrWhiteSpace(vm.StatusMessage));
        });
    }

    [Fact]
    public void ReplacingBackgroundThirtyTimesKeepsBitmapsFrozenAndFilesUnlocked()
    {
        var paths = new List<string>();
        try
        {
            foreach (var (width, height) in new[] { (24, 24), (1920, 1080), (3840, 2160) })
            {
                var path = Path.Combine(_root, $"background-{width}.png");
                var source = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32,
                    null, new byte[width * height * 4], width * 4);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(source));
                using (var file = File.Create(path)) encoder.Save(file);
                paths.Add(path);
            }

            BitmapSource? current = null;
            for (var i = 0; i < 30; i++)
            {
                var path = paths[i % paths.Count];
                current = AppBackgroundImageLoader.Decode(path, maxDecodedSide: 1600);
                Assert.True(current.IsFrozen);
                Assert.InRange(Math.Max(current.PixelWidth, current.PixelHeight), 1, 1600);
                using var exclusive = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            }
            Assert.NotNull(current);
        }
        finally
        {
            foreach (var path in paths) File.Delete(path);
        }
    }

    [Fact]
    public void ForegroundHooksCanStartStopFiftyTimes_AndDisposeIsIdempotent()
    {
        RunSta(() =>
        {
            var paths = new AppPathProvider(_root);
            using var audit = new AuditLoggerService(paths, NullLogger<AuditLoggerService>.Instance);
            using var watcher = new ForegroundWindowWatcher(
                TestCrosshairTests.StubFor<IMonitorService>(), audit,
                NullLogger<ForegroundWindowWatcher>.Instance);

            for (var i = 0; i < 50; i++)
            {
                watcher.Start();
                watcher.Track(ForegroundWindowInfo.Empty); // Invalid HWND must be harmless.
                watcher.Stop();
            }
            watcher.Dispose();
            watcher.Stop();
        });
    }

    [Fact]
    public void GameTabViewModelsCanBeOpenedAndClosedOneHundredTimesWithoutEventRetention()
    {
        using var settings = new AppSettingsService(new AppPathProvider(_root),
            NullLogger<AppSettingsService>.Instance);
        var watcher = TestCrosshairTests.StubFor<IForegroundWindowWatcher>();
        var library = TestCrosshairTests.StubFor<IPresetLibrary>();
        var matcher = TestCrosshairTests.StubFor<IGameProfileMatcher>();
        var scanner = TestCrosshairTests.StubFor<IRunningApplicationScanner>();

        var references = CreateAndCloseGameTabs(settings, library, watcher, matcher, scanner);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.All(references, reference => Assert.False(reference.TryGetTarget(out _)));
    }

    [Fact]
    public void GifThumbnailDoesNotLeaveAnimationTimerRunning()
    {
        RunSta(() =>
        {
            var path = Path.Combine(_root, "thumbnail.gif");
            var encoder = new GifBitmapEncoder();
            foreach (var color in new byte[] { 0, 255 })
            {
                var pixels = new byte[] { color, color, color, 255 };
                var source = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32,
                    null, pixels, 4);
                encoder.Frames.Add(BitmapFrame.Create(source));
            }
            using (var stream = File.Create(path)) encoder.Save(stream);

            var profile = CrosshairProfile.CreateDefault();
            profile.Type = CrosshairType.Image;
            profile.Image.FilePath = path;
            var renderer = new CrosshairRenderer(NullLogger<CrosshairRenderer>.Instance);
            var thumbnail = new CrosshairThumbnail
            {
                Width = 64,
                Height = 64,
                Renderer = renderer,
                Profile = profile,
            };
            thumbnail.Measure(new System.Windows.Size(64, 64));
            thumbnail.Arrange(new System.Windows.Rect(0, 0, 64, 64));
            var target = new RenderTargetBitmap(64, 64, 96, 96, PixelFormats.Pbgra32);
            target.Render(thumbnail);

            Assert.Equal(1, renderer.ImageCacheUsage.Count);
            Assert.Equal(0, DrawingAnimations.ActiveCount);
        });
    }

    [Fact]
    public void RecycledThumbnailsDoNotKeepOneHundredProfilesAlive()
    {
        RunSta(() =>
        {
            var renderer = new CrosshairRenderer(NullLogger<CrosshairRenderer>.Instance);
            var references = RenderAndReleaseThumbnails(renderer);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            Assert.All(references, reference => Assert.False(reference.TryGetTarget(out _)));
            Assert.InRange(renderer.GeometryCacheCount, 1, 64);
        });
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<CrosshairProfile>[] RenderAndReleaseThumbnails(CrosshairRenderer renderer)
    {
        var references = new WeakReference<CrosshairProfile>[100];
        var thumbnail = new CrosshairThumbnail { Width = 64, Height = 64, Renderer = renderer };
        var target = new RenderTargetBitmap(64, 64, 96, 96, PixelFormats.Pbgra32);
        for (var i = 0; i < references.Length; i++)
        {
            var profile = CrosshairProfile.CreateDefault();
            profile.InnerLines.Length = 1 + i;
            references[i] = new WeakReference<CrosshairProfile>(profile);
            thumbnail.Profile = profile;
            thumbnail.Measure(new System.Windows.Size(64, 64));
            thumbnail.Arrange(new System.Windows.Rect(0, 0, 64, 64));
            thumbnail.InvalidateVisual();
            target.Render(thumbnail);
            thumbnail.Profile = null;
        }
        return references;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<GameProfilesViewModel>[] CreateAndCloseGameTabs(
        IAppSettingsService settings, IPresetLibrary library, IForegroundWindowWatcher watcher,
        IGameProfileMatcher matcher, IRunningApplicationScanner scanner)
    {
        var references = new WeakReference<GameProfilesViewModel>[100];
        for (var i = 0; i < references.Length; i++)
        {
            var vm = new GameProfilesViewModel(settings, library, watcher, matcher, scanner);
            references[i] = new WeakReference<GameProfilesViewModel>(vm);
            vm.Dispose();
        }
        return references;
    }

    [Fact]
    public void HotkeyServiceRejectsInvalidHandleWithoutPoisoningLaterAttach()
    {
        RunSta(() =>
        {
            using var service = new HotkeyService(NullLogger<HotkeyService>.Instance);
            Assert.Throws<ArgumentException>(() => service.Attach(0));
            Assert.Throws<InvalidOperationException>(() => service.Attach(new nint(1)));

            using var window = new HotkeyMessageWindow();
            service.Attach(window.Handle);
        });
    }

    [Fact]
    public void ObsHandshakeDisconnectAndPasswordChangeDoNotBlockUiThread()
    {
        RunSta(() =>
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            using var settings = new AppSettingsService(new AppPathProvider(_root),
                NullLogger<AppSettingsService>.Instance);
            settings.Current.StreamModeEnabled = true;
            using var obs = new ObsWebSocketService(settings, NullLogger<ObsWebSocketService>.Instance,
                new Uri($"ws://127.0.0.1:{port}"));

            obs.Start();
            using (var connection = listener.AcceptTcpClientAsync()
                .WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult())
            {
                // Close during the WebSocket handshake, as if OBS disappeared.
            }
            obs.SetPassword("changed while disconnected");
            settings.Current.StreamModeEnabled = false;
            Assert.False(obs.IsCapturing);
            obs.Dispose();
        });
    }

    private static void RunSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { error = ex; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null) throw new Xunit.Sdk.XunitException(error.ToString());
    }
}
