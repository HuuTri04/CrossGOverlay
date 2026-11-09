using System.Buffers;
using System.Diagnostics;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CrosshairOverlay.Services.System;

/// <summary>Precomputes a two-pass box blur on premultiplied pixels; no WPF effect runs while scrolling.</summary>
internal static class ImageBlurHelper
{
    public static Task<BitmapSource> CreatePreBlurredBitmapAsync(
        BitmapSource source, double blurRadius, CancellationToken cancellationToken = default,
        Action<long, long>? timing = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.IsFrozen)
            throw new ArgumentException("Background source must be frozen before background processing.", nameof(source));

        return Task.Run(() =>
        {
            var startedAt = timing is null ? 0 : Stopwatch.GetTimestamp();
            var result = Blur(source, blurRadius, cancellationToken);
            timing?.Invoke(startedAt, Stopwatch.GetTimestamp());
            return result;
        }, cancellationToken);
    }

    private static BitmapSource Blur(BitmapSource source, double blurRadius, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var radius = double.IsFinite(blurRadius) ? (int)Math.Round(Math.Clamp(blurRadius, 0d, 30d)) : 0;
        if (radius == 0) return source;

        var width = source.PixelWidth;
        var height = source.PixelHeight;
        var stride = checked(width * 4);
        var length = checked(stride * height);

        var converted = new FormatConvertedBitmap(source, PixelFormats.Pbgra32, null, 0);
        converted.Freeze();
        byte[]? input = null;
        byte[]? horizontal = null;
        byte[]? output = null;
        try
        {
            input = ArrayPool<byte>.Shared.Rent(length);
            horizontal = ArrayPool<byte>.Shared.Rent(length);
            output = ArrayPool<byte>.Shared.Rent(length);
            converted.CopyPixels(input, stride, 0);
            cancellationToken.ThrowIfCancellationRequested();

            // Sliding sums keep work linear in pixel count, independent of blur radius.
            var divisor = radius * 2 + 1;
            Span<int> sums = stackalloc int[4];
            for (var y = 0; y < height; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var row = y * stride;
                sums.Clear();
                for (var k = -radius; k <= radius; k++)
                    AddPixel(input, row + Math.Clamp(k, 0, width - 1) * 4, sums, 1);

                for (var x = 0; x < width; x++)
                {
                    var offset = row + x * 4;
                    for (var channel = 0; channel < 4; channel++)
                        horizontal[offset + channel] = (byte)(sums[channel] / divisor);
                    AddPixel(input, row + Math.Clamp(x - radius, 0, width - 1) * 4, sums, -1);
                    AddPixel(input, row + Math.Clamp(x + radius + 1, 0, width - 1) * 4, sums, 1);
                }
            }

            for (var x = 0; x < width; x++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                sums.Clear();
                for (var k = -radius; k <= radius; k++)
                    AddPixel(horizontal, Math.Clamp(k, 0, height - 1) * stride + x * 4, sums, 1);

                for (var y = 0; y < height; y++)
                {
                    var offset = y * stride + x * 4;
                    for (var channel = 0; channel < 4; channel++)
                        output[offset + channel] = (byte)(sums[channel] / divisor);
                    AddPixel(horizontal, Math.Clamp(y - radius, 0, height - 1) * stride + x * 4, sums, -1);
                    AddPixel(horizontal, Math.Clamp(y + radius + 1, 0, height - 1) * stride + x * 4, sums, 1);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            // BitmapSource.Create copies the pixel bytes; the frozen bitmap never owns a rented array.
            var result = BitmapSource.Create(width, height, source.DpiX, source.DpiY,
                PixelFormats.Pbgra32, null, output, stride);
            result.Freeze();
            return result;
        }
        finally
        {
            if (output is not null) ArrayPool<byte>.Shared.Return(output);
            if (horizontal is not null) ArrayPool<byte>.Shared.Return(horizontal);
            if (input is not null) ArrayPool<byte>.Shared.Return(input);
        }
    }

    private static void AddPixel(byte[] pixels, int offset, Span<int> sums, int sign)
    {
        for (var channel = 0; channel < 4; channel++)
            sums[channel] += pixels[offset + channel] * sign;
    }
}
