using System.Globalization;
using System.Windows.Input;
using CrosshairOverlay.Core.Models;
using CrosshairOverlay.Localization;
using Xunit;

namespace CrosshairOverlay.Tests;

/// <summary>
/// <see cref="TranslationSource"/> là singleton toàn ứng dụng, nên các test đụng tới ngôn ngữ
/// phải chạy tuần tự — nếu không chúng sẽ giẫm lên nhau khi xUnit chạy song song.
/// </summary>
[CollectionDefinition("Localization", DisableParallelization = true)]
public sealed class LocalizationCollection
{
}

/// <summary>
/// <c>Key.ToString()</c> cho ra "Oem6" thay vì "]". Đây là lớp chắn để những chuỗi đó không
/// quay lại giao diện.
/// </summary>
[Collection("Localization")]
public class KeyNamesTests
{
    [Theory]
    [InlineData(Key.OemCloseBrackets, "]")]
    [InlineData(Key.OemOpenBrackets, "[")]
    [InlineData(Key.OemSemicolon, ";")]
    [InlineData(Key.OemQuotes, "'")]
    [InlineData(Key.OemComma, ",")]
    [InlineData(Key.OemPeriod, ".")]
    [InlineData(Key.OemQuestion, "/")]
    [InlineData(Key.OemPipe, "\\")]
    [InlineData(Key.OemMinus, "-")]
    [InlineData(Key.OemPlus, "=")]
    [InlineData(Key.OemTilde, "`")]
    public void PhimKyHieuHienDungKyTuInTrenBanPhim(Key key, string expected) =>
        Assert.Equal(expected, KeyNames.Label(key));

    [Theory]
    [InlineData(Key.D0, "0")]
    [InlineData(Key.D5, "5")]
    [InlineData(Key.D9, "9")]
    public void HangSoTrenCung_BoTienToD(Key key, string expected) =>
        Assert.Equal(expected, KeyNames.Label(key));

    [Theory]
    [InlineData(Key.NumPad0, "Num 0")]
    [InlineData(Key.NumPad7, "Num 7")]
    [InlineData(Key.Add, "Num +")]
    [InlineData(Key.Divide, "Num /")]
    public void PhimSoBenPhai(Key key, string expected) =>
        Assert.Equal(expected, KeyNames.Label(key));

    [Theory]
    [InlineData(Key.Return, "Enter")]
    [InlineData(Key.Escape, "Esc")]
    [InlineData(Key.Back, "Backspace")]
    [InlineData(Key.Prior, "Page Up")]
    [InlineData(Key.Next, "Page Down")]
    public void PhimDieuKhien_DungTenQuenThuoc(Key key, string expected) =>
        Assert.Equal(expected, KeyNames.Label(key));

    [Theory]
    [InlineData(Key.F1, "F1")]
    [InlineData(Key.A, "A")]
    [InlineData(Key.Space, "Space")]
    public void PhimThongThuong_GiuNguyenTen(Key key, string expected) =>
        Assert.Equal(expected, KeyNames.Label(key));

    [Fact]
    public void MoTaTohopDayDu()
    {
        var text = KeyNames.Describe(
            ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift, Key.OemCloseBrackets);

        Assert.Equal("Ctrl + Alt + Shift + ]", text);
    }

    [Fact]
    public void ThuTuPhimBoTro_LuonCoDinh()
    {
        // Thứ tự phải ổn định bất kể cờ được truyền vào theo trật tự nào.
        var a = KeyNames.Describe(ModifierKeys.Alt | ModifierKeys.Control, Key.X);
        var b = KeyNames.Describe(ModifierKeys.Control | ModifierKeys.Alt, Key.X);

        Assert.Equal(a, b);
        Assert.Equal("Ctrl + Alt + X", a);
    }

    [Theory]
    [InlineData("vi", "(chưa gán)")]
    [InlineData("en", "(unassigned)")]
    public void ChuaGan_CoNhanRieng_TheoTungNgonNgu(string culture, string expected)
    {
        var previous = TranslationSource.Instance.CurrentCulture;
        try
        {
            TranslationSource.Instance.CurrentCulture = new CultureInfo(culture);

            Assert.Equal(expected, KeyNames.Label(Key.None));
            Assert.Equal(expected, KeyNames.Describe(ModifierKeys.Alt, Key.None));
        }
        finally
        {
            TranslationSource.Instance.CurrentCulture = previous;
        }
    }

    [Fact]
    public void HotkeyBinding_DungChungBoDinhDang()
    {
        var binding = new HotkeyBinding
        {
            Modifiers = ModifierKeys.Alt,
            Key = Key.OemOpenBrackets,
        };

        Assert.Equal("Alt + [", binding.ToString());
    }
}
