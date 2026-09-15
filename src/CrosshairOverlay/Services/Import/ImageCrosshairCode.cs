using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Windows.Media.Imaging;
using CrosshairOverlay.Core.Models;
using CrosshairOverlay.Services.Storage;

namespace CrosshairOverlay.Services.Import;

/// <summary>Nội dung đọc được từ một mã tâm ngắm hình ảnh.</summary>
/// <param name="ImageBytes">Nội dung file ảnh ĐÃ GIẢI NÉN, đã kiểm tra giải mã được.</param>
/// <param name="Extension">Đuôi file theo định dạng THẬT của nội dung: ".png", ".gif" hoặc ".jpg".</param>
public sealed record ImageCrosshair(
    byte[] ImageBytes,
    string Extension,
    int PixelWidth,
    int PixelHeight,
    double Scale,
    double Opacity,
    double OffsetX,
    double OffsetY,
    double Rotation);

/// <summary>Vì sao không xuất được mã.</summary>
public enum ImageCodeExportError
{
    None,

    /// <summary>File ảnh không giải mã được.</summary>
    Unreadable,

    /// <summary>Ảnh (GIF động, không thu nhỏ được) vẫn quá <see cref="ImageCrosshairCode.MaxImageBytes"/> sau khi nén.</summary>
    TooLarge,
}

/// <summary>
/// Mã chia sẻ riêng của ứng dụng cho tâm ngắm HÌNH ẢNH — thứ mã Valorant/CS2 không biểu diễn được.
/// </summary>
/// <remarks>
/// <para>
/// Định dạng: <c>CGO-IMG;[Base64 của ảnh đã nén GZip];s[Scale];a[Opacity];x[OffsetX];y[OffsetY];c[CRC32]</c>,
/// thêm <c>;r[Rotation]</c> trước <c>c</c> khi có xoay. Số viết theo văn hoá bất biến (dấu chấm thập
/// phân). Mỗi thông số tự mang tên nên thứ tự không quan trọng và khoá lạ được bỏ qua (để mã của
/// phiên bản sau vẫn nhập được); s/a/x/y là bắt buộc.
/// </para>
/// <para>
/// <c>c</c> là CRC32 (8 chữ số hex) của toàn bộ phần giữa tiền tố và <c>;c</c>, và BẮT BUỘC đứng
/// cuối. Nhờ vậy mã bị cắt ở BẤT KỲ đâu đều bị phát hiện — kể cả cắt đúng giữa con số cuối cùng
/// (<c>y-20</c> thành <c>y-2</c>), thứ mà chỉ CRC bên trong gói GZip (chỉ bọc phần ảnh) không bắt
/// được. Tốn đúng 10 ký tự. Mã thiếu checksum bị từ chối, không coi là "mã cũ": nếu nhận, một mã bị
/// cắt mất cả đuôi sẽ lọt qua.
/// </para>
/// <para>
/// Nén GZip gần như không làm ngắn ảnh PNG/JPEG — hai định dạng đó vốn đã nén sẵn. Thứ làm mã ngắn
/// thật sự là THU NHỎ ảnh lớn trước khi nhúng (xem <see cref="ShareMaxSide"/>), có bù Scale để tâm
/// ngắm hiện ra đúng kích thước cũ. GZip vẫn đáng giữ: CRC32 + độ dài ở đuôi gói
/// là lớp kiểm tra thứ hai cho riêng dữ liệu ảnh, sau khi đã giải nén.
/// </para>
/// <para>
/// Mã đến từ người khác nên KHÔNG đáng tin: độ dài bị giới hạn trước khi giải mã, dữ liệu giải nén
/// bị chặn ở <see cref="MaxImageBytes"/> (chống "bom nén"), nội dung phải là ảnh giải mã được, và
/// mọi thông số đi qua giới hạn của model khi áp vào preset.
/// </para>
/// </remarks>
public static class ImageCrosshairCode
{
    public const string Prefix = "CGO-IMG;";

    /// <summary>Giới hạn cho cả ảnh đã nén trong mã lẫn ảnh sau khi giải nén.</summary>
    public const int MaxImageBytes = 2 * 1024 * 1024;

    /// <summary>
    /// Ảnh nhúng vào mã được thu nhỏ để cạnh dài nhất không vượt con số này — trừ khi tâm ngắm đang
    /// hiện ra còn lớn hơn (xem <see cref="ShareMaxSide"/>).
    /// </summary>
    public const int ShareMinMaxSide = CustomImageStore.MaxStoredPixelSide;

    private static readonly int MaxBase64Chars = (MaxImageBytes + 2) / 3 * 4;

    /// <summary>Chuỗi có mang dấu hiệu của mã hình ảnh không. Không kiểm tra nội dung.</summary>
    public static bool IsImageCode(string? text) =>
        text is not null && text.TrimStart().StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);

    // ====================================================================== xuất

    /// <summary>
    /// Cạnh dài nhất của ảnh nhúng vào mã: 256, hoặc gấp đôi cỡ tâm ngắm đang hiển thị nếu lớn hơn.
    /// </summary>
    /// <remarks>
    /// Gấp đôi để vẫn nét trên màn hình 200% DPI. Không bao giờ vượt kích thước gốc: phóng to không
    /// thêm chi tiết, chỉ làm mã dài ra.
    /// </remarks>
    internal static int ShareMaxSide(int longestSide, double scale)
    {
        var shown = (int)Math.Ceiling(longestSide * Math.Max(scale, 0d) * 2d);
        return Math.Min(longestSide, Math.Max(ShareMinMaxSide, shown));
    }

    /// <param name="imageBytes">Nội dung file ảnh đang dùng (chưa nén).</param>
    public static bool TryEncode(
        byte[] imageBytes, CustomImageSettings image, double rotation,
        out string? code, out ImageCodeExportError error)
    {
        ArgumentNullException.ThrowIfNull(imageBytes);
        ArgumentNullException.ThrowIfNull(image);

        code = null;

        var probe = ImageShrinker.Shrink("share" + (DetectExtension(imageBytes) ?? ".png"), imageBytes, int.MaxValue);
        if (probe.Outcome == ShrinkOutcome.Rejected)
        {
            error = ImageCodeExportError.Unreadable;
            return false;
        }

        var longest = Math.Max(probe.OriginalWidth, probe.OriginalHeight);
        var shrunk = ImageShrinker.Shrink(probe.FileName, imageBytes, ShareMaxSide(longest, image.Scale));

        // Ảnh nhỏ đi bao nhiêu lần thì Scale lớn lên bấy nhiêu: tâm ngắm hiện ra đúng kích thước cũ.
        var scale = image.Scale;
        if (shrunk.Outcome == ShrinkOutcome.Resized)
            scale *= (double)longest / Math.Max(shrunk.Width, shrunk.Height);

        var compressed = Gzip(shrunk.Bytes);
        if (compressed.Length > MaxImageBytes)
        {
            error = ImageCodeExportError.TooLarge;
            return false;
        }

        code = Build(compressed, scale, image.Opacity, image.OffsetX, image.OffsetY, rotation);
        error = ImageCodeExportError.None;
        return true;
    }

    private static string Build(byte[] compressed, double scale, double opacity, double x, double y, double rotation)
    {
        var inv = CultureInfo.InvariantCulture;
        var builder = new StringBuilder(Prefix.Length + (compressed.Length + 2) / 3 * 4 + 40)
            .Append(Prefix)
            .Append(Convert.ToBase64String(compressed))
            .Append(";s").Append(scale.ToString("0.####", inv))
            .Append(";a").Append(opacity.ToString("0.###", inv))
            .Append(";x").Append(x.ToString("0.##", inv))
            .Append(";y").Append(y.ToString("0.##", inv));

        // Không xoay thì không ghi — mỗi ký tự bớt được đều đáng.
        if (Math.Abs(rotation) >= 0.005d) builder.Append(";r").Append(rotation.ToString("0.##", inv));

        return AppendChecksum(builder.ToString());
    }

    /// <summary>
    /// Nối trường checksum vào một mã chưa ký: <c>;c</c> + CRC32 (8 chữ số hex thường) của phần sau tiền tố.
    /// </summary>
    internal static string AppendChecksum(string unsignedCode)
    {
        ArgumentNullException.ThrowIfNull(unsignedCode);
        if (!unsignedCode.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Thiếu tiền tố " + Prefix, nameof(unsignedCode));

        return unsignedCode + ";c" + Checksum(unsignedCode.AsSpan(Prefix.Length)).ToString("x8", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// CRC32 trên các ký tự (đều là ASCII với mã hợp lệ). Tính trên chuỗi đã bỏ khoảng trắng và không
    /// gồm tiền tố, nên mã bị app chat tự xuống dòng hay đổi hoa/thường tiền tố vẫn khớp.
    /// </summary>
    private static uint Checksum(ReadOnlySpan<char> signedPart) => Crc32.Compute(Encoding.UTF8.GetBytes(signedPart.ToArray()));

    private static byte[] Gzip(byte[] data)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
            gzip.Write(data);
        return output.ToArray();
    }

    // ====================================================================== nhập

    /// <summary>Đọc mã. Không bao giờ ném exception với dữ liệu hỏng — trả false.</summary>
    /// <param name="reason">Lý do kỹ thuật, chỉ để ghi log; thông báo cho người dùng là một câu chung.</param>
    public static bool TryDecode(string? text, out ImageCrosshair? result, out string? reason)
    {
        result = null;

        try
        {
            return TryDecodeCore(text, out result, out reason);
        }
        catch (Exception ex)
        {
            // Lưới an toàn cuối: dữ liệu ngoài vào không được phép làm sập ứng dụng, dù bộ giải nén hay
            // bộ giải mã ảnh của hệ thống có ném loại lỗi nào không lường trước.
            result = null;
            reason = ex.GetType().Name + ": " + ex.Message;
            return false;
        }
    }

    private static bool TryDecodeCore(string? text, out ImageCrosshair? result, out string? reason)
    {
        result = null;

        if (!IsImageCode(text))
        {
            reason = "Thiếu tiền tố " + Prefix;
            return false;
        }

        // Ứng dụng chat hay tự xuống dòng hoặc chèn khoảng trắng vào chuỗi dài; mã hợp lệ không bao
        // giờ chứa ký tự trắng, nên bỏ hết là an toàn. Giới hạn độ dài TRƯỚC khi làm gì khác.
        var normalized = StripWhitespace(text!);
        if (normalized.Length > Prefix.Length + MaxBase64Chars + 256)
        {
            reason = $"Độ dài phần ảnh không hợp lệ ({normalized.Length} ký tự).";
            return false;
        }

        if (!TryVerifyChecksum(normalized.AsSpan(Prefix.Length), out var signedPart, out reason)) return false;

        var fields = signedPart.Split(';');
        var base64 = fields[0];
        if (base64.Length == 0 || base64.Length > MaxBase64Chars)
        {
            reason = $"Độ dài phần ảnh không hợp lệ ({base64.Length} ký tự).";
            return false;
        }

        var buffer = new byte[base64.Length / 4 * 3 + 3];
        if (!Convert.TryFromBase64String(base64, buffer, out var written))
        {
            reason = "Phần ảnh không phải Base64 hợp lệ (có thể bị cắt mất ký tự).";
            return false;
        }

        var payload = buffer.AsSpan(0, written).ToArray();

        if (!IsGzip(payload))
        {
            reason = "Phần ảnh không phải dữ liệu nén GZip.";
            return false;
        }

        if (!TryGunzip(payload, out var bytes, out reason)) return false;

        if (!TryReadParameters(fields.AsSpan(1), out var scale, out var opacity, out var x, out var y, out var rotation, out reason))
            return false;

        if (DetectExtension(bytes) is not { } extension)
        {
            reason = "Nội dung không phải PNG, GIF hay JPEG.";
            return false;
        }

        if (!TryReadSize(bytes, out var width, out var height, out reason)) return false;

        result = new ImageCrosshair(bytes, extension, width, height, scale, opacity, x, y, rotation);
        return true;
    }

    /// <summary>
    /// Tách và kiểm tra trường checksum cuối mã.
    /// </summary>
    /// <param name="body">Mã đã bỏ khoảng trắng, không gồm tiền tố.</param>
    /// <param name="signedPart">Phần được ký — mọi thứ trước <c>;c</c>.</param>
    private static bool TryVerifyChecksum(ReadOnlySpan<char> body, out string signedPart, out string? reason)
    {
        signedPart = string.Empty;

        var separator = body.LastIndexOf(';');
        var last = separator < 0 ? ReadOnlySpan<char>.Empty : body[(separator + 1)..];

        if (last.Length == 0 || char.ToLowerInvariant(last[0]) != 'c')
        {
            reason = "Thiếu checksum ở cuối mã (mã có thể bị cắt mất đuôi).";
            return false;
        }

        if (last.Length != 9
            || !uint.TryParse(last[1..], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var expected))
        {
            reason = "Checksum không đúng dạng 8 chữ số hex (mã có thể bị cắt).";
            return false;
        }

        var signed = body[..separator];
        if (Checksum(signed) != expected)
        {
            reason = "Checksum không khớp: mã bị cắt hoặc bị sửa.";
            return false;
        }

        signedPart = signed.ToString();
        reason = null;
        return true;
    }

    /// <summary>Đọc thông số dạng khoá-giá trị (<c>s0.5;a1;x10;y-5;r15</c>).</summary>
    private static bool TryReadParameters(
        ReadOnlySpan<string> fields,
        out double scale, out double opacity, out double x, out double y, out double rotation,
        out string? reason)
    {
        scale = opacity = x = y = rotation = 0d;
        reason = null;

        var tokens = new List<string>(fields.Length);
        foreach (var field in fields)
        {
            if (field.Length > 0) tokens.Add(field);
        }

        bool hasS = false, hasA = false, hasX = false, hasY = false;
        foreach (var token in tokens)
        {
            if (!char.IsLetter(token[0]))
            {
                reason = $"Thông số '{token}' thiếu tên khoá.";
                return false;
            }

            var key = char.ToLowerInvariant(token[0]);
            if (key is not ('s' or 'a' or 'x' or 'y' or 'r')) continue;   // khoá của phiên bản sau


            if (!TryNumber(token.AsSpan(1), out var value))
            {
                reason = $"Giá trị của '{key}' không phải số hợp lệ: '{token}'.";
                return false;
            }

            switch (key)
            {
                case 's': scale = value; hasS = true; break;
                case 'a': opacity = value; hasA = true; break;
                case 'x': x = value; hasX = true; break;
                case 'y': y = value; hasY = true; break;
                case 'r': rotation = value; break;
            }
        }

        if (!(hasS && hasA && hasX && hasY))
        {
            reason = "Thiếu thông số s/a/x/y (mã có thể bị cắt mất đuôi).";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Giải nén GZip, có giới hạn kích thước, rồi đối chiếu CRC32 và độ dài ghi ở đuôi gói.
    /// </summary>
    /// <remarks>
    /// Bộ giải nén không đảm bảo báo lỗi khi dữ liệu bị cắt cụt — nó có thể lặng lẽ trả về phần
    /// đọc được. Đuôi GZip (8 byte cuối: CRC32 rồi độ dài gốc) mới là bằng chứng mã còn nguyên vẹn.
    /// </remarks>
    private static bool TryGunzip(byte[] payload, out byte[] data, out string? reason)
    {
        data = [];

        // 10 byte đầu gói + tối thiểu 2 byte dữ liệu nén + 8 byte đuôi.
        if (payload.Length < 20)
        {
            reason = "Gói nén quá ngắn.";
            return false;
        }

        try
        {
            using var input = new MemoryStream(payload, writable: false);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();

            var chunk = new byte[64 * 1024];
            int read;
            while ((read = gzip.Read(chunk, 0, chunk.Length)) > 0)
            {
                if (output.Length + read > MaxImageBytes)
                {
                    reason = "Ảnh sau khi giải nén vượt giới hạn.";
                    return false;
                }

                output.Write(chunk, 0, read);
            }

            data = output.ToArray();
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or NotSupportedException
                                       or ArgumentException or InvalidOperationException)
        {
            reason = "Không giải nén được: " + ex.Message;
            return false;
        }

        var expectedCrc = BitConverter.ToUInt32(payload, payload.Length - 8);
        var expectedLength = BitConverter.ToUInt32(payload, payload.Length - 4);

        if ((uint)data.Length != expectedLength || Crc32.Compute(data) != expectedCrc)
        {
            reason = "Dữ liệu nén bị cắt hoặc bị sửa (CRC32/độ dài không khớp).";
            return false;
        }

        reason = null;
        return true;
    }

    private static bool IsGzip(byte[] bytes) => bytes.Length >= 2 && bytes[0] == 0x1F && bytes[1] == 0x8B;

    /// <summary>
    /// Dựng preset từ mã. Ảnh CHƯA vào kho: nó nằm trong <see cref="CrosshairProfile.EmbeddedImage"/>,
    /// giống preset nhập từ file, và nơi tạo preset đưa nó vào kho (qua đủ bước kiểm tra của kho).
    /// </summary>
    /// <remarks>
    /// Tách vậy để hộp thoại dán mã chỉ đọc và xem trước — gõ dở hay dán nhầm không để lại file rác.
    /// </remarks>
    public static CrosshairProfile ToProfile(ImageCrosshair code, string name)
    {
        ArgumentNullException.ThrowIfNull(code);

        var profile = new CrosshairProfile
        {
            Name = name,
            Type = CrosshairType.Image,

            // Setter của model tự kẹp về giới hạn — mã tự sửa tay với Scale = 9999 không lọt qua.
            Rotation = code.Rotation,
            EmbeddedImage = new EmbeddedImageData
            {
                // Kho ảnh tự nối mã băm nội dung vào tên: dán cùng một mã hai lần không nhân đôi file.
                FileName = "shared" + code.Extension,
                Data = Convert.ToBase64String(code.ImageBytes),
            },
        };

        profile.Image.Scale = code.Scale;
        profile.Image.Opacity = code.Opacity;
        profile.Image.OffsetX = code.OffsetX;
        profile.Image.OffsetY = code.OffsetY;
        return profile;
    }

    private static bool TryNumber(ReadOnlySpan<char> field, out double value) =>
        double.TryParse(field, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
        && double.IsFinite(value);

    private static string StripWhitespace(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
            if (!char.IsWhiteSpace(ch)) builder.Append(ch);
        return builder.ToString();
    }

    /// <summary>Nhận định dạng qua chữ ký đầu file, không tin bất cứ tên hay đuôi nào.</summary>
    private static string? DetectExtension(byte[] bytes)
    {
        ReadOnlySpan<byte> png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        ReadOnlySpan<byte> gif = "GIF8"u8;
        ReadOnlySpan<byte> jpeg = [0xFF, 0xD8, 0xFF];

        var span = bytes.AsSpan();
        if (span.StartsWith(png)) return ".png";
        if (span.StartsWith(gif)) return ".gif";
        if (span.StartsWith(jpeg)) return ".jpg";
        return null;
    }

    /// <summary>
    /// Giải mã thật để chắc ảnh không hỏng — nội dung đúng chữ ký PNG vẫn có thể không vẽ ra được.
    /// </summary>
    private static bool TryReadSize(byte[] bytes, out int width, out int height, out string? reason)
    {
        width = height = 0;

        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
            width = frame.PixelWidth;
            height = frame.PixelHeight;
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or ArgumentException
                                       or InvalidOperationException or IOException or OverflowException)
        {
            reason = "Ảnh không giải mã được: " + ex.Message;
            return false;
        }

        if (width <= 0 || height <= 0 || width > CustomImageStore.MaxPixelSide || height > CustomImageStore.MaxPixelSide)
        {
            reason = $"Kích thước ảnh không hợp lệ ({width}×{height}).";
            return false;
        }

        reason = null;
        return true;
    }

    /// <summary>CRC-32 chuẩn (đa thức 0xEDB88320) — đúng loại GZip ghi ở đuôi gói.</summary>
    private static class Crc32
    {
        private static readonly uint[] Table = BuildTable();

        public static uint Compute(ReadOnlySpan<byte> data)
        {
            var crc = 0xFFFFFFFFu;
            foreach (var b in data)
                crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
            return ~crc;
        }

        private static uint[] BuildTable()
        {
            var table = new uint[256];
            for (var i = 0u; i < 256; i++)
            {
                var c = i;
                for (var k = 0; k < 8; k++)
                    c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                table[i] = c;
            }

            return table;
        }
    }
}
