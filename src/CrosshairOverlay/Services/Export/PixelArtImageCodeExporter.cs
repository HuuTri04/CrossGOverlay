using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CrosshairOverlay.Services.Import;
using CrosshairOverlay.Services.Storage;

namespace CrosshairOverlay.Services.Export;

/// <summary>Turns the stored square PNG pixels into a code accepted by the Pixel Art editor.</summary>
public static class PixelArtImageCodeExporter
{
    public static bool TryEncodePng(byte[] png, out string? code, out int size)
    {
        code = null;
        size = 0;
        if (png.Length == 0 || png.LongLength > CustomImageStore.MaxFileBytes) return false;

        using var stream = new MemoryStream(png, writable: false);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        if (decoder.Frames.Count == 0) return false;
        var frame = decoder.Frames[0];
        if (frame.PixelWidth != frame.PixelHeight || !PixelArtCode.IsSupportedSize(frame.PixelWidth))
            return false;

        size = frame.PixelWidth;
        var pixels = new uint[size * size];
        BitmapSource source = frame.Format == PixelFormats.Bgra32
            ? frame
            : new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        source.CopyPixels(pixels, size * sizeof(uint), 0);
        code = PixelArtCode.Encode(size, pixels);
        return true;
    }
}
