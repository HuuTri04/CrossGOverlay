using System.Numerics;
using System.Text;

namespace CrosshairOverlay.Services.Import;

/// <summary>
/// Dựng share code crosshair của CS2 (<c>CSGO-xxxxx-xxxxx-xxxxx-xxxxx-xxxxx</c>) từ
/// <see cref="Cs2Crosshair"/> — chiều ngược của <see cref="Cs2ShareCode.TryDecode"/>.
/// </summary>
/// <remarks>
/// <para>
/// Định dạng không được Valve công bố. Bố cục 18 byte dưới đây đối chiếu với bản mô tả cộng đồng
/// đã dùng rộng rãi, và quan trọng hơn: <c>Cs2ShareCodeEncoderTests</c> mã hoá lại hai share code
/// THẬT lấy từ bộ sưu tập công khai và đòi ra đúng từng ký tự. Đó là bằng chứng cho cả bảng byte,
/// cách tính checksum lẫn phép đổi cơ số 57.
/// </para>
/// <para>
/// Byte 0 là checksum = tổng các byte còn lại lấy 8 bit thấp. Sai checksum thì CS2 từ chối mã.
/// </para>
/// </remarks>
public static class Cs2ShareCodeEncoder
{
    private const string Dictionary = "ABCDEFGHJKLMNOPQRSTUVWXYZabcdefhijkmnopqrstuvwxyz23456789";
    private const int ByteCount = 18;
    private const int CharCount = 25;
    private const int GroupSize = 5;

    /// <summary>Giá trị <c>cl_crosshairstyle</c> cho crosshair cổ điển, KHÔNG giãn theo bước chân.</summary>
    public const int ClassicStaticStyle = 4;

    public static string Encode(Cs2Crosshair crosshair)
    {
        ArgumentNullException.ThrowIfNull(crosshair);

        var bytes = new byte[ByteCount];

        // byte 0 để trống, điền checksum sau cùng.
        bytes[1] = 1;
        bytes[2] = Signed(crosshair.Gap);

        // Viền lưu theo nửa đơn vị (nhân 2), khác với các trường khác lưu theo phần mười.
        bytes[3] = (byte)Math.Clamp(Math.Round(crosshair.OutlineThickness * 2d), 0d, 255d);
        bytes[4] = crosshair.Red;
        bytes[5] = crosshair.Green;
        bytes[6] = crosshair.Blue;
        bytes[7] = crosshair.Alpha;

        // Các trường chỉ có tác dụng với crosshair kiểu ĐỘNG (giãn ra khi chạy). Crosshair của app
        // là tĩnh nên chúng không đổi hình dạng, nhưng vẫn ghi đúng giá trị để mã không mang số lạ
        // nếu người dùng đổi sang kiểu động trong game.
        bytes[8] = (byte)((crosshair.SplitDistance & 0x07) | ((crosshair.FollowRecoil ? 1 : 0) << 7));
        bytes[9] = Signed(crosshair.FixedGap);
        bytes[10] = (byte)((crosshair.ColorIndex & 0x07)
                           | ((crosshair.HasOutline ? 1 : 0) << 3)
                           | (Tenth(crosshair.InnerSplitAlpha) << 4));
        bytes[11] = (byte)(Tenth(crosshair.OuterSplitAlpha) | (Tenth(crosshair.SplitSizeRatio) << 4));

        bytes[12] = Scaled(crosshair.Thickness);
        bytes[13] = (byte)(((crosshair.Style & 0x07) << 1)
                           | ((crosshair.HasCenterDot ? 1 : 0) << 4)
                           | ((crosshair.DeployedWeaponGap ? 1 : 0) << 5)
                           | ((crosshair.HasAlpha ? 1 : 0) << 6)
                           | ((crosshair.IsTStyle ? 1 : 0) << 7));
        bytes[14] = Scaled(crosshair.Size);

        // 15–17 để trống: game không dùng, và mã thật cũng bằng 0.

        var sum = 0;
        for (var i = 1; i < bytes.Length; i++) sum += bytes[i];
        bytes[0] = (byte)(sum & 0xFF);

        return Format(ToBase57(bytes));
    }

    /// <summary>Số nguyên big-endian 18 byte đổi sang 25 chữ số cơ số 57, chữ số NHỎ NHẤT đứng trước.</summary>
    /// <remarks>
    /// Thứ tự này là thứ bộ giải mã đọc: nó duyệt ngược chuỗi. Số nhỏ thì thiếu chữ số ở cuối,
    /// phải đệm bằng ký tự mang giá trị 0 để mã luôn đủ 25 ký tự.
    /// </remarks>
    private static string ToBase57(byte[] bytes)
    {
        // BigInteger đọc little-endian; thêm một byte 0 ở cuối để luôn là số DƯƠNG.
        var little = new byte[ByteCount + 1];
        for (var i = 0; i < ByteCount; i++) little[i] = bytes[ByteCount - 1 - i];

        var value = new BigInteger(little);
        var builder = new StringBuilder(CharCount);

        for (var i = 0; i < CharCount; i++)
        {
            value = BigInteger.DivRem(value, Dictionary.Length, out var digit);
            builder.Append(Dictionary[(int)digit]);
        }

        return builder.ToString();
    }

    private static string Format(string code)
    {
        var builder = new StringBuilder("CSGO");

        for (var i = 0; i < code.Length; i += GroupSize)
        {
            builder.Append('-');
            builder.Append(code, i, GroupSize);
        }

        return builder.ToString();
    }

    /// <summary>Giá trị thập phân một chữ số sau dấu phẩy, lưu thành số nguyên nhân 10.</summary>
    private static byte Scaled(double value) => (byte)Math.Clamp(Math.Round(value * 10d), 0d, 255d);

    /// <summary>Giá trị 0–1.5 lưu trong MỘT nibble theo phần mười.</summary>
    private static int Tenth(double value) => (int)Math.Clamp(Math.Round(value * 10d), 0d, 15d);

    /// <summary>Như <see cref="Scaled"/> nhưng cho trường CÓ DẤU (gap âm là chuyện bình thường).</summary>
    private static byte Signed(double value) =>
        unchecked((byte)(sbyte)Math.Clamp(Math.Round(value * 10d), sbyte.MinValue, sbyte.MaxValue));
}
