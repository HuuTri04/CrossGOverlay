using System.Globalization;
using System.Text;

namespace CrosshairOverlay.Services.Import;

/// <summary>
/// Trường checksum ở cuối các mã chia sẻ do ứng dụng tự sinh (<c>CGO-…</c>): <c>;c</c> + CRC32
/// dạng 8 chữ số hex thường.
/// </summary>
/// <remarks>
/// Mã của ứng dụng dài hàng nghìn ký tự và người dùng chuyền tay qua chat, Discord, ghi chú —
/// nơi mã rất dễ bị cắt cụt hoặc dính thêm ký tự. Checksum biến một mã hỏng thành thông báo lỗi
/// rõ ràng thay vì một preset trông sai mà không ai hiểu vì sao.
///
/// <para>
/// Tính trên chuỗi đã bỏ khoảng trắng và KHÔNG gồm tiền tố, nên mã bị ứng dụng chat tự xuống dòng
/// hay đổi hoa/thường tiền tố vẫn khớp.
/// </para>
/// </remarks>
internal static class ShareCodeChecksum
{
    /// <summary>Nối trường checksum vào một mã chưa ký.</summary>
    public static string Append(string unsignedCode, string prefix)
    {
        ArgumentNullException.ThrowIfNull(unsignedCode);

        if (!unsignedCode.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Thiếu tiền tố " + prefix, nameof(unsignedCode));

        return unsignedCode + ";c" + Compute(unsignedCode.AsSpan(prefix.Length)).ToString("x8", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Tách và kiểm tra trường checksum cuối mã.
    /// </summary>
    /// <param name="body">Mã đã bỏ khoảng trắng, không gồm tiền tố.</param>
    /// <param name="signedPart">Phần được ký — mọi thứ trước <c>;c</c>.</param>
    public static bool TryVerify(ReadOnlySpan<char> body, out string signedPart, out string? reason)
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
        if (Compute(signed) != expected)
        {
            reason = "Checksum không khớp: mã bị cắt hoặc bị sửa.";
            return false;
        }

        signedPart = signed.ToString();
        reason = null;
        return true;
    }

    /// <summary>CRC32 trên các ký tự (đều là ASCII với mã hợp lệ).</summary>
    public static uint Compute(ReadOnlySpan<char> text) => Crc32.Compute(Encoding.UTF8.GetBytes(text.ToArray()));

    /// <summary>CRC-32 chuẩn (đa thức 0xEDB88320) — đúng loại GZip ghi ở đuôi gói.</summary>
    public static class Crc32
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
