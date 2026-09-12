using System.Globalization;

namespace CrosshairOverlay.Localization;

/// <summary>Danh sách ngôn ngữ hỗ trợ và cách áp dụng.</summary>
public static class LanguageCatalog
{
    public const string Auto = "auto";

    /// <summary>
    /// Tên từng ngôn ngữ luôn viết BẰNG CHÍNH NGÔN NGỮ ĐÓ — người đang bị kẹt ở ngôn ngữ họ
    /// không đọc được vẫn phải tìm ra mục của mình. Chỉ mục "theo hệ thống" là được dịch.
    /// </summary>
    public static IReadOnlyList<LocalizedOption<string>> All { get; } =
    [
        new(Auto, "Language_Auto"),
        LocalizedOption<string>.Literal("vi", "Tiếng Việt"),
        LocalizedOption<string>.Literal("en", "English"),
    ];

    public static void Apply(string? code) =>
        TranslationSource.Instance.CurrentCulture = Resolve(code);

    /// <summary>Báo cho giao diện đọc lại nhãn của mọi mục.</summary>
    public static void RefreshLabels()
    {
        foreach (var option in All) option.Refresh();
    }

    private static CultureInfo Resolve(string? code)
    {
        if (string.IsNullOrWhiteSpace(code) || string.Equals(code, Auto, StringComparison.OrdinalIgnoreCase))
            return CultureInfo.InstalledUICulture;

        try
        {
            return new CultureInfo(code);
        }
        catch (CultureNotFoundException)
        {
            // settings.json sửa tay có thể chứa mã rác — quay về ngôn ngữ hệ thống.
            return CultureInfo.InstalledUICulture;
        }
    }
}
