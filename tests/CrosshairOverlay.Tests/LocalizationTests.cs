using System.Collections;
using System.Globalization;
using System.Resources;
using System.Text.RegularExpressions;
using CrosshairOverlay.Localization;
using Xunit;

namespace CrosshairOverlay.Tests;

/// <summary>
/// Một bản dịch thiếu khoá sẽ không làm sập gì cả — nó chỉ hiện ra chuỗi lạ trên giao diện của
/// người dùng ngôn ngữ đó, thường là sau khi đã phát hành. Đây là lưới an toàn cho việc đó.
/// </summary>
[Collection("Localization")]
public class LocalizationTests
{
    private static readonly ResourceManager Resources =
        new("CrosshairOverlay.Resources.Strings", typeof(TranslationSource).Assembly);

    private static Dictionary<string, string> Read(CultureInfo culture, bool tryParents)
    {
        var set = Resources.GetResourceSet(culture, createIfNotExists: true, tryParents: tryParents);
        Assert.NotNull(set);

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (DictionaryEntry entry in set!)
        {
            if (entry.Key is string key && entry.Value is string value) result[key] = value;
        }

        return result;
    }

    private static Dictionary<string, string> Vietnamese() => Read(CultureInfo.InvariantCulture, tryParents: true);

    private static Dictionary<string, string> English() => Read(new CultureInfo("en"), tryParents: false);

    [Fact]
    public void ThuVienGocCoDuChuoi()
    {
        // Nếu con số này tụt về 0 nghĩa là .resx không được nhúng vào assembly.
        Assert.True(Vietnamese().Count > 100);
    }

    [Fact]
    public void BanTiengAnhKhongThieuKhoaNao()
    {
        var missing = Vietnamese().Keys.Except(English().Keys).OrderBy(k => k, StringComparer.Ordinal).ToList();

        Assert.True(missing.Count == 0, $"Bản tiếng Anh thiếu: {string.Join(", ", missing)}");
    }

    [Fact]
    public void BanTiengAnhKhongCoKhoaThua()
    {
        var extra = English().Keys.Except(Vietnamese().Keys).OrderBy(k => k, StringComparer.Ordinal).ToList();

        Assert.True(extra.Count == 0, $"Bản tiếng Anh dư khoá không còn dùng: {string.Join(", ", extra)}");
    }

    [Fact]
    public void KhongCoChuoiRong()
    {
        foreach (var (language, map) in new[] { ("vi", Vietnamese()), ("en", English()) })
        {
            var empty = map.Where(p => string.IsNullOrWhiteSpace(p.Value)).Select(p => p.Key).ToList();
            Assert.True(empty.Count == 0, $"[{language}] chuỗi rỗng: {string.Join(", ", empty)}");
        }
    }

    [Fact]
    public void ChoTrongDinhDangKhopNhauGiuaHaiNgonNgu()
    {
        // Bản dịch thiếu {0} sẽ nuốt mất thông tin; thừa {1} sẽ ném FormatException lúc chạy.
        var vi = Vietnamese();
        var en = English();

        foreach (var (key, viText) in vi)
        {
            if (!en.TryGetValue(key, out var enText)) continue;

            Assert.True(
                Placeholders(viText).SetEquals(Placeholders(enText)),
                $"Khoá '{key}': chỗ trống lệch nhau — vi [{string.Join(",", Placeholders(viText))}] "
                    + $"vs en [{string.Join(",", Placeholders(enText))}]");
        }
    }

    [Fact]
    public void DoiNgonNgu_DoiChuoiTraVe()
    {
        var previous = TranslationSource.Instance.CurrentCulture;
        try
        {
            TranslationSource.Instance.CurrentCulture = new CultureInfo("vi");
            var viText = Tr.Get("Tab_General");

            TranslationSource.Instance.CurrentCulture = new CultureInfo("en");
            var enText = Tr.Get("Tab_General");

            Assert.Equal("Chung", viText);
            Assert.Equal("General", enText);
        }
        finally
        {
            TranslationSource.Instance.CurrentCulture = previous;
        }
    }

    [Fact]
    public void KhoaKhongTonTai_HienRoRangChuKhongTraVeRong()
    {
        Assert.Equal("!Khoa_Khong_Ton_Tai!", Tr.Get("Khoa_Khong_Ton_Tai"));
    }

    [Theory]
    [InlineData("auto")]
    [InlineData("vi")]
    [InlineData("en")]
    [InlineData("ma-rac-khong-hop-le")]
    [InlineData(null)]
    public void ApNgonNgu_KhongNemLoiVoiMaBatKy(string? code)
    {
        var previous = TranslationSource.Instance.CurrentCulture;
        try
        {
            LanguageCatalog.Apply(code);
            Assert.False(string.IsNullOrEmpty(Tr.Get("Tab_General")));
        }
        finally
        {
            TranslationSource.Instance.CurrentCulture = previous;
        }
    }

    [Fact]
    public void DanhSachNgonNgu_TenRiengLuonVietBangChinhNgonNguDo()
    {
        var all = LanguageCatalog.All;

        Assert.Contains(all, o => o.Value == "vi" && o.Label == "Tiếng Việt");
        Assert.Contains(all, o => o.Value == "en" && o.Label == "English");
        Assert.Contains(all, o => o.Value == LanguageCatalog.Auto);
    }

    private static HashSet<string> Placeholders(string text) =>
        [.. Regex.Matches(text, @"\{(\d+)\}").Select(m => m.Groups[1].Value)];
}
