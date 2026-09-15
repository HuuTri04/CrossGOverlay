using System.IO;
using System.Windows.Media.Imaging;

namespace CrosshairOverlay.Services.Storage;

internal enum ShrinkOutcome
{
    /// <summary>Ảnh đã đủ nhỏ; nội dung trả về chính là nội dung đưa vào.</summary>
    Unchanged,

    /// <summary>Đã thu nhỏ và mã hoá lại.</summary>
    Resized,

    /// <summary>GIF động: giữ nguyên để không mất chuyển động.</summary>
    AnimatedKept,

    /// <summary>Không giải mã được hoặc vượt giới hạn giải mã; để nơi gọi báo lỗi đúng cách.</summary>
    Rejected,
}

/// <param name="Width">Kích thước của <see cref="Bytes"/> (0 khi <see cref="ShrinkOutcome.Rejected"/>).</param>
internal sealed record ShrinkResult(
    string FileName, byte[] Bytes, int OriginalWidth, int OriginalHeight, int Width, int Height, ShrinkOutcome Outcome);

/// <summary>
/// Thu nhỏ một file ảnh (giữ tỷ lệ) và mã hoá lại. Dùng chung cho kho ảnh (thu về 256px khi chọn
/// ảnh) và mã chia sẻ (thu nhỏ ảnh lớn để mã ngắn lại).
/// </summary>
internal static class ImageShrinker
{
    public static ShrinkResult Shrink(string fileName, byte[] bytes, int maxSide)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (maxSide < 1) throw new ArgumentOutOfRangeException(nameof(maxSide));

        BitmapDecoder decoder;
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException
                                       or ArgumentException or InvalidOperationException
                                       or IOException or OverflowException)
        {
            return new ShrinkResult(fileName, bytes, 0, 0, 0, 0, ShrinkOutcome.Rejected);
        }

        var frame = decoder.Frames[0];
        var (w, h) = (frame.PixelWidth, frame.PixelHeight);
        var longest = Math.Max(w, h);

        if (w <= 0 || h <= 0 || longest > CustomImageStore.MaxPixelSide)
            return new ShrinkResult(fileName, bytes, w, h, 0, 0, ShrinkOutcome.Rejected);

        if (longest <= maxSide) return new ShrinkResult(fileName, bytes, w, h, w, h, ShrinkOutcome.Unchanged);

        if (decoder.Frames.Count > 1) return new ShrinkResult(fileName, bytes, w, h, w, h, ShrinkOutcome.AnimatedKept);

        var (width, height) = FitWithin(w, h, maxSide);
        var resized = ImageResampler.Downscale(frame, width, height);

        var isJpeg = decoder is JpegBitmapDecoder;
        BitmapEncoder encoder = isJpeg
            ? new JpegBitmapEncoder { QualityLevel = 95 }
            : new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(resized));

        using var output = new MemoryStream();
        encoder.Save(output);

        // Đuôi theo định dạng THẬT vừa ghi. GIF tĩnh lưu thành PNG: đủ 256 mức trong suốt, không
        // bị ép về bảng 256 màu lần nữa.
        var extension = Path.GetExtension(fileName);
        var name = isJpeg
            ? (extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
                ? fileName
                : Path.ChangeExtension(fileName, ".jpg"))
            : Path.ChangeExtension(fileName, ".png");

        return new ShrinkResult(name, output.ToArray(), w, h, width, height, ShrinkOutcome.Resized);
    }

    /// <summary>Kích thước mới giữ tỷ lệ, cạnh lớn nhất bằng <paramref name="maxSide"/>, mỗi cạnh ít nhất 1.</summary>
    public static (int Width, int Height) FitWithin(int width, int height, int maxSide)
    {
        if (width >= height)
            return (maxSide, Math.Max(1, (int)Math.Round((double)height * maxSide / width)));

        return (Math.Max(1, (int)Math.Round((double)width * maxSide / height)), maxSide);
    }
}
