using System.Globalization;
using System.Text;
using System.Windows.Media;

namespace CrosshairOverlay.Services.Import;

/// <summary>Giá trị crosshair đọc ra từ mã chia sẻ của Valorant.</summary>
/// <remarks>
/// Giá trị khởi tạo của từng property KHÔNG phải tuỳ ý: đó là crosshair mặc định trong game.
/// Valorant chỉ ghi vào mã những key KHÁC mặc định — mã <c>0;P;d;1</c> nghĩa là "mặc định,
/// nhưng bật chấm giữa". Key nào vắng mặt thì phải hiểu là giá trị mặc định của game, nên đặt
/// sai một con số ở đây là giải mã sai mọi mã có lược key đó.
/// </remarks>
public sealed record ValorantCrosshair
{
    /// <summary>Khoá <c>c</c>, mặc định chỉ số 0 = trắng.</summary>
    public Color Color { get; init; } = Colors.White;

    /// <summary>Khoá <c>h</c>, mặc định bật.</summary>
    public bool HasOutline { get; init; } = true;
    public double OutlineThickness { get; init; } = 1d;
    public double OutlineOpacity { get; init; } = 0.5d;

    public bool HasCenterDot { get; init; }
    public double CenterDotSize { get; init; } = 2d;
    public double CenterDotOpacity { get; init; } = 1d;

    /// <summary>Nhánh trong — các khoá <c>0b 0t 0l 0o 0a 0g 0v</c>.</summary>
    public ValorantLines Inner { get; init; } = ValorantLines.InnerDefault;

    /// <summary>Nhánh ngoài — các khoá <c>1b 1t 1l 1o 1a 1g 1v</c>.</summary>
    public ValorantLines Outer { get; init; } = ValorantLines.OuterDefault;
}

/// <summary>
/// Một lớp nhánh trong mã Valorant. Nhánh trong và nhánh ngoài dùng CÙNG bộ khoá, chỉ khác
/// tiền tố: <c>0</c> cho nhánh trong, <c>1</c> cho nhánh ngoài (<c>0t</c> / <c>1t</c> là độ dày…).
/// </summary>
public sealed record ValorantLines
{
    /// <summary>Khoá <c>b</c>: hiển thị.</summary>
    public bool Show { get; init; } = true;

    /// <summary>Khoá <c>t</c>: độ dày.</summary>
    public double Thickness { get; init; }

    /// <summary>Khoá <c>l</c>: độ dài (vạch ngang, và cả vạch dọc nếu không tách riêng).</summary>
    public double Length { get; init; }

    /// <summary>Khoá <c>o</c>: khoảng cách từ TÂM tới điểm bắt đầu vạch.</summary>
    public double Offset { get; init; }

    /// <summary>Khoá <c>a</c>: độ mờ.</summary>
    public double Opacity { get; init; }

    /// <summary>Khoá <c>g</c>: vạch dọc có độ dài riêng, đọc ở <see cref="VerticalLength"/>.</summary>
    public bool SeparateVerticalLength { get; init; }

    /// <summary>Khoá <c>v</c>. Chỉ có nghĩa khi <see cref="SeparateVerticalLength"/> bật.</summary>
    public double VerticalLength { get; init; }

    /// <summary>Nhánh trong của crosshair mặc định trong game.</summary>
    public static ValorantLines InnerDefault { get; } = new()
    {
        Thickness = 2d, Length = 6d, VerticalLength = 6d, Offset = 3d, Opacity = 0.8d,
    };

    /// <summary>Nhánh ngoài của crosshair mặc định trong game — BẬT, mảnh và mờ.</summary>
    public static ValorantLines OuterDefault { get; } = new()
    {
        Thickness = 2d, Length = 2d, VerticalLength = 2d, Offset = 10d, Opacity = 0.35d,
    };
}

/// <summary>
/// Đọc mã chia sẻ crosshair của Valorant, dạng các khối nối tiếp nhau:
/// <c>0;c;1;s;1;P;t;4;o;1;d;1;0t;10;A;...;S;c;3;s;0.628</c>.
/// </summary>
/// <remarks>
/// Cấu trúc mã:
/// <list type="bullet">
///   <item><c>0</c> — khối General (tuỳ chọn chung như "dùng crosshair chính khi ngắm").</item>
///   <item><c>P</c> — crosshair chính. Đây là khối DUY NHẤT được đọc.</item>
///   <item><c>A</c> — crosshair khi ngắm (ADS), <c>S</c> — súng bắn tỉa. Bỏ qua, vì ứng dụng
///         chỉ vẽ một crosshair.</item>
/// </list>
/// Sau mỗi dấu hiệu khối là các cặp <c>key;value</c>.
///
/// <para>
/// Phải phân biệt khối, không được gộp. Nhiều key dùng CHUNG tên giữa các khối nhưng mang
/// nghĩa khác hẳn: <c>c;1</c> trong khối General là một cờ bật/tắt, còn <c>c</c> trong khối P
/// là chỉ số màu. Gộp chung thì cờ đó bị đọc thành "màu xanh lá".
/// </para>
///
/// <para>
/// Mọi số thực đều đọc bằng <see cref="CultureInfo.InvariantCulture"/>. Mã luôn dùng dấu chấm
/// thập phân, còn máy đặt vùng Việt Nam dùng dấu phẩy — đọc theo văn hoá hiện tại thì
/// <c>0.556</c> sẽ không parse được hoặc ra sai.
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

        AppendLines(builder, "0", crosshair.Inner);
        AppendLines(builder, "1", crosshair.Outer);

        return builder.ToString();
    }

    private static void AppendLines(StringBuilder builder, string prefix, ValorantLines lines)
    {
        Append(builder, prefix + "b", Flag(lines.Show));
        Append(builder, prefix + "t", Number(lines.Thickness));
        Append(builder, prefix + "l", Number(lines.Length));
        Append(builder, prefix + "o", Number(lines.Offset));
        Append(builder, prefix + "a", Number(lines.Opacity));

        // Chỉ ghi khi bật: vắng mặt nghĩa là "vạch dọc dài bằng vạch ngang", đúng mặc định.
        if (!lines.SeparateVerticalLength) return;

        Append(builder, prefix + "g", "1");
        Append(builder, prefix + "v", Number(lines.VerticalLength));
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

    /// <summary>Khối trong mã chia sẻ.</summary>
    private enum Section
    {
        General,
        Primary,
        Ads,
        Sniper,
    }

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

        var values = ReadPrimarySection(tokens, out var sawPrimaryMarker);

        // Khối P có mặt mà rỗng là hợp lệ: đó là crosshair chính để nguyên mặc định.
        if (values.Count == 0 && !sawPrimaryMarker)
        {
            error = Localization.Tr.Get("Import_ErrNothingRead");
            return false;
        }

        crosshair = Build(values);
        return true;
    }

    /// <summary>Gom các cặp key–value của khối P; mọi khối khác bị bỏ qua.</summary>
    private static Dictionary<string, string> ReadPrimarySection(string[] tokens, out bool sawPrimaryMarker)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        sawPrimaryMarker = false;

        // Mã không có dấu hiệu khối nào thì coi toàn bộ là crosshair chính.
        var section = Section.Primary;

        var i = 0;
        while (i < tokens.Length)
        {
            var token = tokens[i];

            // Dấu hiệu khối là một token đơn lẻ, không có value đi kèm.
            if (TryReadSectionMarker(token, out var next))
            {
                section = next;
                if (next == Section.Primary) sawPrimaryMarker = true;
                i++;
                continue;
            }

            // Key cụt ở cuối mã (bị cắt khi copy): không có value thì không có gì để đọc.
            if (i + 1 >= tokens.Length) break;

            // Luôn tiến CẢ CẶP, kể cả với key không nhận ra hay khối không đọc. Nhảy từng token
            // thì value của một key lạ sẽ bị xét như một key, và một value trùng tên key sẽ
            // làm lệch toàn bộ phần còn lại.
            if (section == Section.Primary) values[token] = tokens[i + 1];
            i += 2;
        }

        return values;
    }

    /// <summary>
    /// Nhận ra token đánh dấu khối.
    /// </summary>
    /// <remarks>
    /// <c>P</c>, <c>A</c>, <c>S</c> so khớp phân biệt hoa thường: key thật đều viết thường, nên
    /// <c>s</c> (một key) và <c>S</c> (khối bắn tỉa) là hai thứ khác nhau. Token toàn chữ số ở
    /// vị trí key là chỉ số khối General — nhờ đứng ở vị trí key, nó không bao giờ bị nhầm với
    /// value <c>0</c> của một cặp phía trước.
    /// </remarks>
    private static bool TryReadSectionMarker(string token, out Section section)
    {
        switch (token)
        {
            case "P": section = Section.Primary; return true;
            case "A": section = Section.Ads; return true;
            case "S": section = Section.Sniper; return true;
        }

        section = Section.General;
        return token.Length > 0 && token.All(char.IsAsciiDigit);
    }

    /// <summary>
    /// Dựng giá trị crosshair từ các cặp của khối P.
    /// </summary>
    /// <remarks>
    /// Chỉ đọc những key ứng dụng biểu diễn được. Các key còn lại — sai số khi di chuyển/bắn (<c>0m</c>, <c>0f</c>, <c>0s</c>, <c>0e</c>…), làm mờ theo sai
    /// số (<c>f</c>) — được bỏ qua một cách an toàn: không đọc, không ném lỗi, không ảnh hưởng
    /// những key đứng sau chúng.
    /// </remarks>
    private static ValorantCrosshair Build(Dictionary<string, string> v)
    {
        var defaults = new ValorantCrosshair();

        return new ValorantCrosshair
        {
            Color = ReadColor(v, defaults.Color),
            HasOutline = Flag(v, "h", defaults.HasOutline),
            OutlineThickness = Number(v, "t", defaults.OutlineThickness),
            OutlineOpacity = Number(v, "o", defaults.OutlineOpacity),

            HasCenterDot = Flag(v, "d", defaults.HasCenterDot),
            CenterDotSize = Number(v, "z", defaults.CenterDotSize),
            CenterDotOpacity = Number(v, "a", defaults.CenterDotOpacity),

            Inner = ReadLines(v, "0", defaults.Inner),
            Outer = ReadLines(v, "1", defaults.Outer),
        };
    }

    /// <summary>
    /// Đọc một lớp nhánh theo tiền tố: <c>"0"</c> cho nhánh trong, <c>"1"</c> cho nhánh ngoài.
    /// </summary>
    /// <remarks>
    /// Khoá vắng mặt lấy mặc định của CHÍNH lớp đó. Hai lớp có mặc định khác hẳn nhau (nhánh
    /// trong dài 6 cách tâm 3, nhánh ngoài dài 2 cách tâm 10), nên dùng nhầm mặc định của lớp
    /// kia là đọc sai mọi mã có lược khoá.
    /// </remarks>
    private static ValorantLines ReadLines(Dictionary<string, string> v, string prefix, ValorantLines defaults) => new()
    {
        Show = Flag(v, prefix + "b", defaults.Show),
        Thickness = Number(v, prefix + "t", defaults.Thickness),
        Length = Number(v, prefix + "l", defaults.Length),
        Offset = Number(v, prefix + "o", defaults.Offset),
        Opacity = Number(v, prefix + "a", defaults.Opacity),
        SeparateVerticalLength = Flag(v, prefix + "g", defaults.SeparateVerticalLength),
        VerticalLength = Number(v, prefix + "v", defaults.VerticalLength),
    };

    private static Color ReadColor(Dictionary<string, string> v, Color fallback)
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

        return fallback;
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

    /// <summary>
    /// Đọc một số thực. Luôn dùng <see cref="CultureInfo.InvariantCulture"/> — xem remarks của lớp.
    /// </summary>
    /// <remarks>
    /// Giá trị không parse được, NaN hay vô cực đều rơi về mặc định thay vì lọt vào model: một
    /// key hỏng chỉ được phép làm mất đúng key đó, không được làm hỏng cả crosshair.
    /// </remarks>
    private static double Number(Dictionary<string, string> v, string key, double fallback) =>
        v.TryGetValue(key, out var raw)
        && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
        && double.IsFinite(value)
            ? value
            : fallback;

    /// <summary>Cờ bật/tắt: chỉ <c>1</c> và <c>0</c> có nghĩa, giá trị khác giữ mặc định.</summary>
    private static bool Flag(Dictionary<string, string> v, string key, bool fallback) =>
        v.TryGetValue(key, out var raw)
            ? raw switch
            {
                "1" => true,
                "0" => false,
                _ => fallback,
            }
            : fallback;
}
