using CrosshairOverlay.Core.Models;
using CrosshairOverlay.Services.Rendering;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CrosshairOverlay.Tests;

/// <summary>
/// Phép tính bám lưới pixel trong renderer là thứ quyết định crosshair sắc hay nhoè, và nó
/// sai theo kiểu âm thầm — không ném lỗi, chỉ mờ đi.
/// </summary>
public class CrosshairRendererTests
{
    private readonly CrosshairRenderer _renderer = new(NullLogger<CrosshairRenderer>.Instance);

    private static CrosshairRenderOptions Options(double dpi = 1d, bool snap = true) =>
        new(dpi, snap, 2000d);

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(1.75)]
    [InlineData(2.0)]
    public void Measure_LuonRaSoChanDevicePixel(double dpi)
    {
        // Bất biến nền tảng: bề rộng phải là số CHẴN device pixel, nhờ đó tâm cửa sổ rơi đúng
        // biên pixel và OverlayController có thể dùng Math.Round thay vì Math.Ceiling.
        var size = _renderer.Measure(CrosshairProfile.CreateDefault(), Options(dpi));

        var widthPx = size.Width * dpi;
        var heightPx = size.Height * dpi;

        Assert.Equal(Math.Round(widthPx), widthPx, 6);
        Assert.Equal(Math.Round(heightPx), heightPx, 6);
        Assert.Equal(0, (int)Math.Round(widthPx) % 2);
        Assert.Equal(0, (int)Math.Round(heightPx) % 2);
    }

    [Fact]
    public void Measure_VungBaoLuonVuong()
    {
        var size = _renderer.Measure(CrosshairProfile.CreateDefault(), Options(1.25));
        Assert.Equal(size.Width, size.Height, 6);
    }

    [Fact]
    public void Measure_NhanhDaiHonThiVungBaoLonHon()
    {
        var nho = CrosshairProfile.CreateDefault();
        var to = CrosshairProfile.CreateDefault();
        to.Lines.Length = nho.Lines.Length * 4;

        Assert.True(_renderer.Measure(to, Options()).Width
                    > _renderer.Measure(nho, Options()).Width);
    }

    [Fact]
    public void Measure_HinhXoay_VungBaoRongRaDeKhongBiCatMep()
    {
        var thang = CrosshairProfile.CreateDefault();
        var xoay = CrosshairProfile.CreateDefault();
        xoay.Rotation = 45;

        Assert.True(_renderer.Measure(xoay, Options()).Width
                    > _renderer.Measure(thang, Options()).Width);
    }

    [Fact]
    public void Measure_VienLamVungBaoLonHon()
    {
        var khongVien = CrosshairProfile.CreateDefault();
        khongVien.Outline.Enabled = false;

        var coVien = CrosshairProfile.CreateDefault();
        coVien.Outline.Enabled = true;
        coVien.Outline.Thickness = 4;

        Assert.True(_renderer.Measure(coVien, Options()).Width
                    > _renderer.Measure(khongVien, Options()).Width);
    }

    [Fact]
    public void Measure_TonTrongGioiHanMaxExtent()
    {
        var khongLo = CrosshairProfile.CreateDefault();
        khongLo.Lines.Length = 100000;
        khongLo.Scale = 10;

        var size = _renderer.Measure(khongLo, new CrosshairRenderOptions(1d, true, 200d));

        // MaxExtent là nửa cạnh, nên cạnh tối đa là gấp đôi.
        Assert.True(size.Width <= 400d + 1d);
    }

    [Fact]
    public void Build_TraVeHinhDaDongBang()
    {
        // Đóng băng là điều kiện để cache và dùng lại qua nhiều thread.
        var drawing = _renderer.Build(CrosshairProfile.CreateDefault(), Options());

        Assert.NotNull(drawing);
        Assert.True(drawing.IsFrozen);
    }

    [Fact]
    public void Build_ChayDuocVoiMoiHinhDang()
    {
        foreach (var shape in Enum.GetValues<CrosshairShape>())
        {
            var profile = CrosshairProfile.CreateDefault();
            profile.Shape = shape;
            profile.Ring.Enabled = true;

            var drawing = _renderer.Build(profile, Options(1.25));
            Assert.NotNull(drawing);
        }
    }

    [Fact]
    public void Build_GiaTriCucDoanKhongLamNemLoi()
    {
        var hong = new CrosshairProfile
        {
            Shape = CrosshairShape.Cross,
            Opacity = double.NaN,
            Scale = 0,
            Lines = new CrosshairLines { Length = -5, Thickness = 0, Gap = -10 },
            CenterDot = new CenterDotSettings { Enabled = true, Size = -1 },
            Outline = new OutlineSettings { Enabled = true, Thickness = -3 },
            Ring = new RingSettings { Enabled = true, Radius = -20, Thickness = -1 },
        };

        var drawing = _renderer.Build(hong, Options());
        Assert.NotNull(drawing);
    }

    // ---- quyết định khử răng cưa ----

    [Fact]
    public void ChuThapThang_NenTatKhuRangCua()
    {
        var profile = CrosshairProfile.CreateDefault();
        profile.CenterDot.Size = 2;

        Assert.True(_renderer.PrefersAliasedEdges(profile));
    }

    [Theory]
    [InlineData(CrosshairShape.XShape)]
    [InlineData(CrosshairShape.Circle)]
    [InlineData(CrosshairShape.CircleDot)]
    [InlineData(CrosshairShape.CustomImage)]
    public void HinhCoDuongCongHoacDuongXien_PhaiGiuKhuRangCua(CrosshairShape shape)
    {
        var profile = CrosshairProfile.CreateDefault();
        profile.Shape = shape;

        Assert.False(_renderer.PrefersAliasedEdges(profile));
    }

    [Fact]
    public void HinhDaXoay_PhaiGiuKhuRangCua()
    {
        var profile = CrosshairProfile.CreateDefault();
        profile.Rotation = 30;

        Assert.False(_renderer.PrefersAliasedEdges(profile));
    }

    [Fact]
    public void DauNhanhBoTron_PhaiGiuKhuRangCua()
    {
        var profile = CrosshairProfile.CreateDefault();
        profile.Lines.RoundedCaps = true;

        Assert.False(_renderer.PrefersAliasedEdges(profile));
    }

    [Fact]
    public void ChamGiuaLon_PhaiGiuKhuRangCua()
    {
        // Chấm nhỏ vẽ bằng hình vuông nên vẫn sắc; chấm lớn vẽ bằng hình tròn.
        var nho = CrosshairProfile.CreateDefault();
        nho.CenterDot.Enabled = true;
        nho.CenterDot.Size = 2;

        var lon = CrosshairProfile.CreateDefault();
        lon.CenterDot.Enabled = true;
        lon.CenterDot.Size = 12;

        Assert.True(_renderer.PrefersAliasedEdges(nho));
        Assert.False(_renderer.PrefersAliasedEdges(lon));
    }

    [Fact]
    public void KhungVuongVanDuocTatKhuRangCua()
    {
        var profile = CrosshairProfile.CreateDefault();
        profile.Shape = CrosshairShape.Square;
        profile.Ring.Enabled = true;
        profile.CenterDot.Enabled = false;

        Assert.True(_renderer.PrefersAliasedEdges(profile));
    }
}
