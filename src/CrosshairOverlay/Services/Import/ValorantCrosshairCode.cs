using System.Globalization;
using System.Text;
using System.Windows.Media;

namespace CrosshairOverlay.Services.Import;

/// <summary>Giá trị crosshair đọc ra từ mã chia sẻ của Valorant.</summary>
public sealed record ValorantCrosshair
{
    public Color Color { get; init; } = Colors.Cyan;
    public bool HasOutline { get; init; } = true;
    public double OutlineThickness { get; init; } = 1d;
    public double OutlineOpacity { get; init; } = 1d;

    public bool HasCenterDot { get; init; }
    public double CenterDotSize { get; init; } = 2d;
    public double CenterDotOpacity { get; init; } = 1d;

    public bool ShowInnerLines { get; init; } = true;
    public double InnerLineThickness { get; init; } = 2d;
    public double InnerLineLength { get; init; } = 4d;
    public double InnerLineOffset { get; init; } = 2d;
    public double InnerLineOpacity { get; init; } = 1d;
}

/// <summary>
/// Đọc mã chia sẻ crosshair của Valorant, dạng danh sách cặp khoá–giá trị ngăn bằng dấu chấm
/// phẩy: <c>0;P;c;5;o;1;d;1;z;3;0t;4;0l;1;0o;2;0a;1</c>.
/// </summary>
/// <remarks>
/// Chỉ đọc khối <c>P</c> (crosshair chính). Hai khối <c>A</c> (ngắm bằng ADS) và <c>S</c>
/// (súng bắn tỉa) bị bỏ qua vì ứng dụng chỉ vẽ một crosshair duy nhất.
///
/// <para>
/// Bộ đọc dùng DANH SÁCH KHOÁ CHO PHÉP thay vì cố hiểu mọi token. Valorant thỉnh thoảng thêm
/// khoá mới, và mã còn có token lẻ ở đầu (số phiên bản) — cách này khiến những thứ không hiểu
/// bị bỏ qua một cách an toàn thay vì làm lệch toàn bộ phần sau.
/// </para>
/// </remarks>
public static class ValorantCrosshairCode
{
    /// <summary>Bảng màu dựng sẵn của Valorant, chỉ số 0..7; chỉ số 8 nghĩa là màu tuỳ chỉnh.</summary>
    private static readonly Color[] Palette =
    [
        Colors.White,                       // 0
        Color.FromRgb(0x00, 0xFF, 0x00),    // 1 xanh lá
        Color.FromRgb(0x7F, 0xFF, 0x00),    // 2 xanh ngả vàng
        Color.FromRgb(0xDF, 0xFF, 0x00),    // 3 vàng ngả xanh
        Color.FromRgb(0xFF, 0xFF, 0x00),    // 4 vàng
        Color.FromRgb(0x00, 0xFF, 0xFF),    // 5 lục lam
        Color.FromRgb(0xFF, 0x00, 0xFF),    // 6 hồng
        Color.FromRgb(0xFF, 0x00, 0x00),    // 7 đỏ
    ];

    private static readonly HashSet<string> KnownKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "c", "u", "h", "t", "o", "d", "z", "a",
        "0b", "0t", "0l", "0o", "0a",
        "1b", "1t", "1l", "1o", "1a",
    };

    /// <summary>
    /// Chiều ngược lại: dựng mã chia sẻ từ giá trị crosshair.
    /// </summary>
    /// <remarks>
    /// Luôn ghi màu ở dạng tuỳ chỉnh (<c>c;8</c> kèm <c>u;RRGGBBAA</c>) thay vì cố tìm chỉ số
    /// trong bảng màu dựng sẵn — crosshair của ứng dụng này cho phép mọi màu, ép về 8 màu của
    /// Valorant sẽ làm sai màu một cách âm thầm.
    /// </remarks>
    public static string Encode(ValorantCrosshair crosshair)
    {
        ArgumentNullException.ThrowIfNull(crosshair);

        var builder = new StringBuilder();

        // Token lẻ ở đầu là số phiên bản định dạng, rồi tới dấu hiệu khối chính.
        builder.Append("0;P");

        Append(builder, "c", "8");
        Append(builder, "u", ToHex(crosshair.Color));

        Append(builder, "h", Flag(crosshair.HasOutline));
        Append(builder, "t", Number(crosshair.OutlineThickness));
        Append(builder, "o", Number(crosshair.OutlineOpacity));

        Append(builder, "d", Flag(crosshair.HasCenterDot));
        Append(builder, "z", Number(crosshair.CenterDotSize));
        Append(builder, "a", Number(crosshair.CenterDotOpacity));

        Append(builder, "0b", Flag(crosshair.ShowInnerLines));
        Append(builder, "0t", Number(crosshair.InnerLineThickness));
        Append(builder, "0l", Number(crosshair.InnerLineLength));
        Append(builder, "0o", Number(crosshair.InnerLineOffset));
        Append(builder, "0a", Number(crosshair.InnerLineOpacity));

        // Ứng dụng chỉ có một lớp nhánh, nên lớp ngoài luôn tắt.
        Append(builder, "1b", "0");

        return builder.ToString();
    }

    private static void Append(StringBuilder builder, string key, string value) =>
        builder.Append(';').Append(key).Append(';').Append(value);

    private static string Flag(bool value) => value ? "1" : "0";

    /// <summary>Luôn dùng dấu chấm thập phân — mã chia sẻ không phụ thuộc vùng miền.</summary>
    private static string Number(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>Valorant ghi màu theo thứ tự RRGGBBAA.</summary>
    private static string ToHex(Color color) =>
        $"{color.R:X2}{color.G:X2}{color.B:X2}{color.A:X2}";

    public static bool TryDecode(string? code, out ValorantCrosshair crosshair, out string? error)
    {
        crosshair = new ValorantCrosshair();
        error = null;

        if (string.IsNullOrWhiteSpace(code))
        {
            error = Localization.Tr.Get("Import_ErrEmpty");
            return false;
        }

        var tokens = code.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length < 2)
        {
            error = Localization.Tr.Get("Import_ErrBadFormat");
            return false;
        }

        var values = ReadPrimarySection(tokens);
        if (values.Count == 0)
        {
            error = Localization.Tr.Get("Import_ErrNothingRead");
            return false;
        }

        crosshair = Build(values);
        return true;
    }

    private static Dictionary<string, string> ReadPrimarySection(string[] tokens)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Trước khi gặp dấu hiệu khối nào, coi như đang ở khối chính.
        var inPrimary = true;

        for (var i = 0; i < tokens.Length; i++)
        {
            var token = tokens[i];

            if (token.Length == 1 && (token is "P" or "A" or "S"))
            {
                inPrimary = token is "P";
                continue;
            }

            if (!KnownKeys.Contains(token) || i + 1 >= tokens.Length) continue;

            if (inPrimary) values[token] = tokens[i + 1];
            i++;
        }

        return values;
    }

    private static ValorantCrosshair Build(Dictionary<string, string> v) => new()
    {
        Color = ReadColor(v),
        HasOutline = Flag(v, "h", fallback: true),
        OutlineThickness = Number(v, "t", 1d),
        OutlineOpacity = Number(v, "o", 1d),

        HasCenterDot = Flag(v, "d", fallback: false),
        CenterDotSize = Number(v, "z", 2d),
        CenterDotOpacity = Number(v, "a", 1d),

        ShowInnerLines = Flag(v, "0b", fallback: true),
        InnerLineThickness = Number(v, "0t", 2d),
        InnerLineLength = Number(v, "0l", 4d),
        InnerLineOffset = Number(v, "0o", 2d),
        InnerLineOpacity = Number(v, "0a", 1d),
    };

    private static Color ReadColor(Dictionary<string, string> v)
    {
        // Chỉ số 8 nghĩa là "màu tuỳ chỉnh", giá trị thật nằm ở khoá u dạng RRGGBBAA.
        if (v.TryGetValue("u", out var hex))
        {
            var custom = ParseHex(hex);
            if (custom is { } c) return c;
        }

        if (v.TryGetValue("c", out var raw)
            && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index)
            && index >= 0 && index < Palette.Length)
        {
            return Palette[index];
        }

        return Colors.Cyan;
    }

    private static Color? ParseHex(string hex)
    {
        var text = hex.Trim().TrimStart('#');

        // Valorant ghi RRGGBBAA, còn bộ đọc màu của ứng dụng dùng AARRGGBB — phải đảo alpha
        // về đầu, nếu không màu sẽ ra sai hoàn toàn.
        if (text.Length == 8)
            text = string.Concat(text.AsSpan(6, 2), text.AsSpan(0, 6));

        return Storage.JsonColorConverter.Parse(text);
    }

    private static double Number(Dictionary<string, string> v, string key, double fallback) =>
        v.TryGetValue(key, out var raw)
        && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;

    private static bool Flag(Dictionary<string, string> v, string key, bool fallback) =>
        v.TryGetValue(key, out var raw)
            ? raw is "1"
            : fallback;
}
