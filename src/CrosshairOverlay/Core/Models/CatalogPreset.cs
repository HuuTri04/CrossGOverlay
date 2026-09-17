namespace CrosshairOverlay.Core.Models;

/// <summary>
/// Một mẫu trong thư viện dựng sẵn (<c>Data/builtin_presets.json</c>, sinh bởi
/// <c>tools/preset-catalog/scrape_and_convert.py</c>).
/// </summary>
/// <remarks>
/// Chỉ là dữ liệu MÔ TẢ, không phải preset của người dùng: bấm "Dùng mẫu" mới dựng ra một
/// <see cref="CrosshairProfile"/> mới (xem <c>CatalogPresetMapper</c>). Thư viện đọc một lần và giữ nguyên
/// trong RAM, nên đây là record bất biến.
/// </remarks>
public sealed record CatalogPreset
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;

    /// <summary>Khoá danh mục, xem <see cref="CatalogCategories"/>.</summary>
    public string Category { get; init; } = string.Empty;

    /// <summary><c>ClassicCross</c>, <c>TShape</c>, <c>XShape</c>, <c>Dot</c>, <c>Circle</c> hoặc <c>Square</c>.</summary>
    public string ShapeType { get; init; } = string.Empty;

    /// <summary><c>#RRGGBB</c> hoặc <c>#AARRGGBB</c>.</summary>
    public string Color { get; init; } = string.Empty;

    /// <summary>Độ dày nét (nhánh, hoặc vòng/khung).</summary>
    public double Thickness { get; init; }

    /// <summary>Độ dài nhánh; với vòng tròn và khung vuông là BÁN KÍNH.</summary>
    public double Size { get; init; }

    /// <summary>Khoảng cách từ tâm tới đầu nhánh.</summary>
    public double Gap { get; init; }

    public bool HasDot { get; init; }

    public double DotSize { get; init; } = 1d;

    public bool HasOutline { get; init; }

    public double OutlineThickness { get; init; } = 1d;

    public double OutlineOpacity { get; init; } = 1d;

    /// <summary>
    /// Mã chia sẻ Valorant gốc nếu mẫu đến từ một mã. Có mã thì preset được dựng từ mã — giữ đúng cả nhánh ngoài
    /// và độ mờ từng phần mà các trường rút gọn ở trên không mô tả được.
    /// </summary>
    public string? Code { get; init; }

    /// <summary>Nguồn dữ liệu (URL / tên file) để ghi công, nếu có.</summary>
    public string? Source { get; init; }
}

/// <summary>Khoá danh mục dùng trong file thư viện. Tên hiển thị lấy từ tài nguyên ngôn ngữ.</summary>
public static class CatalogCategories
{
    public const string All = "All";
    public const string ProPlayers = "Pro Players";
    public const string Dots = "Dots";
    public const string Crosses = "Crosses";
    public const string Circles = "Circles";
    public const string Tactical = "Tactical";

    /// <summary>Thứ tự hiển thị của các chip lọc; "Tất cả" luôn đứng đầu.</summary>
    public static IReadOnlyList<string> Ordered { get; } = [All, ProPlayers, Dots, Crosses, Circles, Tactical];
}
