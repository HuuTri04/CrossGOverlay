using System.Numerics;

namespace CrosshairOverlay.Services.Import;

/// <summary>
/// Giá trị crosshair đọc ra từ một share code của CS2, giữ nguyên đơn vị của game.
/// </summary>
public sealed record Cs2Crosshair
{
    public double Size { get; init; }
    public double Thickness { get; init; }
    public double Gap { get; init; }
    public double OutlineThickness { get; init; }
    public bool HasOutline { get; init; }
    public byte Red { get; init; }
    public byte Green { get; init; }
    public byte Blue { get; init; }
    public byte Alpha { get; init; }
    public bool HasAlpha { get; init; }
    public bool HasCenterDot { get; init; }
    public bool IsTStyle { get; init; }
    public int Style { get; init; }
}

/// <summary>
/// Giải mã share code crosshair của CS2 (<c>CSGO-xxxxx-xxxxx-xxxxx-xxxxx-xxxxx</c>).
/// </summary>
/// <remarks>
/// Định dạng này không được Valve công bố; cách giải mã dưới đây theo bản mô tả cộng đồng đã
/// dùng rộng rãi (xem README, mục Import mã crosshair). Mã là một số base-57 big-endian, đổi
/// sang 19 byte rồi đọc từng trường theo bảng bit.
///
/// <para>
/// Bảng chữ cái cố tình bỏ các ký tự I, O và 1 để người dùng không đọc nhầm khi chép tay.
/// </para>
/// </remarks>
public static class Cs2ShareCode
{
    private const string Dictionary = "ABCDEFGHJKLMNOPQRSTUVWXYZabcdefhijkmnopqrstuvwxyz23456789";
    private const string Prefix = "CSGO-";
    private const int ByteCount = 19;

    /// <summary>
    /// Thử giải mã. Trả về false với mã sai định dạng thay vì ném exception — người dùng dán
    /// nhầm chuỗi là chuyện thường, không phải lỗi lập trình.
    /// </summary>
    public static bool TryDecode(string? shareCode, out Cs2Crosshair crosshair, out string? error)
    {
        crosshair = new Cs2Crosshair();
        error = null;

        if (string.IsNullOrWhiteSpace(shareCode))
        {
            error = Localization.Tr.Get("Import_ErrEmpty");
            return false;
        }

        var cleaned = Normalize(shareCode);

        if (cleaned.Length == 0)
        {
            error = Localization.Tr.Get("Import_ErrNoValidChars");
            return false;
        }

        // Duyệt NGƯỢC từ ký tự cuối: chữ số có trọng số nhỏ nhất nằm ở đầu chuỗi. Duyệt xuôi
        // vẫn cho ra một số hợp lệ nhưng mọi byte đều sai, và sai lặng lẽ.
        var value = BigInteger.Zero;
        for (var i = cleaned.Length - 1; i >= 0; i--)
        {
            var index = Dictionary.IndexOf(cleaned[i], StringComparison.Ordinal);
            if (index < 0)
            {
                error = Localization.Tr.Format("Import_ErrBadChar", cleaned[i]);
                return false;
            }

            value = (value * Dictionary.Length) + index;
        }

        if (value.Sign < 0)
        {
            error = Localization.Tr.Get("Import_ErrNoValidChars");
            return false;
        }

        var bytes = ToBigEndianBytes(value);
        if (bytes is null)
        {
            error = Localization.Tr.Get("Import_ErrTooLong");
            return false;
        }

        crosshair = ReadFields(bytes);
        return true;
    }

    /// <summary>Bỏ tiền tố, dấu gạch nối và khoảng trắng.</summary>
    private static string Normalize(string shareCode)
    {
        var text = shareCode.Trim();

        if (text.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
            text = text[Prefix.Length..];

        return new string([.. text.Where(c => c != '-' && !char.IsWhiteSpace(c))]);
    }

    /// <summary>
    /// Quy về đúng <see cref="ByteCount"/> byte theo thứ tự big-endian.
    /// </summary>
    /// <remarks>
    /// <c>BigInteger.ToByteArray</c> trả little-endian và có thể kèm một byte 0 ở cuối để đánh
    /// dấu số dương — phải cắt byte đó đi trước khi đảo chiều, nếu không mọi trường sẽ lệch
    /// một vị trí.
    /// </remarks>
    private static byte[]? ToBigEndianBytes(BigInteger value)
    {
        var little = value.ToByteArray();

        var length = little.Length;
        while (length > 0 && little[length - 1] == 0) length--;

        if (length > ByteCount) return null;

        var result = new byte[ByteCount];
        for (var i = 0; i < length; i++)
            result[ByteCount - 1 - i] = little[i];

        return result;
    }

    private static Cs2Crosshair ReadFields(byte[] b) => new()
    {
        // Gap là số CÓ DẤU: crosshair phổ biến hay dùng gap âm.
        Gap = unchecked((sbyte)b[3]) / 10d,

        OutlineThickness = b[4] / 2d,
        Red = b[5],
        Green = b[6],
        Blue = b[7],
        Alpha = b[8],

        HasOutline = (b[11] & 0x08) != 0,

        Thickness = b[13] / 10d,

        Style = (b[14] & 0x0F) >> 1,
        HasCenterDot = ((b[14] >> 4) & 0x01) != 0,
        HasAlpha = ((b[14] >> 4) & 0x04) != 0,
        IsTStyle = ((b[14] >> 4) & 0x08) != 0,

        Size = b[15] / 10d,
    };
}
