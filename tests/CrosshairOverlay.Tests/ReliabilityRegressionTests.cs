using System.IO;
using System.Text.Json;
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
using CrosshairOverlay.Services.Export;
using CrosshairOverlay.Services.Input;
using CrosshairOverlay.Services.Logging;
using CrosshairOverlay.Services.Overlay;
using CrosshairOverlay.Services.Process;
using CrosshairOverlay.Services.Rendering;
using CrosshairOverlay.Services.Storage;
using CrosshairOverlay.Services.System;
using CrosshairOverlay.ViewModels;
using CrosshairOverlay.Views;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CrosshairOverlay.Tests;

public sealed class ReliabilityRegressionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "cgo-reliability-" + Guid.NewGuid().ToString("N"));

    public ReliabilityRegressionTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void WindowsNotificationLogoUsesOnlyAnExistingPhysicalFile()
    {
        Assert.Null(WindowsNotificationService.GetExistingLogoUri(_root));

        var logo = WindowsNotificationService.GetExistingLogoUri(AppContext.BaseDirectory);
        Assert.NotNull(logo);
        Assert.True(logo.IsFile);
        Assert.True(File.Exists(logo.LocalPath));
    }

    [Fact]
    public void PixelArtCanvasDisablesPenGesturesWithoutChangingMouseInput()
    {
        RunSta(() =>
        {
            var canvas = new PixelArtCanvas();
            Assert.False(System.Windows.Input.Stylus.GetIsFlicksEnabled(canvas));
            Assert.False(System.Windows.Input.Stylus.GetIsPressAndHoldEnabled(canvas));
            Assert.False(System.Windows.Input.Stylus.GetIsTapFeedbackEnabled(canvas));
            Assert.False(System.Windows.Input.Stylus.GetIsTouchFeedbackEnabled(canvas));
            Assert.True(canvas.Focusable);
        });
    }

    [Fact]
    public void PixelArtInputModeDefaultsToMouseAndCommandsSwitchExclusiveModes()
    {
        RunSta(() =>
        {
            var vm = new PixelArtViewModel(
                TestCrosshairTests.StubFor<IPresetLibrary>(),
                TestCrosshairTests.StubFor<ICustomImageStore>(),
                TestCrosshairTests.StubFor<IOverlayController>());

            Assert.Equal(DrawingInputMode.Mouse, vm.CurrentInputMode);
            Assert.True(vm.IsMouseInputSelected);
            Assert.False(vm.IsStylusInputSelected);

            vm.UseStylusInputCommand.Execute(null);
            Assert.Equal(DrawingInputMode.Stylus, vm.CurrentInputMode);
            Assert.False(vm.IsMouseInputSelected);
            Assert.True(vm.IsStylusInputSelected);

            vm.UseMouseInputCommand.Execute(null);
            Assert.True(vm.IsMouseInputSelected);
            Assert.False(vm.IsStylusInputSelected);
        });
    }

    [Fact]
    public void PixelArtInputFilterRejectsPromotedMouseAndPalmTouch()
    {
        Assert.True(PixelArtCanvas.CanDrawWithMouse(DrawingInputMode.Mouse, promotedFromStylus: false));
        Assert.False(PixelArtCanvas.CanDrawWithMouse(DrawingInputMode.Mouse, promotedFromStylus: true));
        Assert.False(PixelArtCanvas.CanDrawWithMouse(DrawingInputMode.Stylus, promotedFromStylus: false));
        Assert.True(PixelArtCanvas.CanDrawWithStylus(DrawingInputMode.Stylus,
            System.Windows.Input.TabletDeviceType.Stylus));
        Assert.False(PixelArtCanvas.CanDrawWithStylus(DrawingInputMode.Stylus,
            System.Windows.Input.TabletDeviceType.Touch));
        Assert.False(PixelArtCanvas.CanDrawWithStylus(DrawingInputMode.Mouse,
            System.Windows.Input.TabletDeviceType.Stylus));
    }

    [Fact]
    public void BindingAuditDetectsAnActuallyInvalidPath()
    {
        RunSta(() =>
        {
            var text = new System.Windows.Controls.TextBlock { DataContext = new object() };
            System.Windows.Data.BindingOperations.SetBinding(text,
                System.Windows.Controls.TextBlock.TextProperty,
                new System.Windows.Data.Binding("MissingAuditProperty"));
            text.GetBindingExpression(System.Windows.Controls.TextBlock.TextProperty)?.UpdateTarget();
            Assert.Equal(1, CrosshairOverlay.Core.Diagnostics.BindingAudit.Scan(text));
        });
    }

    [Fact]
    public void MicaRequiresSupportedWindowsAndPreservesCustomBackground()
    {
        Assert.False(SettingsWindow.CanUseMica(22000, highContrast: false, hasCustomBackground: false));
        Assert.True(SettingsWindow.CanUseMica(22621, highContrast: false, hasCustomBackground: false));
        Assert.False(SettingsWindow.CanUseMica(26200, highContrast: true, hasCustomBackground: false));
        Assert.False(SettingsWindow.CanUseMica(26200, highContrast: false, hasCustomBackground: true));
    }

    [Fact]
    public async Task FirstLaunchSeedsPresetsAndReloadsTheSameIdentities()
    {
        var repository = new PresetRepository(new AppPathProvider(_root),
            TestCrosshairTests.StubFor<ICustomImageStore>(), NullLogger<PresetRepository>.Instance);

        var first = await repository.GetAllAsync();
        var second = await repository.GetAllAsync();

        Assert.NotEmpty(first);
        Assert.Equal(first.Count, Directory.GetFiles(Path.Combine(_root, "presets"), "*.json").Length);
        Assert.Equal(first.Select(preset => preset.Id).OrderBy(id => id),
            second.Select(preset => preset.Id).OrderBy(id => id));
    }

    [Fact]
    public void FavoriteAndNotificationPreferencesRoundTripThroughJson()
    {
        var preset = CrosshairProfile.CreateDefault();
        preset.IsFavorite = true;
        var restoredPreset = JsonSerializer.Deserialize<CrosshairProfile>(
            JsonSerializer.Serialize(preset, AppJson.Options), AppJson.Options);
        Assert.True(restoredPreset?.IsFavorite);

        var settings = AppSettings.CreateDefault();
        Assert.True(settings.NotifyOnMinimizeToTray);
        Assert.False(settings.NotifyOnPresetChanged);
        Assert.True(settings.NotifyOnAppStatus);
        settings.NotifyOnAppStatus = false;
        settings.FavoriteCatalogPresetIds.Add(Guid.NewGuid());
        var restoredSettings = JsonSerializer.Deserialize<AppSettings>(
            JsonSerializer.Serialize(settings, AppJson.Options), AppJson.Options);
        Assert.NotNull(restoredSettings);
        Assert.False(restoredSettings.NotifyOnAppStatus);
        Assert.Single(restoredSettings.FavoriteCatalogPresetIds);
    }

    [Fact]
    public void LazyPixelAndCatalogTabsLoadTheirNewCommandsAndStarStyle()
    {
        RunSta(() =>
        {
            _ = ThemeFixture.Load();
            Assert.NotNull(new PixelArtTab().FindResource("FavoriteStarToggle"));
            Assert.NotNull(new PresetLibraryView().FindResource("FavoriteStarToggle"));
            var trayResources = new System.Windows.ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/CrossGOverlay;component/Resources/TrayMenu.xaml"),
            };
            Assert.NotNull(trayResources["TrayContextMenu"]);
        });
    }

    [Fact]
    public void TrayFavoriteMenuLimitsEntriesAndAppliesSelectedPreset()
    {
        using var vm = new TrayMenuViewModel(() => { }, () => { }, () => { });
        var ids = Enumerable.Range(0, 20).Select(index => (Guid.NewGuid(), $"Preset {index}"))
            .ToArray();
        Guid applied = Guid.Empty;
        vm.SetFavorites(ids, id => applied = id);
        Assert.True(vm.HasFavorites);
        Assert.Equal(12, vm.FavoritePresets.Count);
        vm.FavoritePresets[4].ApplyCommand.Execute(null);
        Assert.Equal(ids[4].Item1, applied);
    }

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
    public async Task Test_ConcurrentSave_ShouldNotThrowException()
    {
        var paths = new AppPathProvider(_root);
        using var first = new AppSettingsService(paths, NullLogger<AppSettingsService>.Instance);
        using var second = new AppSettingsService(paths, NullLogger<AppSettingsService>.Instance);
        first.Current.Language = "en";
        second.Current.Language = "en";

        var saves = Enumerable.Range(0, 100).Select(index => Task.Run(() =>
            (index % 2 == 0 ? first : second).SaveAsync()));
        await Task.WhenAll(saves).WaitAsync(TimeSpan.FromSeconds(30));

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
    public void PixelArtUndoRedoGroupsStrokeAndClearWithoutBitmapSnapshots()
    {
        RunSta(() =>
        {
            var vm = new PixelArtViewModel(
                TestCrosshairTests.StubFor<IPresetLibrary>(),
                TestCrosshairTests.StubFor<ICustomImageStore>(),
                TestCrosshairTests.StubFor<IOverlayController>());
            vm.DrawLine(0, 0, 2, 0, erase: false);
            vm.FinishStroke();
            Assert.True(vm.UndoCommand.CanExecute(null));

            vm.ClearCanvasCommand.Execute(null);
            Assert.True(vm.UndoCommand.CanExecute(null));
            Assert.All(ReadPixels(vm), pixel => Assert.Equal(0u, pixel));

            vm.UndoCommand.Execute(null); // Restore the whole cleared stroke.
            Assert.Equal(3, ReadPixels(vm).Count(pixel => pixel != 0));
            vm.UndoCommand.Execute(null); // Earlier stroke history was released by Clear.
            Assert.Equal(3, ReadPixels(vm).Count(pixel => pixel != 0));
            vm.RedoCommand.Execute(null);
            Assert.All(ReadPixels(vm), pixel => Assert.Equal(0u, pixel));
            for (var index = 0; index < 100; index++) vm.RedoCommand.Execute(null);
            Assert.All(ReadPixels(vm), pixel => Assert.Equal(0u, pixel));
            vm.DrawLine(4, 4, 4, 4, erase: false);
            vm.FinishStroke();
            Assert.False(vm.RedoCommand.CanExecute(null));
        });
    }

    [Fact]
    public void PixelArtUndoTwiceRedoOnceRestoresSecondStrokePixels()
    {
        RunSta(() =>
        {
            var vm = new PixelArtViewModel(
                TestCrosshairTests.StubFor<IPresetLibrary>(),
                TestCrosshairTests.StubFor<ICustomImageStore>(),
                TestCrosshairTests.StubFor<IOverlayController>());
            vm.SelectPaletteColorCommand.Execute("#FF0000");
            for (var x = 0; x < 3; x++)
            {
                vm.DrawLine(x, 0, x, 0, erase: false);
                vm.FinishStroke();
            }

            vm.UndoCommand.Execute(null);
            vm.UndoCommand.Execute(null);
            vm.RedoCommand.Execute(null);

            var pixels = ReadPixels(vm);
            Assert.Equal(0xFFFF0000u, pixels[0]);
            Assert.Equal(0xFFFF0000u, pixels[1]);
            Assert.Equal(0u, pixels[2]);
        });
    }

    [Fact]
    public void PixelArtLiveStrokeWritesSelectedColorAtTheCorrectBitmapCell()
    {
        RunSta(() =>
        {
            var vm = new PixelArtViewModel(
                TestCrosshairTests.StubFor<IPresetLibrary>(),
                TestCrosshairTests.StubFor<ICustomImageStore>(),
                TestCrosshairTests.StubFor<IOverlayController>());
            Assert.Equal(PixelFormats.Bgra32, vm.Bitmap.Format);
            vm.SelectPaletteColorCommand.Execute("#FF0000");
            vm.DrawLine(3, 5, 3, 5, erase: false);
            var pixels = ReadPixels(vm);
            Assert.Equal(0xFFFF0000u, pixels[5 * vm.GridSize + 3]);
            Assert.Equal(1, pixels.Count(pixel => pixel != 0));

            vm.FinishStroke();
            vm.SelectPaletteColorCommand.Execute("#0000FF");
            vm.DrawLine(7, 11, 9, 11, erase: false);
            pixels = ReadPixels(vm);
            Assert.Equal(0xFFFF0000u, pixels[5 * vm.GridSize + 3]);
            Assert.All(pixels.Skip(11 * vm.GridSize + 7).Take(3),
                pixel => Assert.Equal(0xFF0000FFu, pixel));
            Assert.Equal(4, pixels.Count(pixel => pixel != 0));
        });
    }

    [Fact]
    public void PixelArtBatchedInputKeepsIntermediateCellsAndCapturedColorsForUndo()
    {
        RunSta(() =>
        {
            var vm = new PixelArtViewModel(
                TestCrosshairTests.StubFor<IPresetLibrary>(),
                TestCrosshairTests.StubFor<ICustomImageStore>(),
                TestCrosshairTests.StubFor<IOverlayController>());
            Span<PixelStrokeSegment> samples =
            [
                new(0, 0, 5, 0, 0xFFFF0000u),
                new(5, 0, 5, 5, 0xFF0000FFu),
            ];

            vm.DrawSegments(samples);
            vm.FinishStroke();

            var pixels = ReadPixels(vm);
            Assert.All(pixels.Take(5), pixel => Assert.Equal(0xFFFF0000u, pixel));
            Assert.All(Enumerable.Range(0, 6), y =>
                Assert.Equal(0xFF0000FFu, pixels[y * vm.GridSize + 5]));

            vm.UndoCommand.Execute(null);
            Assert.All(ReadPixels(vm), pixel => Assert.Equal(0u, pixel));
            vm.RedoCommand.Execute(null);
            Assert.Equal(0xFF0000FFu, ReadPixels(vm)[5 * vm.GridSize + 5]);
        });
    }

    [Fact]
    public void PixelArtCanvasRendersTheUpdatedBitmapInsteadOfAEmptyGrid()
    {
        RunSta(() =>
        {
            var vm = new PixelArtViewModel(
                TestCrosshairTests.StubFor<IPresetLibrary>(),
                TestCrosshairTests.StubFor<ICustomImageStore>(),
                TestCrosshairTests.StubFor<IOverlayController>());
            var canvas = new PixelArtCanvas
            {
                Width = 384,
                Height = 384,
                GridSize = vm.GridSize,
                ShowCenterGuide = false,
                Bitmap = vm.Bitmap,
            };
            canvas.Measure(new System.Windows.Size(384, 384));
            canvas.Arrange(new System.Windows.Rect(0, 0, 384, 384));

            vm.SelectPaletteColorCommand.Execute("#FF0000");
            vm.DrawLine(3, 5, 3, 5, erase: false);
            var rendered = new RenderTargetBitmap(384, 384, 96, 96, PixelFormats.Pbgra32);
            rendered.Render(canvas);
            var pixels = new uint[384 * 384];
            rendered.CopyPixels(pixels, 384 * 4, 0);

            Assert.Equal(0xFFFF0000u, pixels[88 * 384 + 56]);
        });
    }

    [Fact]
    public void PixelArtPartialWritesRespectEveryGridSizeAndUndoAfterErase()
    {
        RunSta(() =>
        {
            foreach (var size in new[] { 16, 24, 32 })
            {
                var vm = new PixelArtViewModel(
                    TestCrosshairTests.StubFor<IPresetLibrary>(),
                    TestCrosshairTests.StubFor<ICustomImageStore>(),
                    TestCrosshairTests.StubFor<IOverlayController>());
                vm.GridSize = size;
                vm.SelectPaletteColorCommand.Execute("#00FF00");
                vm.DrawLine(size - 1, size - 1, size - 1, size - 1, erase: false);
                vm.FinishStroke();
                Assert.Equal(0xFF00FF00u, ReadPixels(vm)[size * size - 1]);

                vm.DrawLine(size - 1, size - 1, size - 1, size - 1, erase: true);
                vm.FinishStroke();
                Assert.Equal(0u, ReadPixels(vm)[size * size - 1]);

                vm.UndoCommand.Execute(null);
                Assert.Equal(0xFF00FF00u, ReadPixels(vm)[size * size - 1]);
            }
        });
    }

    [Fact]
    public void PixelArtAccepts1024GridAndKeepsUndoAfterCornerStroke()
    {
        RunSta(() =>
        {
            var vm = new PixelArtViewModel(
                TestCrosshairTests.StubFor<IPresetLibrary>(),
                TestCrosshairTests.StubFor<ICustomImageStore>(),
                TestCrosshairTests.StubFor<IOverlayController>());
            vm.GridSizeInput = "1024";
            vm.ApplyGridSizeCommand.Execute(null);

            Assert.Equal(1024, vm.GridSize);
            Assert.Equal(1024, vm.CanvasDisplaySize);
            Assert.Equal(1024, vm.Bitmap.PixelWidth);
            Assert.Empty(vm.ShareCode); // Background encode has not run yet.
            vm.DrawLine(1023, 1023, 1023, 1023, erase: false);
            vm.FinishStroke();
            Assert.Equal(0xFF00FF00u, ReadPixels(vm)[^1]);

            vm.UndoCommand.Execute(null);
            Assert.Equal(0u, ReadPixels(vm)[^1]);
            vm.RedoCommand.Execute(null);
            Assert.Equal(0xFF00FF00u, ReadPixels(vm)[^1]);

            vm.GridSizeInput = "1025";
            vm.ApplyGridSizeCommand.Execute(null);
            Assert.Equal(1024, vm.GridSize);
        });
    }

    [Fact]
    public void PixelArt1024GeneratesCurrentPx2CodeAfterDrawingPauses()
    {
        RunSta(() =>
        {
            using var vm = new PixelArtViewModel(
                TestCrosshairTests.StubFor<IPresetLibrary>(),
                TestCrosshairTests.StubFor<ICustomImageStore>(),
                TestCrosshairTests.StubFor<IOverlayController>());
            vm.GridSize = 1024;
            vm.DrawLine(10, 10, 10, 10, erase: false);
            vm.FinishStroke();
            vm.DrawLine(900, 900, 900, 900, erase: false);
            vm.FinishStroke();
            Assert.Empty(vm.ShareCode);

            var frame = new DispatcherFrame();
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            var poll = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(20),
            };
            poll.Tick += (_, _) =>
            {
                if (vm.ShareCode.Length != 0 || DateTime.UtcNow >= deadline)
                    frame.Continue = false;
            };
            poll.Start();
            Dispatcher.PushFrame(frame);
            poll.Stop();

            Assert.StartsWith("PX2|1024|", vm.ShareCode);
            Assert.True(PixelArtCode.TryDecode(vm.ShareCode, out var size, out var pixels));
            Assert.Equal(1024, size);
            Assert.Equal(0xFF00FF00u, pixels[10 * size + 10]);
            Assert.Equal(0xFF00FF00u, pixels[900 * size + 900]);
        });
    }

    [Fact]
    public void PixelArtHighResolutionCanvasHidesDenseGridButKeepsCenterGuide()
    {
        RunSta(() =>
        {
            var vm = new PixelArtViewModel(
                TestCrosshairTests.StubFor<IPresetLibrary>(),
                TestCrosshairTests.StubFor<ICustomImageStore>(),
                TestCrosshairTests.StubFor<IOverlayController>());
            vm.GridSize = 1024;
            var canvas = new PixelArtCanvas
            {
                Width = 384,
                Height = 384,
                GridSize = vm.GridSize,
                ShowCenterGuide = true,
                Bitmap = vm.Bitmap,
            };
            canvas.Measure(new System.Windows.Size(384, 384));
            canvas.Arrange(new System.Windows.Rect(0, 0, 384, 384));
            var rendered = new RenderTargetBitmap(384, 384, 96, 96, PixelFormats.Pbgra32);
            rendered.Render(canvas);
            var pixels = new uint[384 * 384];
            rendered.CopyPixels(pixels, 384 * 4, 0);

            Assert.Equal(0xFF111B22u, pixels[100 * 384 + 100]);
            Assert.NotEqual(0xFF111B22u, pixels[192 * 384 + 192]);
        });
    }

    [Fact]
    public void PixelArtUndoHistoryKeepsOnlyFiftyNewestDeltaStrokes()
    {
        RunSta(() =>
        {
            var vm = new PixelArtViewModel(
                TestCrosshairTests.StubFor<IPresetLibrary>(),
                TestCrosshairTests.StubFor<ICustomImageStore>(),
                TestCrosshairTests.StubFor<IOverlayController>());
            for (var index = 0; index < 60; index++)
            {
                vm.DrawLine(index % vm.GridSize, index / vm.GridSize,
                    index % vm.GridSize, index / vm.GridSize, erase: false);
                vm.FinishStroke();
            }
            for (var index = 0; index < 50; index++) vm.UndoCommand.Execute(null);

            Assert.False(vm.UndoCommand.CanExecute(null));
            var pixels = ReadPixels(vm);
            Assert.Equal(10, pixels.Count(pixel => pixel != 0));
            Assert.All(pixels.Take(10), pixel => Assert.Equal(0xFF00FF00u, pixel));
            Assert.All(pixels.Skip(10), pixel => Assert.Equal(0u, pixel));
        });
    }

    [Fact]
    public async Task FavoritePresetSurvivesRepositorySaveAndReload()
    {
        var repository = new PresetRepository(new AppPathProvider(_root),
            TestCrosshairTests.StubFor<ICustomImageStore>(), NullLogger<PresetRepository>.Instance);
        var preset = CrosshairProfile.CreateDefault();
        preset.IsFavorite = true;

        await repository.SaveAsync(preset).WaitAsync(TimeSpan.FromSeconds(5));
        var reloaded = await repository.GetAsync(preset.Id).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.NotNull(reloaded);
        Assert.True(reloaded.IsFavorite);
    }

    private static uint[] ReadPixels(PixelArtViewModel vm)
    {
        var pixels = new uint[vm.GridSize * vm.GridSize];
        vm.Bitmap.CopyPixels(pixels, vm.GridSize * 4, 0);
        return pixels;
    }

    [Fact]
    public void ClearingShareCodeClearsCanvasAndHistoryWithoutRestartingCodeGeneration()
    {
        RunSta(() =>
        {
            using var vm = new PixelArtViewModel(
                TestCrosshairTests.StubFor<IPresetLibrary>(),
                TestCrosshairTests.StubFor<ICustomImageStore>(),
                TestCrosshairTests.StubFor<IOverlayController>());
            vm.DrawLine(2, 3, 2, 3, erase: false);
            vm.FinishStroke();
            Assert.True(vm.UndoCommand.CanExecute(null));

            vm.ShareCode = string.Empty;

            Assert.All(ReadPixels(vm), pixel => Assert.Equal(0u, pixel));
            Assert.False(vm.UndoCommand.CanExecute(null));
            Assert.False(vm.RedoCommand.CanExecute(null));
            Assert.Empty(vm.ShareCode);
        });
    }

    [Fact]
    public void ShareCodeTextBoxUsesTopAlignedTemplateAndImmediateSourceUpdates()
    {
        RunSta(() =>
        {
            _ = ThemeFixture.Load();
            using var vm = new PixelArtViewModel(
                TestCrosshairTests.StubFor<IPresetLibrary>(),
                TestCrosshairTests.StubFor<ICustomImageStore>(),
                TestCrosshairTests.StubFor<IOverlayController>());
            var tab = new PixelArtTab { DataContext = vm };
            var box = Assert.IsType<System.Windows.Controls.TextBox>(tab.FindName("ShareCodeTextBox"));
            Assert.True(box.AcceptsReturn);
            Assert.Equal(System.Windows.TextWrapping.Wrap, box.TextWrapping);
            Assert.Equal(System.Windows.VerticalAlignment.Top, box.VerticalContentAlignment);
            Assert.True(box.ApplyTemplate());
            var contentHost = Assert.IsType<System.Windows.Controls.ScrollViewer>(
                box.Template.FindName("PART_ContentHost", box));
            Assert.Equal(System.Windows.VerticalAlignment.Top, contentHost.VerticalAlignment);

            var binding = Assert.IsType<System.Windows.Data.BindingExpression>(
                box.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty));
            Assert.Equal(System.Windows.Data.UpdateSourceTrigger.PropertyChanged,
                binding.ParentBinding.UpdateSourceTrigger);
        });
    }

    [Fact]
    public void LoadingPixelCodeWithNewGridSizePublishesOneCompleteBitmap()
    {
        RunSta(() =>
        {
            var vm = new PixelArtViewModel(
                TestCrosshairTests.StubFor<IPresetLibrary>(),
                TestCrosshairTests.StubFor<ICustomImageStore>(),
                TestCrosshairTests.StubFor<IOverlayController>());
            var pixels = new uint[16 * 16];
            pixels[5 * 16 + 7] = 0xFF00FF00u;
            var bitmapNotifications = 0;
            vm.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(PixelArtViewModel.Bitmap)) bitmapNotifications++;
            };

            vm.ShareCode = PixelArtCode.Encode(16, pixels);
            vm.LoadCodeCommand.Execute(null);

            Assert.Equal(16, vm.GridSize);
            Assert.Equal(16, vm.Bitmap.PixelWidth);
            Assert.Equal(1, bitmapNotifications);
            var copied = new uint[16 * 16];
            vm.Bitmap.CopyPixels(copied, 16 * 4, 0);
            Assert.Equal(pixels, copied);
        });
    }

    [Fact]
    public void LoadCodeButtonParameterUsesPastedPx1EvenIfViewModelTextIsStale()
    {
        RunSta(() =>
        {
            using var vm = new PixelArtViewModel(
                TestCrosshairTests.StubFor<IPresetLibrary>(),
                TestCrosshairTests.StubFor<ICustomImageStore>(),
                TestCrosshairTests.StubFor<IOverlayController>());
            var pixels = new uint[32 * 32];
            pixels[5 * 32 + 7] = 0xFFFF0000u;
            var pastedCode = PixelArtCode.Encode(32, pixels);

            Assert.Empty(vm.ShareCode);
            vm.LoadCodeCommand.Execute(pastedCode);

            Assert.Equal(32, vm.GridSize);
            Assert.Equal("32", vm.GridSizeInput);
            Assert.Equal(pixels, ReadPixels(vm));
            Assert.Equal(pastedCode, vm.ShareCode);
            Assert.False(string.IsNullOrWhiteSpace(vm.CodeLoadMessage));
            Assert.False(vm.UndoCommand.CanExecute(null));
        });
    }

    [Fact]
    public void EmptyCanvasDoesNotGenerateShareCodeOnStartupResizeOrClear()
    {
        RunSta(() =>
        {
            using var vm = new PixelArtViewModel(
                TestCrosshairTests.StubFor<IPresetLibrary>(),
                TestCrosshairTests.StubFor<ICustomImageStore>(),
                TestCrosshairTests.StubFor<IOverlayController>());
            Assert.Empty(vm.ShareCode);
            vm.GridSize = 32;
            Assert.Empty(vm.ShareCode);
            vm.ClearCanvasCommand.Execute(null);
            Assert.Empty(vm.ShareCode);

            vm.DrawLine(2, 3, 2, 3, erase: false);
            vm.FinishStroke();
            Assert.StartsWith("PX1|32|", vm.ShareCode);
            vm.GridSize = 64;
            Assert.Contains(ReadPixels(vm), pixel => pixel != 0);
            Assert.StartsWith("PX1|32|", vm.ShareCode);
            vm.DrawLine(19, 18, 19, 18, erase: false);
            vm.FinishStroke();
            WaitForPixelCode(vm);
            Assert.StartsWith("PX2|64|", vm.ShareCode);
            Assert.True(PixelArtCode.TryDecode(vm.ShareCode, out var resizedSize, out var resizedPixels));
            Assert.Equal(64, resizedSize);
            Assert.Equal(0xFF00FF00u, resizedPixels[18 * resizedSize + 19]);
            vm.ClearCanvasCommand.Execute(null);
            Assert.Empty(vm.ShareCode);
            Assert.All(ReadPixels(vm), pixel => Assert.Equal(0u, pixel));
        });
    }

    [Fact]
    public void ResizingColoredPixelCanvasKeepsOldCodeAndBlocksCopyUntilNewStroke()
    {
        RunSta(() =>
        {
            using var vm = new PixelArtViewModel(
                TestCrosshairTests.StubFor<IPresetLibrary>(),
                TestCrosshairTests.StubFor<ICustomImageStore>(),
                TestCrosshairTests.StubFor<IOverlayController>());
            vm.DrawLine(12, 12, 12, 12, erase: false);
            vm.FinishStroke();
            var originalCode = vm.ShareCode;

            vm.GridSizeInput = "32";
            vm.ApplyGridSizeCommand.Execute(null);
            Assert.Equal(originalCode, vm.ShareCode);
            Assert.Equal(vm.ShareCode, vm.VisibleShareCode);
            vm.CopyCodeCommand.Execute(null);
            Assert.Equal(vm.GeneratedCodeStatus, vm.StatusMessage);
            vm.DrawLine(17, 17, 17, 17, erase: false);
            Assert.Equal(originalCode, vm.ShareCode); // No encode before stroke commit.
            vm.FinishStroke();
            Assert.StartsWith("PX1|32|", vm.ShareCode);
            Assert.True(PixelArtCode.TryDecode(vm.ShareCode, out var size, out var pixels));
            Assert.Equal(0xFF00FF00u, pixels[16 * size + 16]);

            vm.GridSizeInput = "1024";
            vm.ApplyGridSizeCommand.Execute(null);
            vm.CopyCodeCommand.Execute(null); // Copy must not encode or publish code.
            Assert.StartsWith("PX1|32|", vm.ShareCode);
            Assert.Equal(vm.GeneratedCodeStatus, vm.StatusMessage);
            vm.DrawLine(514, 514, 514, 514, erase: false);
            vm.FinishStroke();
            Assert.Empty(vm.ShareCode);
            WaitForPixelCode(vm);
            Assert.StartsWith("PX2|1024|", vm.ShareCode);
            Assert.True(PixelArtCode.TryDecode(vm.ShareCode, out size, out pixels));
            Assert.Equal(0xFF00FF00u, pixels[512 * size + 512]);
            Assert.Equal(0xFF00FF00u, pixels[514 * size + 514]);
        });
    }

    [Fact]
    public void LargeShareCodeKeepsFullModelValueWithBoundedTextboxPreview()
    {
        RunSta(() =>
        {
            using var vm = new PixelArtViewModel(
                TestCrosshairTests.StubFor<IPresetLibrary>(),
                TestCrosshairTests.StubFor<ICustomImageStore>(),
                TestCrosshairTests.StubFor<IOverlayController>());
            var pixels = new uint[256 * 256];
            for (var index = 0; index < pixels.Length; index++)
                pixels[index] = 0xFF000000u | (uint)(index * 2654435761u & 0xFFFFFFu);
            var fullCode = PixelArtCode.Encode(256, pixels);
            Assert.True(fullCode.Length > 32_768);

            vm.ShareCode = fullCode;

            Assert.Equal(fullCode, vm.ShareCode);
            Assert.StartsWith("PX2|256|", vm.VisibleShareCode);
            Assert.True(vm.VisibleShareCode.Length < 4_200);
        });
    }

    private static void WaitForPixelCode(PixelArtViewModel vm)
    {
        var frame = new DispatcherFrame();
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        var poll = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(20),
        };
        poll.Tick += (_, _) =>
        {
            if (vm.ShareCode.Length != 0 || DateTime.UtcNow >= deadline)
                frame.Continue = false;
        };
        poll.Start();
        Dispatcher.PushFrame(frame);
        poll.Stop();
        Assert.NotEmpty(vm.ShareCode);
    }

    [Fact]
    public void InvalidPastedCodeReportsAnErrorWithoutReplacingBitmap()
    {
        RunSta(() =>
        {
            using var vm = new PixelArtViewModel(
                TestCrosshairTests.StubFor<IPresetLibrary>(),
                TestCrosshairTests.StubFor<ICustomImageStore>(),
                TestCrosshairTests.StubFor<IOverlayController>());
            var original = vm.Bitmap;
            vm.LoadCodeCommand.Execute("PX1|24|invalid-base64");

            Assert.Same(original, vm.Bitmap);
            Assert.False(string.IsNullOrWhiteSpace(vm.CodeLoadMessage));
        });
    }

    [Fact]
    public void PresetCodeInPixelEditorExplainsWrongCodeType()
    {
        RunSta(() =>
        {
            using var vm = new PixelArtViewModel(
                TestCrosshairTests.StubFor<IPresetLibrary>(),
                TestCrosshairTests.StubFor<ICustomImageStore>(),
                TestCrosshairTests.StubFor<IOverlayController>());
            var original = vm.Bitmap;
            vm.LoadCodeCommand.Execute("CGO-CH;AAAA;c00000000");

            Assert.Same(original, vm.Bitmap);
            Assert.Contains("PX1/PX2", vm.CodeLoadMessage);
        });
    }

    [Fact]
    public void HeaderlessBase64InPixelEditorExplainsMissingPrefix()
    {
        RunSta(() =>
        {
            using var vm = new PixelArtViewModel(
                TestCrosshairTests.StubFor<IPresetLibrary>(),
                TestCrosshairTests.StubFor<ICustomImageStore>(),
                TestCrosshairTests.StubFor<IOverlayController>());
            vm.LoadCodeCommand.Execute("AQAAAAAA;c00000000");
            Assert.Contains("PX1|", vm.CodeLoadMessage);
            Assert.Contains("PX2|", vm.CodeLoadMessage);
        });
    }

    [Theory]
    [InlineData(24, "PX1|")]
    [InlineData(64, "PX2|")]
    public void StoredSquarePngExportsPixelEditorCode(int size, string header)
    {
        RunSta(() =>
        {
            var pixels = new uint[size * size];
            pixels[7 * size + 8] = 0xFF00FF00u;
            var bitmap = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null,
                pixels, size * sizeof(uint));
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = new MemoryStream();
            encoder.Save(stream);

            Assert.True(PixelArtImageCodeExporter.TryEncodePng(stream.ToArray(), out var code, out var exportedSize));
            Assert.Equal(size, exportedSize);
            Assert.StartsWith(header + size + "|", code);
            Assert.True(PixelArtCode.TryDecode(code, out var decodedSize, out var decodedPixels));
            Assert.Equal(size, decodedSize);
            Assert.Equal(pixels, decodedPixels);
        });
    }

    [Fact]
    public void Truncated1024PixelCodeCanUseMatchingFullClipboardText()
    {
        var pixels = new uint[1024 * 1024];
        for (var index = 0; index < pixels.Length; index++)
            pixels[index] = 0xFF000000u | (unchecked((uint)index * 2654435761u) & 0x00FFFFFFu);
        var fullCode = PixelArtCode.Encode(1024, pixels);
        Assert.True(fullCode.Length > 32_768);
        var visible = fullCode[..32_768];

        var selected = PixelArtViewModel.PreferFullClipboardCode(visible, fullCode);
        Assert.Same(fullCode, selected);
        Assert.True(PixelArtCode.TryDecode(selected, out var size, out var decoded));
        Assert.Equal(1024, size);
        Assert.Equal(pixels[0], decoded[0]);
        Assert.Equal(pixels[^1], decoded[^1]);
        Assert.Equal(visible, PixelArtViewModel.PreferFullClipboardCode(visible, "PX2|1024|unrelated"));
    }

    [Fact]
    public void SignedPx2CodeLoadsIntoResizedBitmap()
    {
        RunSta(() =>
        {
            using var vm = new PixelArtViewModel(
                TestCrosshairTests.StubFor<IPresetLibrary>(),
                TestCrosshairTests.StubFor<ICustomImageStore>(),
                TestCrosshairTests.StubFor<IOverlayController>());
            var pixels = new uint[64 * 64];
            pixels[31 * 64 + 32] = 0xFF00FF00u;
            var code = ShareCodeChecksum.Append(PixelArtCode.Encode(64, pixels), PixelArtCode.CompressedPrefix);

            vm.LoadCodeCommand.Execute(code);

            Assert.Equal(64, vm.GridSize);
            Assert.Equal(pixels, ReadPixels(vm));
            Assert.Equal(code, vm.ShareCode);
        });
    }

    [Fact]
    public void PerformanceMonitorSamplesOnlyWhileStartedAndCanBeDisposed()
    {
        RunSta(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            using var monitor = new PerformanceMonitorService(dispatcher);
            var frame = new DispatcherFrame();
            monitor.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(PerformanceMonitorService.MemoryValue))
                    frame.Continue = false;
            };
            using var timeout = new System.Threading.Timer(state =>
            {
                _ = dispatcher.BeginInvoke(new Action(() => frame.Continue = false));
            },
                null, TimeSpan.FromSeconds(5), Timeout.InfiniteTimeSpan);

            monitor.Start();
            monitor.Start(); // Repeated activation must not create another sampler.
            Dispatcher.PushFrame(frame);
            Assert.EndsWith(" MB", monitor.MemoryValue);

            monitor.Stop();
            var stoppedValue = monitor.MemoryValue;
            Assert.Equal("…", monitor.CpuValue);
            monitor.Dispose();
            monitor.Start();
            Assert.Equal(stoppedValue, monitor.MemoryValue);
        });
    }

    [Fact]
    public async Task HiddenSettingsReusesFrozenBackgroundAfterIntermediateCacheExpires()
    {
        var path = Path.Combine(_root, "tray-background.png");
        var source = BitmapSource.Create(4, 4, 96, 96, PixelFormats.Bgra32,
            null, new byte[4 * 4 * 4], 16);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using (var stream = File.Create(path)) encoder.Save(stream);

        var settings = new OverlayBehaviorTests.SettingsStub();
        settings.Current.CustomAppBgPath = path;
        var monitors = TestCrosshairTests.StubFor<IMonitorService>();
        using var vm = new GeneralSettingsViewModel(settings, monitors,
            new CrosshairOverlay.Services.Display.DisplayAnalyzerService(monitors,
                TestCrosshairTests.StubFor<IDisplayModeReader>()),
            TestCrosshairTests.StubFor<ITrayIconController>(),
            TestCrosshairTests.StubFor<IOverlayController>(),
            TestCrosshairTests.StubFor<IStartupService>(),
            TestCrosshairTests.StubFor<IUpdateService>(),
            TestCrosshairTests.StubFor<IDialogService>(), new AppPathProvider(_root),
            TestCrosshairTests.StubFor<IProcessLauncher>(),
            TestCrosshairTests.StubFor<IAppRestartService>(),
            TestCrosshairTests.StubFor<IStorageLocationService>(), new ToastViewModel());

        await vm.PrepareBackgroundForShowAsync();
        var processed = Assert.IsAssignableFrom<BitmapSource>(vm.AppBackgroundImage);
        Assert.True(processed.IsFrozen);
        vm.SetBackgroundViewActive(false);
        vm.ExpireBackgroundSourceCache(); // The same action as the 30-second idle timer.

        using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            await vm.PrepareBackgroundForShowAsync();
            Assert.Same(processed, vm.AppBackgroundImage);
        }
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
        thread.IsBackground = true;
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "STA test exceeded its 10-second limit.");
        if (error is not null) throw new Xunit.Sdk.XunitException(error.ToString());
    }
}
