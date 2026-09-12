using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Media;

namespace CrosshairOverlay.Services.Storage;

/// <summary>
/// Đọc/ghi <see cref="Color"/> dưới dạng chuỗi hex <c>"#AARRGGBB"</c>.
/// </summary>
/// <remarks>
/// Mặc định System.Text.Json sẽ tuần tự hoá <see cref="Color"/> thành một object bốn số
/// (<c>A/R/G/B</c>) kèm cả những property dẫn xuất — vừa dài vừa không sửa tay được.
/// File preset là thứ người dùng chia sẻ cho nhau, nên nó phải đọc được bằng mắt.
/// </remarks>
public sealed class JsonColorConverter : JsonConverter<Color>
{
    public override Color Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException($"Màu phải là chuỗi hex, gặp {reader.TokenType}.");

        var text = reader.GetString();
        return Parse(text) ?? throw new JsonException($"Không đọc được mã màu '{text}'.");
    }

    public override void Write(Utf8JsonWriter writer, Color value, JsonSerializerOptions options) =>
        writer.WriteStringValue($"#{value.A:X2}{value.R:X2}{value.G:X2}{value.B:X2}");

    /// <summary>
    /// Chấp nhận <c>#AARRGGBB</c>, <c>#RRGGBB</c> và <c>#RGB</c>, có hoặc không có dấu '#'.
    /// Trả về null nếu chuỗi không hợp lệ — dùng được cho cả ô nhập màu trong UI.
    /// </summary>
    public static Color? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var hex = text.Trim().TrimStart('#');

        // Dạng rút gọn #RGB -> #RRGGBB
        if (hex.Length == 3)
            hex = string.Concat(hex[0], hex[0], hex[1], hex[1], hex[2], hex[2]);

        if (hex.Length == 6) hex = "FF" + hex;
        if (hex.Length != 8) return null;

        if (!uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var packed))
            return null;

        return Color.FromArgb(
            (byte)((packed >> 24) & 0xFF),
            (byte)((packed >> 16) & 0xFF),
            (byte)((packed >> 8) & 0xFF),
            (byte)(packed & 0xFF));
    }

    /// <summary>Chuỗi hex tương ứng, cùng định dạng với bản ghi JSON.</summary>
    public static string ToHex(Color color) => $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
}
