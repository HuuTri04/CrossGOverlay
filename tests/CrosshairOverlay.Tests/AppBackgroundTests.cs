using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CrosshairOverlay.Controls;
using CrosshairOverlay.Core.Models;
using CrosshairOverlay.Services.Storage;
using CrosshairOverlay.Services.System;
using Xunit;

namespace CrosshairOverlay.Tests;

public sealed class AppBackgroundTests
{
    [Fact]
    public void Settings_KeepDefaultsForOlderJson_AndClampInvalidValues()
    {
        var old = JsonSerializer.Deserialize<AppSettings>("{}", AppJson.Options)!;
        Assert.Null(old.CustomAppBgPath);
        Assert.Equal(AppSettings.DefaultAppBgOpacity, old.AppBgOpacity);
        Assert.Equal(AppSettings.DefaultAppBgBlurRadius, old.AppBgBlurRadius);

        old.AppBgOpacity = 2d;
        old.AppBgBlurRadius = -10d;
        Assert.Equal(1d, old.AppBgOpacity);
        Assert.Equal(0d, old.AppBgBlurRadius);

        old.AppBgOpacity = double.NaN;
        old.AppBgBlurRadius = double.PositiveInfinity;
        Assert.Equal(AppSettings.DefaultAppBgOpacity, old.AppBgOpacity);
        Assert.Equal(AppSettings.DefaultAppBgBlurRadius, old.AppBgBlurRadius);
    }

    [Fact]
    public void Settings_RoundTripImagePathOpacityAndBlur()
    {
        var settings = new AppSettings
        {
            CustomAppBgPath = @"C:\Images\wall.png",
            AppBgOpacity = 0.42d,
            AppBgBlurRadius = 14d,
        };
        var saved = JsonSerializer.Deserialize<AppSettings>(
            JsonSerializer.Serialize(settings, AppJson.Options), AppJson.Options)!;

        Assert.Equal(settings.CustomAppBgPath, saved.CustomAppBgPath);
        Assert.Equal(0.42d, saved.AppBgOpacity);
        Assert.Equal(14d, saved.AppBgBlurRadius);
    }

    [Fact]
    public void Loader_DecodesFrozenImage_AndReleasesSourceFile()
    {
        var path = Path.Combine(Path.GetTempPath(), "CrossGOverlay-background-" + Guid.NewGuid() + ".png");
        try
        {
            var pixels = new byte[4 * 4 * 4];
            var source = BitmapSource.Create(4, 4, 96, 96, PixelFormats.Bgra32, null, pixels, 16);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            using (var stream = File.Create(path)) encoder.Save(stream);

            var image = AppBackgroundImageLoader.Decode(path);
            Assert.True(image.IsFrozen);
            Assert.Equal(4, image.PixelWidth);
            using var exclusive = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Loader_DownsamplesTallImagesToBoundMemory()
    {
        var path = Path.Combine(Path.GetTempPath(), "CrossGOverlay-background-" + Guid.NewGuid() + ".png");
        try
        {
            var source = BitmapSource.Create(2, 3000, 96, 96, PixelFormats.Bgra32,
                null, new byte[2 * 3000 * 4], 8);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            using (var stream = File.Create(path)) encoder.Save(stream);

            var image = AppBackgroundImageLoader.Decode(path);
            Assert.InRange(image.PixelHeight, 1, 1920);
            Assert.True(image.IsFrozen);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task LoaderAsync_Downsamples4KImage_AndReleasesFile()
    {
        var path = Path.Combine(Path.GetTempPath(), "CrossGOverlay-background-" + Guid.NewGuid() + ".png");
        try
        {
            const int width = 3840;
            const int height = 2160;
            var source = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32,
                null, new byte[width * height * 4], width * 4);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            using (var stream = File.Create(path)) encoder.Save(stream);

            var image = await AppBackgroundImageLoader.DecodeAsync(path);
            Assert.Equal(1920, image.PixelWidth);
            Assert.Equal(1080, image.PixelHeight);
            Assert.True(image.IsFrozen);
            var preview = await AppBackgroundImageLoader.DecodeAsync(path, maxDecodedSide: 1280);
            Assert.Equal(1280, preview.PixelWidth);
            Assert.Equal(720, preview.PixelHeight);
            Assert.True(preview.IsFrozen);
            using var exclusive = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task LoaderAsync_HonorsCancellationBeforeOpeningFile()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            AppBackgroundImageLoader.DecodeAsync("missing.png", cancellationToken: cancellation.Token));
    }

    [Fact]
    public async Task ProcessedBackgroundCache_RoundTripsFrozenBitmap_AndInvalidatesOnInputs()
    {
        var root = Path.Combine(Path.GetTempPath(), "CrossGOverlay-cache-" + Guid.NewGuid());
        var logs = Path.Combine(root, "logs");
        var sourcePath = Path.Combine(root, "source.png");
        var changedAt = new DateTime(2026, 11, 3, 0, 0, 0, DateTimeKind.Utc);
        var cachePath = AppBackgroundCache.CachePath(logs, sourcePath, 100, changedAt, 10, 0.18);
        try
        {
            Assert.NotEqual(cachePath, AppBackgroundCache.CachePath(logs, sourcePath, 101, changedAt, 10, 0.18));
            Assert.NotEqual(cachePath, AppBackgroundCache.CachePath(logs, sourcePath, 100, changedAt.AddSeconds(1), 10, 0.18));
            Assert.NotEqual(cachePath, AppBackgroundCache.CachePath(logs, sourcePath, 100, changedAt, 11, 0.18));
            Assert.NotEqual(cachePath, AppBackgroundCache.CachePath(logs, sourcePath, 100, changedAt, 10, 0.19));

            var pixels = new byte[] { 0, 0, 255, 255, 0, 255, 0, 255,
                                      255, 0, 0, 255, 255, 255, 255, 255 };
            var image = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null, pixels, 8);
            image.Freeze();
            await AppBackgroundCache.SaveAsync(cachePath, image, CancellationToken.None);
            var loaded = await AppBackgroundImageLoader.DecodeAsync(cachePath);
            Assert.True(loaded.IsFrozen);
            Assert.Equal((2, 2), (loaded.PixelWidth, loaded.PixelHeight));
            using var exclusive = File.Open(cachePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void PreviewDoesNotPaintOverTheSeparatePhotoLayer()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var photo = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32,
                    null, new byte[] { 0, 0, 255, 255 }, 4);
                photo.Freeze();
                var preview = new CrosshairPreview { CustomBackgroundImage = photo };
                preview.Measure(new Size(32, 32));
                preview.Arrange(new Rect(0, 0, 32, 32));
                var rendered = new RenderTargetBitmap(32, 32, 96, 96, PixelFormats.Pbgra32);
                rendered.Render(preview);
                var pixels = new byte[32 * 32 * 4];
                rendered.CopyPixels(pixels, 32 * 4, 0);
                Assert.Equal(0, pixels[(2 * 32 + 2) * 4 + 3]);
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "Test worker exceeded its 10-second limit.");
        if (failure is not null) throw failure;
    }

    [Fact]
    public async Task PreBlur_RunsOffThread_AndReturnsFrozenBitmap()
    {
        var pixels = new byte[3 * 4];
        pixels[4] = pixels[5] = pixels[6] = pixels[7] = 255;
        var source = BitmapSource.Create(3, 1, 96, 96, PixelFormats.Pbgra32, null, pixels, 12);
        source.Freeze();

        var blurred = await ImageBlurHelper.CreatePreBlurredBitmapAsync(source, 1);
        var result = new byte[12];
        blurred.CopyPixels(result, 12, 0);

        Assert.True(blurred.IsFrozen);
        Assert.NotSame(source, blurred);
        Assert.Equal(85, result[7]);
        Assert.Same(source, await ImageBlurHelper.CreatePreBlurredBitmapAsync(source, 0));
    }

    [Fact]
    public async Task PreBlur_RespectsCancellation()
    {
        var source = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Pbgra32,
            null, new byte[4], 4);
        source.Freeze();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ImageBlurHelper.CreatePreBlurredBitmapAsync(source, 10, cancellation.Token));
    }

    [Fact]
    public async Task PreBlur_ResultKeepsPixelsAfterPooledBuffersAreReused()
    {
        var pixels = new byte[64 * 64 * 4];
        for (var i = 0; i < pixels.Length; i++) pixels[i] = (byte)(i % 251);
        var source = BitmapSource.Create(64, 64, 96, 96, PixelFormats.Pbgra32,
            null, pixels, 64 * 4);
        source.Freeze();

        var first = await ImageBlurHelper.CreatePreBlurredBitmapAsync(source, 7);
        var expected = new byte[pixels.Length];
        first.CopyPixels(expected, 64 * 4, 0);
        for (var i = 0; i < 12; i++)
            await ImageBlurHelper.CreatePreBlurredBitmapAsync(source, 1 + i);
        var actual = new byte[pixels.Length];
        first.CopyPixels(actual, 64 * 4, 0);

        Assert.True(first.IsFrozen);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void SliderField_DisplaysAndParsesPercentAndPixelSuffixes()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                _ = ThemeFixture.Load();
                var slider = new SliderField { UnitSuffix = "%", Value = 18d };
                Assert.Equal("18%", slider.Text);
                slider.Text = "25%";
                Assert.Equal(25d, slider.Value);
                slider.UnitSuffix = "px";
                Assert.Equal("25px", slider.Text);
                slider.Text = "10px";
                Assert.Equal(10d, slider.Value);
            }
            catch (Exception ex) { failure = ex; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "Test worker exceeded its 10-second limit.");
        if (failure is not null) throw new Xunit.Sdk.XunitException(failure.ToString());
    }

    [Fact]
    public void SliderField_ReleaseCommitsLastDelayedValue()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                _ = ThemeFixture.Load();
                var model = new SliderValueModel { Value = 10d };
                var field = new SliderField { Range = new(0d, 100d) };
                BindingOperations.SetBinding(field, SliderField.ValueProperty,
                    new Binding(nameof(SliderValueModel.Value))
                    {
                        Source = model,
                        Mode = BindingMode.TwoWay,
                        UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
                    });
                var slider = Assert.IsType<Slider>(field.FindName("TrackSlider"));
                slider.Value = 42d;
                slider.RaiseEvent(new DragCompletedEventArgs(0d, 0d, false)
                {
                    RoutedEvent = Thumb.DragCompletedEvent,
                });

                Assert.Equal(42d, model.Value);
            }
            catch (Exception ex) { failure = ex; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "Test worker exceeded its 10-second limit.");
        if (failure is not null) throw new Xunit.Sdk.XunitException(failure.ToString());
    }

    [Fact]
    public void ColorPicker_ReleaseCommitsLatestRgbWithoutWaitingForTimer()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                _ = ThemeFixture.Load();
                var picker = new ColorPickerButton();
                var red = Assert.IsType<Slider>(picker.FindName("RedSlider"));
                var green = Assert.IsType<Slider>(picker.FindName("GreenSlider"));
                var blue = Assert.IsType<Slider>(picker.FindName("BlueSlider"));
                red.Value = 20d;
                green.Value = 30d;
                blue.Value = 40d;

                Assert.Equal(Colors.Lime, picker.SelectedColor);
                blue.RaiseEvent(new DragCompletedEventArgs(0d, 0d, false)
                {
                    RoutedEvent = Thumb.DragCompletedEvent,
                });

                Assert.Equal(Color.FromRgb(20, 30, 40), picker.SelectedColor);
            }
            catch (Exception ex) { failure = ex; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "Test worker exceeded its 10-second limit.");
        if (failure is not null) throw new Xunit.Sdk.XunitException(failure.ToString());
    }

    private sealed class SliderValueModel
    {
        public double Value { get; set; }
    }
}
