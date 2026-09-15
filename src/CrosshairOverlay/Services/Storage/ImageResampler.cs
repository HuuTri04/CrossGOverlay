using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CrosshairOverlay.Services.Storage;

/// <summary>
/// Thu nhỏ ảnh bằng phép lấy trung bình theo diện tích (box filter có trọng số).
/// </summary>
/// <remarks>
/// <para>
/// Tự làm thay cho <c>TransformedBitmap</c> vì ba lý do. Kích thước ra chính xác tuyệt đối
/// (<c>TransformedBitmap</c> làm tròn theo cách riêng, có thể ra 257 thay vì 256). Chạy được trên
/// thread pool, không cần Dispatcher như <c>RenderTargetBitmap</c>. Và tính trên màu ĐÃ NHÂN
/// alpha: lấy trung bình màu chưa nhân alpha thì các điểm trong suốt (thường mang màu đen) lem
/// vào mép, tạo viền tối quanh tâm ngắm có nền trong suốt.
/// </para>
/// <para>
/// Mỗi điểm ảnh đích là trung bình có trọng số của đúng vùng ảnh gốc nó phủ lên, kể cả phần lẻ
/// ở mép — cách thu nhỏ cho ảnh mịn nhất, không răng cưa như lấy mẫu điểm gần nhất.
/// </para>
/// </remarks>
internal static class ImageResampler
{
    public static BitmapSource Downscale(BitmapSource source, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (width < 1 || height < 1) throw new ArgumentOutOfRangeException(nameof(width));

        var premultiplied = source.Format == PixelFormats.Pbgra32
            ? source
            : new FormatConvertedBitmap(source, PixelFormats.Pbgra32, null, 0);

        var srcWidth = premultiplied.PixelWidth;
        var srcHeight = premultiplied.PixelHeight;
        if (width > srcWidth || height > srcHeight)
            throw new ArgumentException("Chỉ dùng để thu nhỏ.", nameof(width));

        var pixels = new byte[srcWidth * srcHeight * 4];
        premultiplied.CopyPixels(pixels, srcWidth * 4, 0);

        // Hai lượt tách rời: ngang rồi dọc. Cho kết quả như lấy trung bình theo ô chữ nhật, nhưng
        // chi phí tỷ lệ với chiều rộng + chiều cao chứ không phải tích của chúng.
        var horizontal = new float[width * srcHeight * 4];
        var columns = Weights(srcWidth, width);
        for (var y = 0; y < srcHeight; y++)
        {
            var srcRow = y * srcWidth * 4;
            var dstRow = y * width * 4;
            for (var x = 0; x < width; x++)
            {
                foreach (var (index, weight) in columns[x])
                {
                    var s = srcRow + index * 4;
                    var d = dstRow + x * 4;
                    horizontal[d] += pixels[s] * weight;
                    horizontal[d + 1] += pixels[s + 1] * weight;
                    horizontal[d + 2] += pixels[s + 2] * weight;
                    horizontal[d + 3] += pixels[s + 3] * weight;
                }
            }
        }

        var result = new byte[width * height * 4];
        var rows = Weights(srcHeight, height);
        for (var y = 0; y < height; y++)
        {
            var dstRow = y * width * 4;
            for (var x = 0; x < width; x++)
            {
                float b = 0, g = 0, r = 0, a = 0;
                foreach (var (index, weight) in rows[y])
                {
                    var s = (index * width + x) * 4;
                    b += horizontal[s] * weight;
                    g += horizontal[s + 1] * weight;
                    r += horizontal[s + 2] * weight;
                    a += horizontal[s + 3] * weight;
                }

                var alpha = ToByte(a);
                var d = dstRow + x * 4;

                // Màu đã nhân alpha không được vượt alpha, kể cả vì sai số làm tròn.
                result[d] = Math.Min(ToByte(b), alpha);
                result[d + 1] = Math.Min(ToByte(g), alpha);
                result[d + 2] = Math.Min(ToByte(r), alpha);
                result[d + 3] = alpha;
            }
        }

        var scaled = BitmapSource.Create(width, height, 96, 96, PixelFormats.Pbgra32, null, result, width * 4);

        // Encoder PNG/JPEG không nhận định dạng đã nhân alpha; đổi về BGRA thường để lưu.
        var output = new FormatConvertedBitmap(scaled, PixelFormats.Bgra32, null, 0);
        output.Freeze();
        return output;
    }

    /// <summary>
    /// Với mỗi điểm đích: danh sách (chỉ số điểm gốc, trọng số) phủ lên nó; tổng trọng số bằng 1.
    /// </summary>
    private static (int Index, float Weight)[][] Weights(int sourceLength, int targetLength)
    {
        var ratio = (double)sourceLength / targetLength;
        var weights = new (int, float)[targetLength][];

        for (var i = 0; i < targetLength; i++)
        {
            var start = i * ratio;
            var end = Math.Min((i + 1) * ratio, sourceLength);
            var first = (int)Math.Floor(start);
            var last = Math.Min((int)Math.Ceiling(end), sourceLength);

            var list = new (int, float)[last - first];
            for (var k = first; k < last; k++)
            {
                var overlap = Math.Min(end, k + 1) - Math.Max(start, k);
                list[k - first] = (k, (float)(overlap / ratio));
            }

            weights[i] = list;
        }

        return weights;
    }

    private static byte ToByte(float value) => (byte)Math.Clamp(MathF.Round(value), 0f, 255f);
}
