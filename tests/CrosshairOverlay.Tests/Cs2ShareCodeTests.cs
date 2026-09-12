using CrosshairOverlay.Services.Import;
using Xunit;

namespace CrosshairOverlay.Tests;

/// <summary>
/// Định dạng share code không được Valve công bố, nên các vector dưới đây là cách DUY NHẤT để
/// biết bộ giải mã có đúng hay không.
/// </summary>
/// <remarks>
/// Hai mã lấy từ các bộ sưu tập crosshair công khai. Bộ giá trị
/// <c>gap -2 / size 2.5 / thickness 1.1</c> được công bố kèm theo và khớp chính xác mã thứ hai —
/// đó là bằng chứng độc lập cho bảng ánh xạ byte. Các trường còn lại củng cố thêm: cả hai mã
/// đều cho alpha = 255 ở cùng vị trí, và màu đọc ra là đỏ thuần / vàng, đều là màu crosshair
/// rất phổ biến.
/// </remarks>
public class Cs2ShareCodeTests
{
    private const string RedCode = "CSGO-Gj9ry-3QQF3-T78kK-onMAf-6DR7B";
    private const string YellowCode = "CSGO-oU57W-DoxMr-auovM-p6edw-pevvE";

    [Fact]
    public void GiaiMaMaCrosshairDo()
    {
        Assert.True(Cs2ShareCode.TryDecode(RedCode, out var c, out var error), error);

        Assert.Equal(-2d, c.Gap, 3);
        Assert.Equal(1d, c.Size, 3);
        Assert.Equal(0d, c.Thickness, 3);
        Assert.Equal(255, c.Red);
        Assert.Equal(0, c.Green);
        Assert.Equal(0, c.Blue);
        Assert.Equal(255, c.Alpha);
        Assert.Equal(5, c.Style);
        Assert.True(c.HasCenterDot);
    }

    [Fact]
    public void GiaiMaMaCrosshairVang()
    {
        Assert.True(Cs2ShareCode.TryDecode(YellowCode, out var c, out var error), error);

        // Bộ ba này được công bố kèm mã — bằng chứng độc lập cho bảng ánh xạ.
        Assert.Equal(-2d, c.Gap, 3);
        Assert.Equal(2.5d, c.Size, 3);
        Assert.Equal(1.1d, c.Thickness, 3);

        Assert.Equal(250, c.Red);
        Assert.Equal(250, c.Green);
        Assert.Equal(50, c.Blue);
        Assert.Equal(255, c.Alpha);
        Assert.Equal(4, c.Style);
        Assert.False(c.HasCenterDot);
    }

    [Fact]
    public void HaiMaKhacNhau_ChoKetQuaKhacNhau()
    {
        Assert.True(Cs2ShareCode.TryDecode(RedCode, out var a, out _));
        Assert.True(Cs2ShareCode.TryDecode(YellowCode, out var b, out _));

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void CacTruongNamTrongKhoangHopLy()
    {
        foreach (var code in new[] { RedCode, YellowCode })
        {
            Assert.True(Cs2ShareCode.TryDecode(code, out var c, out _));

            Assert.InRange(c.Size, 0d, 20d);
            Assert.InRange(c.Thickness, 0d, 10d);
            Assert.InRange(c.Gap, -10d, 10d);
            Assert.InRange(c.OutlineThickness, 0d, 10d);
            Assert.InRange(c.Style, 0, 5);
        }
    }

    [Fact]
    public void ChapNhanMaKhongCoTienToVaKhongCoGachNoi()
    {
        Assert.True(Cs2ShareCode.TryDecode(RedCode, out var a, out _));
        Assert.True(Cs2ShareCode.TryDecode("Gj9ry3QQF3T78kKonMAf6DR7B", out var b, out _));

        Assert.Equal(a, b);
    }

    [Fact]
    public void ChapNhanKhoangTrangThua() =>
        Assert.True(Cs2ShareCode.TryDecode($"  {RedCode}  ", out _, out _));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("CSGO-")]
    [InlineData("CSGO-IIIII-IIIII-IIIII-IIIII-IIIII")]  // I không có trong bảng mã
    [InlineData("day khong phai ma")]
    public void MaSai_TraVeFalseChuKhongNemLoi(string? code)
    {
        Assert.False(Cs2ShareCode.TryDecode(code, out _, out var error));
        Assert.False(string.IsNullOrEmpty(error));
    }
}
