using System.Globalization;
using System.Windows.Media;
using CrosshairOverlay.Core.Models;
using CrosshairOverlay.Services.Import;
using Xunit;

namespace CrosshairOverlay.Tests;

public class ValorantCrosshairCodeTests
{
    /// <summary>Một mã thật, đủ cả khối nhánh trong lẫn nhánh ngoài.</summary>
    private const string RealCode =
        "0;P;c;5;o;1;d;1;z;3;f;0;0t;4;0l;1;0o;2;0a;1;0f;0;1t;0;1l;0;1o;0;1a;0;1m;0;1f;0";

    [Fact]
    public void DocDungKhoiCrosshairChinh()
    {
        Assert.True(ValorantCrosshairCode.TryDecode(RealCode, out var c, out var error), error);

        Assert.Equal(Color.FromRgb(0x00, 0xFF, 0xFF), c.Color);   // c;5 = lục lam
        Assert.True(c.HasCenterDot);                              // d;1
        Assert.Equal(3d, c.CenterDotSize);                        // z;3
        Assert.Equal(4d, c.InnerLineThickness);                   // 0t;4
        Assert.Equal(1d, c.InnerLineLength);                      // 0l;1
        Assert.Equal(2d, c.InnerLineOffset);                      // 0o;2
        Assert.Equal(1d, c.InnerLineOpacity);                     // 0a;1
    }

    [Fact]
    public void TokenLeODau_KhongLamLechCacCapSau()
    {
        // Mã bắt đầu bằng một token "0" đơn lẻ rồi mới tới "P". Nếu bộ đọc ghép nhầm "0" với
        // "P" thành một cặp thì MỌI khoá sau đó lệch đi một vị trí.
        Assert.True(ValorantCrosshairCode.TryDecode(RealCode, out var c, out _));
        Assert.Equal(4d, c.InnerLineThickness);
    }

    [Fact]
    public void ChiDocKhoiP_BoQuaKhoiADSVaSniper()
    {
        const string code = "0;P;0l;4;A;c;7;0l;99;S;0l;50";

        Assert.True(ValorantCrosshairCode.TryDecode(code, out var c, out _));

        Assert.Equal(4d, c.InnerLineLength);                      // của khối P
        Assert.NotEqual(Color.FromRgb(0xFF, 0, 0), c.Color);      // c;7 của khối A bị bỏ qua
    }

    [Theory]
    [InlineData(0, 0xFF, 0xFF, 0xFF)]
    [InlineData(1, 0x00, 0xFF, 0x00)]
    [InlineData(4, 0xFF, 0xFF, 0x00)]
    [InlineData(5, 0x00, 0xFF, 0xFF)]
    [InlineData(7, 0xFF, 0x00, 0x00)]
    public void BangMauDungSan(int index, byte r, byte g, byte b)
    {
        Assert.True(ValorantCrosshairCode.TryDecode($"0;P;c;{index}", out var c, out _));
        Assert.Equal(Color.FromRgb(r, g, b), c.Color);
    }

    [Fact]
    public void MauTuyChinh_DaoAlphaVeDauChoDung()
    {
        // Valorant ghi RRGGBBAA; nếu không đảo về AARRGGBB thì màu ra sai hoàn toàn.
        Assert.True(ValorantCrosshairCode.TryDecode("0;P;c;8;u;FF8800FF", out var c, out _));

        Assert.Equal(0xFF, c.Color.R);
        Assert.Equal(0x88, c.Color.G);
        Assert.Equal(0x00, c.Color.B);
    }

    [Fact]
    public void KhoaLa_BiBoQuaKhongLamHongPhanConLai()
    {
        Assert.True(ValorantCrosshairCode.TryDecode("0;P;khoaMoi;123;0l;7", out var c, out _));
        Assert.Equal(7d, c.InnerLineLength);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("0")]
    public void MaSai_TraVeFalse(string? code)
    {
        Assert.False(ValorantCrosshairCode.TryDecode(code, out _, out var error));
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void ChuyenSangPresetGiuDungCauTruc()
    {
        Assert.True(ValorantCrosshairCode.TryDecode(RealCode, out var c, out _));

        var profile = CrosshairCodeConverter.ToProfile(c, "Thử");

        Assert.Equal("Thử", profile.Name);
        Assert.Equal(CrosshairShape.Cross, profile.Shape);
        Assert.Equal(Color.FromRgb(0x00, 0xFF, 0xFF), profile.Color);
        Assert.True(profile.CenterDot.Enabled);
        Assert.Equal(4d, profile.Lines.Thickness);
        Assert.Equal(2d, profile.Lines.Gap);
    }

    // ---- bộ mã hoá (chiều xuất) ----

    [Fact]
    public void MaHoaRoiGiaiMa_GiuNguyenGiaTri()
    {
        var goc = new ValorantCrosshair
        {
            Color = Color.FromArgb(0xCC, 0xFF, 0x69, 0xB4),
            HasOutline = true,
            OutlineThickness = 2,
            OutlineOpacity = 0.75,
            HasCenterDot = true,
            CenterDotSize = 3,
            CenterDotOpacity = 0.5,
            ShowInnerLines = true,
            InnerLineThickness = 2,
            InnerLineLength = 6,
            InnerLineOffset = 3,
            InnerLineOpacity = 0.9,
        };

        var code = ValorantCrosshairCode.Encode(goc);

        Assert.True(ValorantCrosshairCode.TryDecode(code, out var doc, out var error), error);
        Assert.Equal(goc, doc);
    }

    [Fact]
    public void MaHoa_DungDinhDangMaChiaSe()
    {
        var code = ValorantCrosshairCode.Encode(new ValorantCrosshair());

        // Mở đầu bằng token phiên bản rồi tới dấu hiệu khối chính.
        Assert.StartsWith("0;P;", code, StringComparison.Ordinal);

        // Màu luôn ghi ở dạng tuỳ chỉnh, không ép về bảng 8 màu của Valorant.
        Assert.Contains(";c;8;", code, StringComparison.Ordinal);
        Assert.Contains(";u;", code, StringComparison.Ordinal);
    }

    [Fact]
    public void MaHoa_DungDauChamThapPhanBatKeVungMien()
    {
        var previous = Thread.CurrentThread.CurrentCulture;
        try
        {
            // Vùng miền dùng dấu phẩy thập phân sẽ sinh ra mã hỏng nếu bộ mã hoá quên
            // InvariantCulture.
            Thread.CurrentThread.CurrentCulture = new CultureInfo("vi-VN");

            var code = ValorantCrosshairCode.Encode(new ValorantCrosshair { InnerLineOpacity = 0.5 });

            Assert.Contains("0a;0.5", code, StringComparison.Ordinal);
            Assert.DoesNotContain("0,5", code, StringComparison.Ordinal);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previous;
        }
    }

    [Fact]
    public void PresetSangMaRoiVePreset_GiuNguyenPhanBieuDienDuoc()
    {
        var goc = new CrosshairProfile
        {
            Shape = CrosshairShape.Cross,
            Color = Color.FromRgb(0xFF, 0x69, 0xB4),
            Opacity = 0.8,
            Lines = new CrosshairLines { Enabled = true, Length = 7, Thickness = 3, Gap = 4 },
            CenterDot = new CenterDotSettings { Enabled = true, Size = 2, Opacity = 1 },
            Outline = new OutlineSettings { Enabled = true, Thickness = 1, Opacity = 1 },
        };

        var code = ValorantCrosshairCode.Encode(CrosshairCodeConverter.ToValorantCrosshair(goc));

        Assert.True(ValorantCrosshairCode.TryDecode(code, out var decoded, out _));
        var back = CrosshairCodeConverter.ToProfile(decoded, "vòng lại");

        Assert.Equal(goc.Lines.Length, back.Lines.Length);
        Assert.Equal(goc.Lines.Thickness, back.Lines.Thickness);
        Assert.Equal(goc.Lines.Gap, back.Lines.Gap);
        Assert.Equal(goc.Color.R, back.Color.R);
        Assert.Equal(goc.Color.G, back.Color.G);
        Assert.Equal(goc.Color.B, back.Color.B);
        Assert.True(back.CenterDot.Enabled);
        Assert.True(back.Outline.Enabled);
    }

    [Fact]
    public void ChuThapThuong_XuatDuocTronVen()
    {
        var profile = CrosshairProfile.CreateDefault();
        Assert.True(CrosshairCodeConverter.CanExportFaithfully(profile));
    }

    [Theory]
    [InlineData(CrosshairShape.Circle, 0d, 1d, false)]
    [InlineData(CrosshairShape.XShape, 0d, 1d, false)]
    [InlineData(CrosshairShape.Cross, 45d, 1d, false)]
    [InlineData(CrosshairShape.Cross, 0d, 2d, false)]
    [InlineData(CrosshairShape.Cross, 0d, 1d, true)]
    public void CanhBaoKhiPresetCoPhanKhongBieuDienDuoc(
        CrosshairShape shape, double rotation, double scale, bool expected)
    {
        var profile = CrosshairProfile.CreateDefault();
        profile.Shape = shape;
        profile.Rotation = rotation;
        profile.Scale = scale;

        Assert.Equal(expected, CrosshairCodeConverter.CanExportFaithfully(profile));
    }

    [Fact]
    public void VongTronBatKem_CungLamMatDuLieu()
    {
        var profile = CrosshairProfile.CreateDefault();
        profile.Ring.Enabled = true;

        Assert.False(CrosshairCodeConverter.CanExportFaithfully(profile));
    }

    [Fact]
    public void ChuyenCS2SangPreset_KieuChuTBoNhanhTren()
    {
        Assert.True(Cs2ShareCode.TryDecode("CSGO-Gj9ry-3QQF3-T78kK-onMAf-6DR7B", out var cs2, out _));

        var profile = CrosshairCodeConverter.ToProfile(cs2 with { IsTStyle = true }, "T");

        Assert.Equal(CrosshairShape.TShape, profile.Shape);
        Assert.False(profile.Lines.ShowTop);
    }
}
