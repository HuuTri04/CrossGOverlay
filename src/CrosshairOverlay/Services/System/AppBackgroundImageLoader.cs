using System.IO;
using System.Windows.Media.Imaging;

namespace CrosshairOverlay.Services.System;

/// <summary>Decodes one Settings backdrop at a bounded size and releases its source file.</summary>
internal static class AppBackgroundImageLoader
{
    private const long MaxSourceBytes = 50L * 1024 * 1024;
    private const int MaxDecodedSide = 1920;
    private static readonly HashSet<string> Extensions =
        new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".webp" };

    public static Task<BitmapSource> DecodeAsync(
        string path, int maxDecodedSide = MaxDecodedSide, CancellationToken cancellationToken = default) =>
        Task.Run(() => Decode(path, maxDecodedSide, cancellationToken), cancellationToken);

    public static BitmapSource Decode(
        string path, int maxDecodedSide = MaxDecodedSide, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (maxDecodedSide is < 1 or > MaxDecodedSide)
            throw new ArgumentOutOfRangeException(nameof(maxDecodedSide));
        if (!Extensions.Contains(Path.GetExtension(path)))
            throw new NotSupportedException("Unsupported background image format.");

        var file = new FileInfo(path);
        if (!file.Exists) throw new FileNotFoundException("Background image is missing.", path);
        if (file.Length > MaxSourceBytes)
            throw new InvalidDataException("Background image exceeds 50 MB.");

        int width, height;
        using (var probe = new FileStream(path, FileMode.Open, FileAccess.Read,
                   FileShare.ReadWrite | FileShare.Delete))
        {
            var decoder = BitmapDecoder.Create(probe, BitmapCreateOptions.DelayCreation,
                BitmapCacheOption.None);
            width = decoder.Frames[0].PixelWidth;
            height = decoder.Frames[0].PixelHeight;
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (width <= 0 || height <= 0 || width > 16384 || height > 16384)
            throw new InvalidDataException("Background image dimensions are unsupported.");

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        if (width >= height && width > maxDecodedSide) image.DecodePixelWidth = maxDecodedSide;
        else if (height > maxDecodedSide) image.DecodePixelHeight = maxDecodedSide;
        image.StreamSource = stream;
        image.EndInit();
        cancellationToken.ThrowIfCancellationRequested();
        image.Freeze();
        return image;
    }
}
