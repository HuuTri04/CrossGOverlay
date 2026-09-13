using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CrosshairOverlay.Services.Rendering;

/// <summary>Các khung hình đã ghép hoàn chỉnh của một GIF động, kèm thời lượng từng khung.</summary>
internal sealed record AnimatedImage(IReadOnlyList<BitmapSource> Frames, IReadOnlyList<TimeSpan> Delays)
{
    public TimeSpan Duration { get; } = Delays.Aggregate(TimeSpan.Zero, (sum, d) => sum + d);
}

/// <summary>
/// Giải mã GIF động thành chuỗi khung hình đầy đủ.
/// </summary>
/// <remarks>
/// <c>GifBitmapDecoder</c> trả về khung THÔ: mỗi khung thường chỉ là một mảnh nhỏ (vùng thay
/// đổi so với khung trước) đặt ở toạ độ riêng, kèm quy tắc xử lý sau khi hiện. Hiện thẳng các
/// khung thô sẽ ra hình nhấp nháy, lệch chỗ và mất nền — nên phải tự ghép lên một canvas theo
/// đúng chuẩn GIF89a trước.
/// </remarks>
internal static class GifAnimation
{
    /// <summary>
    /// Tổng bộ nhớ tối đa cho mọi khung đã ghép. Mỗi khung là một ảnh đầy đủ 4 byte/pixel giữ
    /// trong RAM suốt phiên: GIF 512×512 dài 250 khung đã là 256 MB. Vượt mức thì dùng khung đầu.
    /// </summary>
    public const long MaxDecodedBytes = 96L * 1024 * 1024;

    public const int MaxFrames = 600;

    /// <summary>
    /// Trình duyệt coi delay 0 hoặc 1 phần trăm giây là "chạy nhanh nhất có thể" và ép về 100 ms,
    /// vì nhiều GIF cũ ghi sai. Làm theo, nếu không những GIF đó sẽ quay tít như chong chóng.
    /// </summary>
    private static readonly TimeSpan MinimumDelay = TimeSpan.FromMilliseconds(100);

    /// <summary>Cách xử lý vùng khung sau khi hiện, theo chuẩn GIF89a.</summary>
    internal enum Disposal
    {
        /// <summary>0/1: giữ nguyên, khung sau vẽ đè lên.</summary>
        Keep = 1,

        /// <summary>2: xoá vùng khung về trong suốt.</summary>
        RestoreBackground = 2,

        /// <summary>3: trả vùng về trạng thái TRƯỚC khi vẽ khung này.</summary>
        RestorePrevious = 3,
    }

    /// <summary>Một khung thô: điểm ảnh BGRA của riêng vùng khung, vị trí và quy tắc.</summary>
    internal readonly record struct RawFrame(
        byte[] Pixels, int Left, int Top, int Width, int Height, Disposal Disposal, TimeSpan Delay);

    /// <summary>
    /// Thử đọc GIF động. Trả null nếu không phải GIF, chỉ có một khung, hoặc vượt giới hạn bộ
    /// nhớ — khi đó bên gọi dùng ảnh tĩnh như bình thường.
    /// </summary>
    public static AnimatedImage? TryLoad(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var decoder = new GifBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);

            var count = decoder.Frames.Count;
            if (count < 2 || count > MaxFrames) return null;

            var (width, height) = CanvasSize(decoder);
            if ((long)width * height * 4 * count > MaxDecodedBytes) return null;

            var raw = new List<RawFrame>(count);
            foreach (var frame in decoder.Frames) raw.Add(ReadRaw(frame));

            var composed = Compose(width, height, raw);
            var frames = new List<BitmapSource>(composed.Count);

            foreach (var pixels in composed)
            {
                var bitmap = BitmapSource.Create(width, height, 96d, 96d, PixelFormats.Bgra32, null, pixels, width * 4);
                bitmap.Freeze();
                frames.Add(bitmap);
            }

            return new AnimatedImage(frames, raw.Select(f => f.Delay).ToList());
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or FileFormatException
                                       or ArgumentException or InvalidOperationException
                                       or UnauthorizedAccessException or OverflowException)
        {
            return null;
        }
    }

    /// <summary>
    /// Ghép các khung thô thành các khung hoàn chỉnh cỡ canvas.
    /// </summary>
    /// <remarks>
    /// Tách riêng, không phụ thuộc WPF, để kiểm thử được từng quy tắc xử lý khung bằng dữ liệu
    /// tổng hợp — thứ mà bộ mã hoá GIF của WPF không tạo ra được.
    /// </remarks>
    internal static IReadOnlyList<byte[]> Compose(int width, int height, IReadOnlyList<RawFrame> frames)
    {
        var canvas = new byte[width * height * 4];
        var output = new List<byte[]>(frames.Count);

        foreach (var frame in frames)
        {
            var previous = frame.Disposal == Disposal.RestorePrevious ? (byte[])canvas.Clone() : null;

            ForEachPixel(width, height, frame, (src, dst) =>
            {
                // Độ trong suốt của GIF chỉ có bật hoặc tắt: pixel trong suốt thì để lộ canvas bên dưới.
                if (frame.Pixels[src + 3] == 0) return;
                Buffer.BlockCopy(frame.Pixels, src, canvas, dst, 4);
            });

            output.Add((byte[])canvas.Clone());

            switch (frame.Disposal)
            {
                case Disposal.RestoreBackground:
                    ForEachPixel(width, height, frame, (_, dst) => Array.Clear(canvas, dst, 4));
                    break;

                case Disposal.RestorePrevious:
                    canvas = previous!;
                    break;
            }
        }

        return output;
    }

    /// <summary>Duyệt các pixel của khung nằm TRONG canvas; khung tràn ra ngoài bị cắt.</summary>
    private static void ForEachPixel(int width, int height, RawFrame frame, Action<int, int> action)
    {
        for (var y = 0; y < frame.Height; y++)
        {
            var cy = frame.Top + y;
            if (cy < 0 || cy >= height) continue;

            for (var x = 0; x < frame.Width; x++)
            {
                var cx = frame.Left + x;
                if (cx < 0 || cx >= width) continue;

                action(((y * frame.Width) + x) * 4, ((cy * width) + cx) * 4);
            }
        }
    }

    private static (int Width, int Height) CanvasSize(BitmapDecoder decoder)
    {
        var first = decoder.Frames[0];
        var width = first.PixelWidth;
        var height = first.PixelHeight;

        if (decoder.Metadata is BitmapMetadata meta)
        {
            if (Query(meta, "/logscrdesc/Width") is { } w and > 0) width = w;
            if (Query(meta, "/logscrdesc/Height") is { } h and > 0) height = h;
        }

        return (width, height);
    }

    private static RawFrame ReadRaw(BitmapFrame frame)
    {
        var width = frame.PixelWidth;
        var height = frame.PixelHeight;

        var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[width * height * 4];
        converted.CopyPixels(pixels, width * 4, 0);

        var meta = frame.Metadata as BitmapMetadata;
        var left = meta is null ? 0 : Query(meta, "/imgdesc/Left") ?? 0;
        var top = meta is null ? 0 : Query(meta, "/imgdesc/Top") ?? 0;
        var disposal = meta is null ? 0 : Query(meta, "/grctlext/Disposal") ?? 0;
        var delayCs = meta is null ? 0 : Query(meta, "/grctlext/Delay") ?? 0;

        var delay = delayCs <= 1 ? MinimumDelay : TimeSpan.FromMilliseconds(delayCs * 10d);

        return new RawFrame(
            pixels, left, top, width, height,
            disposal is 2 or 3 ? (Disposal)disposal : Disposal.Keep,
            delay);
    }

    private static int? Query(BitmapMetadata meta, string query)
    {
        try
        {
            return meta.GetQuery(query) switch
            {
                ushort u => u,
                byte b => b,
                int i => i,
                _ => null,
            };
        }
        catch (Exception ex) when (ex is NotSupportedException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }
}
