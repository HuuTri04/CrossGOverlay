using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using CrosshairOverlay.Core.Models;
using CrosshairOverlay.Services.Import;
using CrosshairOverlay.Services.Rendering;
using CrosshairOverlay.Services.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CrosshairOverlay.Tests;

/// <summary>
/// Hai lớp nhánh độc lập (nhánh trong / nhánh ngoài) kiểu Valorant: model, định dạng file,
/// renderer và mã chia sẻ.
/// </summary>
public class CrosshairLayersTests
{
    private readonly CrosshairRenderer _renderer = new(NullLogger<CrosshairRenderer>.Instance);

    private static readonly Color Core = Colors.Lime;
    private static readonly Color Edge = Colors.Black;

    /// <summary>Preset tối giản: chỉ bật đúng những gì từng test cần.</summary>
    private static CrosshairProfile Bare() => new()
    {
        Shape = CrosshairShape.Cross,
        Color = Core,
        CenterDot = new CenterDotSettings { Enabled = false },
        Outline = new OutlineSettings { Enabled = false, Color = Edge },
        InnerLines = new LineLayerSettings { Enabled = false },
        OuterLines = new LineLayerSettings { Enabled = false },
    };

    private static CrosshairRenderOptions Options() => new(1d, true, 2000d);

    // ================================================================== model & file

    [Fact]
    public void PresetMoi_NhanhNgoaiMacDinhTat()
    {
        // Preset cũ phải trông y hệt sau khi nâng cấp, nên lớp mới sinh ra phải tắt.
        Assert.True(new CrosshairProfile().InnerLines.Enabled);
        Assert.False(new CrosshairProfile().OuterLines.Enabled);
    }

    [Fact]
    public void HaiLopLaHaiObjectDocLap()
    {
        var profile = new CrosshairProfile();
        profile.InnerLines.Length = 17;

        Assert.NotSame(profile.InnerLines, profile.OuterLines);
        Assert.NotEqual(17d, profile.OuterLines.Length);
    }

    [Fact]
    public void FilePhienBan1_DocDuoc_LinesThanhNhanhTrong_GapThanhOffset()
    {
        // Đúng định dạng file mà mọi người dùng hiện tại đang có trên đĩa.
        const string v1 = """
        {
          "SchemaVersion": 1,
          "Name": "Preset cu",
          "Shape": "Cross",
          "Color": "#FF00FF00",
          "Lines": { "Enabled": true, "Length": 13, "Thickness": 3, "Gap": 7, "ShowTop": false },
          "CenterDot": { "Enabled": true, "Size": 2 }
        }
        """;

        var profile = JsonSerializer.Deserialize<CrosshairProfile>(v1, AppJson.Options)!;

        Assert.Equal(13d, profile.InnerLines.Length);
        Assert.Equal(3d, profile.InnerLines.Thickness);
        Assert.Equal(7d, profile.InnerLines.Offset);
        Assert.False(profile.InnerLines.ShowTop);
        Assert.False(profile.OuterLines.Enabled);
    }

    [Fact]
    public void FileMoi_KhongGhiLaiTenCu()
    {
        var json = JsonSerializer.Serialize(CrosshairProfile.CreateDefault(), AppJson.Options);

        Assert.DoesNotContain("\"Lines\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Gap\"", json, StringComparison.Ordinal);
        Assert.Contains("\"InnerLines\"", json, StringComparison.Ordinal);
        Assert.Contains("\"OuterLines\"", json, StringComparison.Ordinal);
        Assert.Contains("\"Offset\"", json, StringComparison.Ordinal);
        Assert.Contains($"\"SchemaVersion\": {CrosshairProfile.CurrentSchemaVersion}", json, StringComparison.Ordinal);
    }

    [Fact]
    public void GhiRoiDoc_GiuNguyenCaHaiLop()
    {
        var goc = Bare();
        goc.InnerLines = new LineLayerSettings { Enabled = true, Length = 6, Thickness = 3, Offset = 2, Opacity = 0.8, ShowLeft = false };
        goc.OuterLines = new LineLayerSettings { Enabled = true, Length = 4, Thickness = 1, Offset = 25, Opacity = 0.35, RoundedCaps = true };

        var doc = JsonSerializer.Deserialize<CrosshairProfile>(
            JsonSerializer.Serialize(goc, AppJson.Options), AppJson.Options)!;

        Assert.Equal(6d, doc.InnerLines.Length);
        Assert.Equal(0.8d, doc.InnerLines.Opacity);
        Assert.False(doc.InnerLines.ShowLeft);

        Assert.True(doc.OuterLines.Enabled);
        Assert.Equal(4d, doc.OuterLines.Length);
        Assert.Equal(1d, doc.OuterLines.Thickness);
        Assert.Equal(25d, doc.OuterLines.Offset);
        Assert.Equal(0.35d, doc.OuterLines.Opacity);
        Assert.True(doc.OuterLines.RoundedCaps);
    }

    [Fact]
    public void Clone_SaoCaHaiLop_KhongDungChungObject()
    {
        var goc = Bare();
        goc.OuterLines.Enabled = true;
        goc.OuterLines.Offset = 20;

        var clone = goc.Clone();
        clone.OuterLines.Offset = 30;

        Assert.True(clone.OuterLines.Enabled);
        Assert.Equal(20d, goc.OuterLines.Offset);
    }

    // ================================================================== renderer: khoảng cách

    [Fact]
    public void OffsetNhanhNgoai_TinhTuTam_KhongCongDonVaoNhanhTrong()
    {
        var profile = Bare();
        profile.InnerLines = new LineLayerSettings { Length = 10, Thickness = 2, Offset = 4 };
        profile.OuterLines = new LineLayerSettings { Enabled = true, Length = 5, Thickness = 2, Offset = 30 };

        var size = _renderer.Measure(profile, Options());

        // Nhánh ngoài kết thúc ở 30 + 5 = 35 tính TỪ TÂM, cộng nửa độ dày (1) và đệm 2 px.
        // Nếu offset bị cộng dồn sau đầu nhánh trong, nó sẽ kết thúc ở 14 + 30 + 5 = 49.
        Assert.Equal((35d + 1d + 2d) * 2d, size.Width);
    }

    [Fact]
    public void DoiNhanhTrong_KhongLamXeDichNhanhNgoai()
    {
        var a = Bare();
        a.InnerLines = new LineLayerSettings { Length = 3, Thickness = 2, Offset = 2 };
        a.OuterLines = new LineLayerSettings { Enabled = true, Length = 5, Thickness = 2, Offset = 30 };

        var b = a.Clone();
        b.InnerLines.Length = 20;
        b.InnerLines.Offset = 6;

        // Nhánh ngoài vẫn xa nhất, nên vùng bao không đổi.
        Assert.Equal(_renderer.Measure(a, Options()), _renderer.Measure(b, Options()));
    }

    [Fact]
    public void NhanhNgoaiTat_KhongAnhHuongKichThuoc()
    {
        var chiTrong = Bare();
        chiTrong.InnerLines = new LineLayerSettings { Length = 8, Thickness = 2, Offset = 3 };

        var coNgoaiTat = chiTrong.Clone();
        coNgoaiTat.OuterLines = new LineLayerSettings { Enabled = false, Length = 50, Offset = 50 };

        Assert.Equal(_renderer.Measure(chiTrong, Options()), _renderer.Measure(coNgoaiTat, Options()));
    }

    // ================================================================== renderer: thứ tự vẽ

    [Fact]
    public void ThuTuVe_ChamGiua_RoiNhanhTrong_RoiNhanhNgoai()
    {
        var profile = Bare();
        profile.CenterDot = new CenterDotSettings { Enabled = true, Size = 2 };
        profile.InnerLines = new LineLayerSettings { Length = 6, Thickness = 2, Offset = 3, Opacity = 1 };
        profile.OuterLines = new LineLayerSettings { Enabled = true, Length = 2, Thickness = 2, Offset = 12, Opacity = 0.5 };

        var children = ((DrawingGroup)_renderer.Build(profile, Options())).Children;

        // [0] chấm giữa, [1..2] hai đường hình học nhánh trong, [3] nhóm nhánh ngoài.
        Assert.Equal(4, children.Count);
        Assert.IsType<RectangleGeometry>(((GeometryDrawing)children[0]).Geometry);

        for (var i = 1; i <= 2; i++)
            Assert.True(Assert.IsType<StreamGeometry>(((GeometryDrawing)children[i]).Geometry).IsFrozen);

        var outer = Assert.IsType<DrawingGroup>(children[3]);
        Assert.Equal(0.5d, outer.Opacity, 3);
        Assert.Equal(2, outer.Children.Count);
    }

    [Fact]
    public void ToanBoVien_VeTruocMoiPhanLoi()
    {
        var profile = Bare();
        profile.Outline = new OutlineSettings { Enabled = true, Thickness = 1, Opacity = 1, Color = Edge };
        profile.CenterDot = new CenterDotSettings { Enabled = true, Size = 2 };
        profile.InnerLines = new LineLayerSettings { Length = 6, Thickness = 2, Offset = 3 };
        profile.OuterLines = new LineLayerSettings { Enabled = true, Length = 2, Thickness = 2, Offset = 12, Opacity = 0.6 };

        var colors = Flatten(_renderer.Build(profile, Options())).ToList();

        var lastEdge = colors.FindLastIndex(c => c == Edge);
        var firstCore = colors.FindIndex(c => c == Core);

        Assert.True(lastEdge >= 0 && firstCore >= 0);
        Assert.True(lastEdge < firstCore, "Viền của một thành phần đang đè lên lõi của thành phần khác.");
    }

    [Fact]
    public void NhanhVienVaLoi_DungChungStreamGeometryDaDongBang()
    {
        var profile = Bare();
        profile.Outline = new OutlineSettings { Enabled = true, Thickness = 1, Color = Edge };
        profile.InnerLines = new LineLayerSettings { Length = 6, Thickness = 2, Offset = 3 };

        var children = ((DrawingGroup)_renderer.Build(profile, Options())).Children;
        var outline = children.Cast<GeometryDrawing>().Take(2).ToArray();
        var core = children.Cast<GeometryDrawing>().Skip(2).ToArray();

        Assert.Equal(2, outline.Length);
        Assert.Equal(2, core.Length);
        for (var i = 0; i < 2; i++)
        {
            Assert.Same(outline[i].Geometry, core[i].Geometry);
            Assert.True(Assert.IsType<StreamGeometry>(core[i].Geometry).IsFrozen);
        }
    }

    [Fact]
    public void DoiMauVaDoMo_TaiSuDungGeometry_NhungDoiKichThuocThiDungGeometryMoi()
    {
        var profile = Bare();
        profile.InnerLines = new LineLayerSettings { Length = 6, Thickness = 2, Offset = 3 };

        static StreamGeometry FirstArm(Drawing drawing)
        {
            if (drawing is GeometryDrawing { Geometry: StreamGeometry geometry }) return geometry;
            foreach (var child in ((DrawingGroup)drawing).Children)
            {
                if (child is GeometryDrawing { Geometry: StreamGeometry arm }) return arm;
                if (child is DrawingGroup group && group.Children.Count > 0)
                    return FirstArm(group);
            }
            throw new InvalidOperationException("No arm geometry.");
        }

        static Pen FirstPen(Drawing drawing)
        {
            if (drawing is GeometryDrawing { Pen: { } pen }) return pen;
            foreach (var child in ((DrawingGroup)drawing).Children)
            {
                if (child is GeometryDrawing { Pen: { } armPen }) return armPen;
                if (child is DrawingGroup group && group.Children.Count > 0)
                    return FirstPen(group);
            }
            throw new InvalidOperationException("No arm pen.");
        }

        var original = FirstArm(_renderer.Build(profile, Options()));
        var originalPen = FirstPen(_renderer.Build(profile, Options()));
        profile.Color = Colors.Red;
        profile.Opacity = 0.5;
        profile.InnerLines.Opacity = 0.4;
        var recolored = FirstArm(_renderer.Build(profile, Options()));

        Assert.Same(original, recolored);
        Assert.True(recolored.IsFrozen);
        Assert.NotSame(originalPen, FirstPen(_renderer.Build(profile, Options())));
        profile.InnerLines.Length = 12;
        Assert.NotSame(original, FirstArm(_renderer.Build(profile, Options())));
    }

    [Fact]
    public void GeometryCache_KhongTangQua64MucQuaNhieuLanDoiPreset()
    {
        var profile = Bare();
        profile.InnerLines = new LineLayerSettings { Enabled = true, Thickness = 2, Offset = 3 };
        for (var i = 0; i < 200; i++)
        {
            profile.InnerLines.Length = 1 + i;
            _renderer.Build(profile, Options());
        }

        Assert.Equal(64, _renderer.GeometryCacheCount);
    }

    [Fact]
    public void PaintCache_GiuGioiHanQuaNhieuMauVaDoDay()
    {
        var profile = Bare();
        profile.InnerLines = new LineLayerSettings { Enabled = true, Length = 8, Thickness = 2 };
        for (var i = 0; i < 200; i++)
        {
            profile.Color = Color.FromRgb((byte)i, (byte)(255 - i), 100);
            profile.InnerLines.Thickness = 1 + (i % 20);
            _renderer.Build(profile, Options());
        }

        Assert.InRange(_renderer.PaintCacheCount.Brushes, 1, 64);
        Assert.InRange(_renderer.PaintCacheCount.Pens, 1, 128);
    }

    [Fact]
    public void VienNhanhNgoai_NhanDoMoCuaLop()
    {
        var profile = Bare();
        profile.Outline = new OutlineSettings { Enabled = true, Thickness = 1, Opacity = 0.5, Color = Edge };
        profile.OuterLines = new LineLayerSettings { Enabled = true, Length = 2, Thickness = 2, Offset = 12, Opacity = 0.4 };

        var children = ((DrawingGroup)_renderer.Build(profile, Options())).Children;

        // Nhánh ngoài mờ 40% mà viền vẫn đậm 50% thì trên màn hình chỉ thấy bốn khung đen.
        var outlineGroup = Assert.IsType<DrawingGroup>(children[0]);
        Assert.Equal(0.5d * 0.4d, outlineGroup.Opacity, 3);
    }

    [Fact]
    public void DoMoLop_ApChoCaNhom_KhongApTungVach()
    {
        // Áp độ mờ vào từng vạch thì chỗ vạch ngang và dọc chồng nhau gần tâm bị pha màu hai lần.
        var profile = Bare();
        profile.InnerLines = new LineLayerSettings { Length = 6, Thickness = 4, Offset = 0, Opacity = 0.5 };

        var children = ((DrawingGroup)_renderer.Build(profile, Options())).Children;

        var layer = Assert.IsType<DrawingGroup>(Assert.Single(children));
        Assert.Equal(0.5d, layer.Opacity, 3);

        foreach (var drawing in layer.Children.Cast<GeometryDrawing>())
            Assert.Equal(1d, drawing.Pen.Brush.Opacity);
    }

    [Fact]
    public void KieuChuT_BoVachTren_OCaHaiLop()
    {
        var profile = Bare();
        profile.Shape = CrosshairShape.TShape;
        profile.InnerLines = new LineLayerSettings { Length = 6, Thickness = 2, Offset = 3 };
        profile.OuterLines = new LineLayerSettings { Enabled = true, Length = 2, Thickness = 2, Offset = 12, Opacity = 1 };

        var lines = FlattenLines(_renderer.Build(profile, Options())).Count;

        Assert.Equal(3 + 3, lines);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    public void CaHaiLop_VanRaSoChanDevicePixel(double dpi)
    {
        var profile = Bare();
        profile.InnerLines = new LineLayerSettings { Length = 5, Thickness = 3, Offset = 2 };
        profile.OuterLines = new LineLayerSettings { Enabled = true, Length = 3, Thickness = 2, Offset = 11 };

        var widthPx = _renderer.Measure(profile, new CrosshairRenderOptions(dpi, true, 2000d)).Width * dpi;

        Assert.Equal(Math.Round(widthPx), widthPx, 6);
        Assert.Equal(0, (int)Math.Round(widthPx) % 2);
    }

    // ================================================================== mã chia sẻ

    [Fact]
    public void KhoaTienTo1_VaoNhanhNgoai_TienTo0_VaoNhanhTrong()
    {
        Assert.True(ValorantCrosshairCode.TryDecode(
            "0;P;0t;4;0l;5;0o;1;0a;0.9;1t;3;1l;7;1o;15;1a;0.6", out var c, out _));

        Assert.Equal(new ValorantLines { Show = true, Thickness = 4, Length = 5, Offset = 1, Opacity = 0.9, VerticalLength = 6 }, c.Inner);
        Assert.Equal(new ValorantLines { Show = true, Thickness = 3, Length = 7, Offset = 15, Opacity = 0.6, VerticalLength = 2 }, c.Outer);
    }

    [Fact]
    public void NhanhNgoaiVangMat_LayMacDinhCuaNhanhNgoai()
    {
        // Mặc định hai lớp khác hẳn nhau; dùng nhầm mặc định của nhánh trong là sai.
        Assert.True(ValorantCrosshairCode.TryDecode("0;P;d;1", out var c, out _));

        Assert.Equal(ValorantLines.OuterDefault, c.Outer);
        Assert.True(c.Outer.Show);
        Assert.Equal(10d, c.Outer.Offset);
    }

    [Fact]
    public void MaDuocBaoLoi_NhanhNgoaiChuyenDungSangPreset()
    {
        const string code =
            "0;c;1;s;1;P;t;4;o;1;d;1;z;5;a;0.556;0t;10;0l;20;0v;0;0g;1;0o;13;0a;1;0f;0;" +
            "1t;1;1l;1;1v;0;1g;1;1o;14;1a;1;1s;0.064;1e;0.375;S;c;3;s;0.628;o;1";

        Assert.True(ValorantCrosshairCode.TryDecode(code, out var c, out _));
        var profile = CrosshairCodeConverter.ToProfile(c, "x");

        Assert.True(profile.OuterLines.Enabled);
        Assert.Equal(1d, profile.OuterLines.Thickness);
        Assert.Equal(1d, profile.OuterLines.Length);
        Assert.Equal(14d, profile.OuterLines.Offset);
        Assert.Equal(1d, profile.OuterLines.Opacity);

        // 1g;1 + 1v;0: nhánh ngoài cũng chỉ còn vạch ngang.
        Assert.True(profile.OuterLines.SeparateVerticalLength);
        Assert.Equal(0d, profile.OuterLines.VerticalLength);
    }

    [Fact]
    public void TatNhanhNgoaiBang1b0()
    {
        Assert.True(ValorantCrosshairCode.TryDecode("0;P;1b;0", out var c, out _));
        Assert.False(CrosshairCodeConverter.ToProfile(c, "x").OuterLines.Enabled);
    }

    [Fact]
    public void PresetHaiLop_XuatRoiNhapLai_GiuNguyen()
    {
        var goc = Bare();
        goc.Color = Colors.White;
        goc.InnerLines = new LineLayerSettings { Length = 6, Thickness = 2, Offset = 3, Opacity = 0.8 };
        goc.OuterLines = new LineLayerSettings { Enabled = true, Length = 2, Thickness = 2, Offset = 10, Opacity = 0.35 };

        Assert.True(CrosshairCodeConverter.CanExportFaithfully(goc));

        var code = ValorantCrosshairCode.Encode(CrosshairCodeConverter.ToValorantCrosshair(goc));
        Assert.Contains(";1o;10;", code, StringComparison.Ordinal);

        Assert.True(ValorantCrosshairCode.TryDecode(code, out var decoded, out _));
        var back = CrosshairCodeConverter.ToProfile(decoded, "x");

        foreach (var (a, b) in new[] { (goc.InnerLines, back.InnerLines), (goc.OuterLines, back.OuterLines) })
        {
            Assert.Equal(a.Enabled, b.Enabled);
            Assert.Equal(a.Length, b.Length);
            Assert.Equal(a.Thickness, b.Thickness);
            Assert.Equal(a.Offset, b.Offset);
            Assert.Equal(a.Opacity, b.Opacity, 3);
        }
    }

    [Fact]
    public void ChiVachNgang_XuatThanhDoDaiDocBang0_RoiNhapLaiDung()
    {
        var goc = Bare();
        goc.OuterLines = new LineLayerSettings { Enabled = true, Length = 4, Thickness = 2, Offset = 12, ShowTop = false, ShowBottom = false };

        var code = ValorantCrosshairCode.Encode(CrosshairCodeConverter.ToValorantCrosshair(goc));
        Assert.Contains(";1g;1;1v;0", code, StringComparison.Ordinal);

        Assert.True(ValorantCrosshairCode.TryDecode(code, out var decoded, out _));
        var back = CrosshairCodeConverter.ToProfile(decoded, "x").OuterLines;

        // Quay về dưới dạng "độ dài dọc = 0" thay vì cờ ẩn vạch — khác cách lưu nhưng cùng hình.
        Assert.Equal(4d, back.Length);
        Assert.Equal(0d, back.EffectiveVerticalLength);
    }

    [Fact]
    public void BaVach_KhongXuatTronVenDuoc()
    {
        var profile = Bare();
        profile.OuterLines = new LineLayerSettings { Enabled = true, ShowTop = false };

        Assert.False(CrosshairCodeConverter.CanExportFaithfully(profile));
    }

    // ================================================================== độ dài dọc riêng

    /// <summary>Các vạch của lớp theo thứ tự vẽ: trên, dưới, trái, phải (bỏ vạch bị ẩn).</summary>
    private List<(Point StartPoint, Point EndPoint)> Lines(CrosshairProfile profile) =>
        FlattenLines(_renderer.Build(profile, Options()));

    private static List<(Point StartPoint, Point EndPoint)> FlattenLines(Drawing drawing)
    {
        var lines = new List<(Point StartPoint, Point EndPoint)>();
        void Visit(Drawing item)
        {
            if (item is DrawingGroup group)
            {
                foreach (var child in group.Children) Visit(child);
            }
            else if (item is GeometryDrawing { Geometry: StreamGeometry geometry })
            {
                foreach (var figure in geometry.GetFlattenedPathGeometry().Figures)
                {
                    var segment = Assert.IsType<LineSegment>(Assert.Single(figure.Segments));
                    lines.Add((figure.StartPoint, segment.Point));
                }
            }
        }
        Visit(drawing);
        return lines;
    }

    [Fact]
    public void DoDaiDocRieng_VachDocVaVachNgangDaiKhacNhau()
    {
        var profile = Bare();
        profile.InnerLines = new LineLayerSettings
        {
            Length = 6, SeparateVerticalLength = true, VerticalLength = 15, Thickness = 2, Offset = 3,
        };

        var lines = Lines(profile);
        Assert.Equal(4, lines.Count);

        // [0] trên, [1] dưới: 3 → 18. [2] trái, [3] phải: 3 → 9.
        Assert.Equal(15d, Math.Abs(lines[0].EndPoint.Y - lines[0].StartPoint.Y), 6);
        Assert.Equal(15d, Math.Abs(lines[1].EndPoint.Y - lines[1].StartPoint.Y), 6);
        Assert.Equal(6d, Math.Abs(lines[2].EndPoint.X - lines[2].StartPoint.X), 6);
        Assert.Equal(6d, Math.Abs(lines[3].EndPoint.X - lines[3].StartPoint.X), 6);
    }

    [Fact]
    public void TatTachRieng_VachDocDaiBangVachNgang_DuDaDatDoDaiDoc()
    {
        var profile = Bare();
        profile.InnerLines = new LineLayerSettings
        {
            Length = 6, SeparateVerticalLength = false, VerticalLength = 40, Thickness = 2, Offset = 3,
        };

        var lines = Lines(profile);
        Assert.Equal(6d, Math.Abs(lines[0].EndPoint.Y - lines[0].StartPoint.Y), 6);
    }

    [Fact]
    public void DoDaiDocBang0_KhongVeVachDoc_VaKhongLamPhinhVungBao()
    {
        var profile = Bare();
        profile.InnerLines = new LineLayerSettings
        {
            Length = 6, SeparateVerticalLength = true, VerticalLength = 0, Thickness = 2, Offset = 3,
        };

        var lines = Lines(profile);

        Assert.Equal(2, lines.Count);
        Assert.All(lines, l => Assert.Equal(l.StartPoint.Y, l.EndPoint.Y, 6));
    }

    [Fact]
    public void DoDaiNgangBang0_ChiConVachDoc()
    {
        var profile = Bare();
        profile.InnerLines = new LineLayerSettings
        {
            Length = 0, SeparateVerticalLength = true, VerticalLength = 8, Thickness = 2, Offset = 3,
        };

        var lines = Lines(profile);

        Assert.Equal(2, lines.Count);
        Assert.All(lines, l => Assert.Equal(l.StartPoint.X, l.EndPoint.X, 6));
    }

    [Fact]
    public void VungBao_TheoTrucDaiNhat()
    {
        var profile = Bare();
        profile.InnerLines = new LineLayerSettings
        {
            Length = 4, SeparateVerticalLength = true, VerticalLength = 30, Thickness = 2, Offset = 5,
        };

        // Vạch dọc kết thúc ở 5 + 30 = 35, cộng nửa độ dày 1 và đệm 2.
        Assert.Equal((35d + 1d + 2d) * 2d, _renderer.Measure(profile, Options()).Width);
    }

    [Fact]
    public void HinhX_BoQuaDoDaiDocRieng()
    {
        var profile = Bare();
        profile.Shape = CrosshairShape.XShape;
        profile.InnerLines = new LineLayerSettings
        {
            Length = 10, SeparateVerticalLength = true, VerticalLength = 0, Thickness = 2, Offset = 0,
        };

        // Nhánh chéo không có ngang/dọc: đủ bốn nhánh, cùng một độ dài.
        var lines = Lines(profile);
        Assert.Equal(4, lines.Count);
    }

    [Theory]
    [InlineData("0;P;0l;6;0g;1;0v;15")]
    [InlineData("0;P;0l;0;0g;1;0v;9")]
    [InlineData("0;P;0l;20;0g;1;0v;0;1l;3;1g;1;1v;7")]
    public void DoDaiDocRieng_NhapRoiXuat_GiuNguyen(string code)
    {
        Assert.True(ValorantCrosshairCode.TryDecode(code, out var first, out _));
        var profile = CrosshairCodeConverter.ToProfile(first, "x");

        Assert.True(CrosshairCodeConverter.CanExportFaithfully(profile));

        var again = ValorantCrosshairCode.Encode(CrosshairCodeConverter.ToValorantCrosshair(profile));
        Assert.True(ValorantCrosshairCode.TryDecode(again, out var second, out _));

        foreach (var (a, b) in new[] { (first.Inner, second.Inner), (first.Outer, second.Outer) })
        {
            Assert.Equal(a.Length, b.Length);
            Assert.Equal(a.SeparateVerticalLength ? a.VerticalLength : a.Length,
                         b.SeparateVerticalLength ? b.VerticalLength : b.Length);
        }
    }

    [Fact]
    public void DoDaiDoc_GhiRoiDocFile_GiuNguyen()
    {
        var goc = Bare();
        goc.OuterLines = new LineLayerSettings { Enabled = true, Length = 3, SeparateVerticalLength = true, VerticalLength = 11 };

        var json = JsonSerializer.Serialize(goc, AppJson.Options);
        var doc = JsonSerializer.Deserialize<CrosshairProfile>(json, AppJson.Options)!;

        Assert.True(doc.OuterLines.SeparateVerticalLength);
        Assert.Equal(11d, doc.OuterLines.VerticalLength);

        // Thuộc tính tính toán không được lọt vào file.
        Assert.DoesNotContain("EffectiveVerticalLength", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Clone_GiuDoDaiDocRieng()
    {
        var goc = Bare();
        goc.InnerLines.SeparateVerticalLength = true;
        goc.InnerLines.VerticalLength = 13;

        var clone = goc.Clone();

        Assert.True(clone.InnerLines.SeparateVerticalLength);
        Assert.Equal(13d, clone.InnerLines.VerticalLength);
    }

    // ================================================================== màu ghi đè (khi bắn)

    [Fact]
    public void MauGhiDe_DoiMauNhanhVaChamGiua_GiuMauVien_KhongSuaPreset()
    {
        var profile = Bare();
        profile.Outline = new OutlineSettings { Enabled = true, Thickness = 1, Opacity = 1, Color = Edge };
        profile.CenterDot = new CenterDotSettings { Enabled = true, Size = 2, UseProfileColor = false, Color = Colors.Blue };
        profile.InnerLines = new LineLayerSettings { Length = 6, Thickness = 2, Offset = 3 };

        var colors = Flatten(_renderer.Build(profile, Options() with { ColorOverride = Colors.Red })).ToList();

        Assert.Contains(Colors.Red, colors);
        Assert.Contains(Edge, colors);
        Assert.DoesNotContain(Core, colors);
        Assert.DoesNotContain(Colors.Blue, colors);

        Assert.Equal(Core, profile.Color);
        Assert.Equal(Colors.Blue, profile.CenterDot.Color);

        var normal = Flatten(_renderer.Build(profile, Options())).ToList();
        Assert.Contains(Core, normal);
        Assert.DoesNotContain(Colors.Red, normal);
    }

    // ================================================================== tiện ích

    /// <summary>Màu của từng nét theo đúng thứ tự vẽ, đi sâu vào các nhóm lồng nhau.</summary>
    private static IEnumerable<Color> Flatten(Drawing drawing)
    {
        switch (drawing)
        {
            case DrawingGroup group:
                foreach (var child in group.Children)
                foreach (var color in Flatten(child))
                    yield return color;
                break;

            case GeometryDrawing geometry:
                var brush = geometry.Brush as SolidColorBrush ?? geometry.Pen?.Brush as SolidColorBrush;
                if (brush is not null) yield return brush.Color;
                break;
        }
    }
}
