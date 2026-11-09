using System.Globalization;
using System.Windows.Media;
using CrosshairOverlay.Core.Models;
using CrosshairOverlay.Localization;
using CrosshairOverlay.Services.Export;
using CrosshairOverlay.Services.Import;
using Xunit;

namespace CrosshairOverlay.Tests;

/// <summary>
/// Bộ dịch tâm ngắm đa game: một preset ra mã Valorant, share code CS2, bộ lệnh console và mã nội
/// bộ của ứng dụng.
/// </summary>
[Collection("Localization")]
public sealed class CrosshairTranslatorTests : IDisposable
{
    private readonly CultureInfo _previous = TranslationSource.Instance.CurrentCulture;
    private readonly CrosshairTranslatorService _translator = new();

    public CrosshairTranslatorTests() => TranslationSource.Instance.CurrentCulture = new CultureInfo("vi");

    public void Dispose() => TranslationSource.Instance.CurrentCulture = _previous;

    private static CrosshairProfile Drawn()
    {
        var profile = CrosshairProfile.CreateDefault();
        profile.Name = "Test preset";
        profile.Color = Color.FromRgb(13, 170, 82);
        profile.Opacity = 0.8;
        profile.Shape = CrosshairShape.Cross;
        profile.InnerLines.Enabled = true;
        profile.InnerLines.Length = 10;
        profile.InnerLines.Thickness = 2;
        profile.InnerLines.Offset = 6;
        profile.OuterLines.Enabled = false;
        profile.CenterDot.Enabled = true;
        profile.Outline.Enabled = true;
        profile.Outline.Thickness = 2;
        return profile;
    }

    // ================================================================== Valorant

    [Fact]
    public void MaValorant_DocLaiRaDungThongSo()
    {
        var code = _translator.ToValorantCode(Drawn());

        Assert.StartsWith("0;P", code, StringComparison.Ordinal);
        Assert.True(ValorantCrosshairCode.TryDecode(code, out var decoded, out var error), error);
        Assert.Equal(Color.FromRgb(13, 170, 82), decoded.Color);
        Assert.True(decoded.HasCenterDot);
        Assert.True(decoded.HasOutline);
    }

    // ================================================================== CS2

    [Fact]
    public void ShareCodeCs2_DungDinhDang_VaDocLaiDuoc()
    {
        var profile = Drawn();

        var code = _translator.ToCs2Code(profile);

        Assert.Matches("^CSGO(-[A-Za-z0-9]{5}){5}$", code);
        Assert.True(Cs2ShareCode.TryDecode(code, out var cs2, out var error), error);

        // Hệ số đổi đơn vị: 2 DIP = 1 đơn vị CS2 (xem CrosshairCodeConverter).
        Assert.Equal(5d, cs2.Size, 2);
        Assert.Equal(1d, cs2.Thickness, 2);
        Assert.Equal(-1d, cs2.Gap, 2);
        Assert.True(cs2.HasCenterDot);
        Assert.True(cs2.HasOutline);
        Assert.Equal(13, cs2.Red);
        Assert.Equal(170, cs2.Green);
        Assert.Equal(82, cs2.Blue);
        Assert.Equal(204, cs2.Alpha);   // độ mờ 0.8
        Assert.Equal(Cs2Crosshair.CustomColorIndex, cs2.ColorIndex);
        Assert.Equal(Cs2ShareCodeEncoder.ClassicStaticStyle, cs2.Style);
    }

    [Fact]
    public void ShareCodeCs2_NhapLaiVaoApp_RaDungKichThuocBanDau()
    {
        var profile = Drawn();

        Assert.True(Cs2ShareCode.TryDecode(_translator.ToCs2Code(profile), out var cs2, out _));
        var roundTrip = CrosshairCodeConverter.ToProfile(cs2, "lại");

        Assert.Equal(profile.InnerLines.Length, roundTrip.InnerLines.Length, 2);
        Assert.Equal(profile.InnerLines.Thickness, roundTrip.InnerLines.Thickness, 2);
        Assert.Equal(profile.InnerLines.Offset, roundTrip.InnerLines.Offset, 2);
        Assert.Equal(profile.Color, roundTrip.Color);
        Assert.Equal(profile.Opacity, roundTrip.Opacity, 2);
    }

    [Fact]
    public void KieuChuT_RaCoTStyleTrongMaCs2()
    {
        var profile = Drawn();
        profile.Shape = CrosshairShape.TShape;

        Assert.True(Cs2ShareCode.TryDecode(_translator.ToCs2Code(profile), out var cs2, out _));
        Assert.True(cs2.IsTStyle);
    }

    [Fact]
    public void LenhConsole_DuThongSo_VaDungDauChamPhay()
    {
        var commands = _translator.ToCs2ConsoleCommands(Drawn());

        foreach (var name in new[]
                 {
                     "cl_crosshairstyle", "cl_crosshairsize", "cl_crosshairthickness", "cl_crosshairgap",
                     "cl_crosshairdot", "cl_crosshaircolor_r", "cl_crosshairalpha",
                 })
        {
            Assert.Contains(name + " ", commands, StringComparison.Ordinal);
        }

        Assert.Contains("cl_crosshairsize 5", commands, StringComparison.Ordinal);
        Assert.Contains("cl_crosshairgap -1", commands, StringComparison.Ordinal);
        Assert.Contains("cl_crosshaircolor 5", commands, StringComparison.Ordinal);

        // Dấu chấm thập phân phải là dấu chấm dù máy đang dùng ngôn ngữ nào — console CS2 không
        // hiểu dấu phẩy.
        Assert.DoesNotContain(',', commands);
    }

    // ================================================================== mã nội bộ

    [Fact]
    public void MaNoiBo_GiuNguyenCaNhungGiGameKhongCo()
    {
        var profile = Drawn();
        profile.Ring.Enabled = true;
        profile.Ring.Radius = 24;
        profile.Rotation = 33;
        profile.OuterLines.Enabled = true;
        profile.OuterLines.SeparateVerticalLength = true;
        profile.OuterLines.VerticalLength = 7;
        profile.InnerLines.ShowTop = false;
        profile.InnerLines.RoundedCaps = true;

        var code = _translator.ToAppCode(profile);

        Assert.StartsWith(AppCrosshairCode.CurrentPrefix, code, StringComparison.Ordinal);
        Assert.True(AppCrosshairCode.TryDecode(code, out var copy, out var reason), reason);

        Assert.Equal(profile.Name, copy!.Name);
        Assert.Equal(profile.Color, copy.Color);
        Assert.Equal(profile.Rotation, copy.Rotation, 3);
        Assert.True(copy.Ring.Enabled);
        Assert.Equal(24d, copy.Ring.Radius, 3);
        Assert.True(copy.OuterLines.SeparateVerticalLength);
        Assert.Equal(7d, copy.OuterLines.VerticalLength, 3);
        Assert.False(copy.InnerLines.ShowTop);
        Assert.True(copy.InnerLines.RoundedCaps);

        // Id mới: nhập hai lần không ghi đè lên nhau trong kho preset.
        Assert.NotEqual(profile.Id, copy.Id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("CGO-CH;")]
    [InlineData("0;P;c;5;h;0;f;0")]
    [InlineData("CSGO-Gj9ry-3QQF3-T78kK-onMAf-6DR7B")]
    public void MaNoiBo_KhongPhaiDinhDangNay_TraFalse(string text)
    {
        Assert.False(AppCrosshairCode.TryDecode(text, out var profile, out _));
        Assert.Null(profile);
    }

    [Fact]
    public void MaNoiBo_BiCatHoacSua_BiPhatHien()
    {
        var code = AppCrosshairCode.EncodeLegacy(Drawn());

        foreach (var broken in new[]
                 {
                     code[..^1],                                   // mất một ký tự checksum
                     code[..^12] + code[^11..],                    // mất một ký tự giữa phần dữ liệu
                     code.Replace("CGO-CH;H4sI", "CGO-CH;H4sJ", StringComparison.Ordinal),
                 })
        {
            Assert.False(AppCrosshairCode.TryDecode(broken, out var profile, out _), broken[..20]);
            Assert.Null(profile);
        }
    }

    [Fact]
    public void MaNoiBo_KhongCheoQuaTamNgamAnh()
    {
        var profile = Drawn();
        profile.Type = CrosshairType.Image;
        profile.Image.FilePath = "CustomImages/abc.png";

        Assert.False(AppCrosshairCode.TryDecode(AppCrosshairCode.Encode(profile), out var copy, out var reason));
        Assert.Null(copy);
        Assert.Contains(ImageCrosshairCode.Prefix, reason!, StringComparison.Ordinal);
    }

    // ================================================================== gom cho hộp thoại

    [Fact]
    public void BuildCodes_TamNgamVe_DuBaMaVaLenhConsole()
    {
        var profile = Drawn();

        var codes = _translator.BuildCodes(profile, imageCode: null, imageCodeError: null);

        Assert.Equal(profile.Name, codes.PresetName);
        Assert.StartsWith("0;P", codes.Valorant.Code!, StringComparison.Ordinal);
        Assert.StartsWith("CSGO-", codes.Cs2.Code!, StringComparison.Ordinal);
        Assert.StartsWith(AppCrosshairCode.CurrentPrefix, codes.App.Code!, StringComparison.Ordinal);
        Assert.Contains("cl_crosshairsize", codes.Cs2Console!.Code!, StringComparison.Ordinal);

        // Mỗi ô đều phải có câu nói rõ dán vào đâu và mất gì.
        foreach (var entry in new[] { codes.Valorant, codes.Cs2, codes.App })
            Assert.False(string.IsNullOrWhiteSpace(entry.Note), entry.Title);
    }

    [Fact]
    public void BuildCodes_PresetCoVongTron_BaoTruocLaMaSeKhac()
    {
        var profile = Drawn();
        profile.Ring.Enabled = true;

        var codes = _translator.BuildCodes(profile, null, null);

        Assert.Equal(Tr.Get("Export_ValorantLossy"), codes.Valorant.Note);
        Assert.Equal(Tr.Get("Export_Cs2Lossy"), codes.Cs2.Note);

        // Mã nội bộ vẫn giữ đủ, nên không có cảnh báo mất mát.
        Assert.Equal(Tr.Get("Export_AppNote"), codes.App.Note);
    }

    [Fact]
    public void BuildCodes_TamNgamAnh_ChiCoMaNoiBo()
    {
        var profile = Drawn();
        profile.Type = CrosshairType.Image;

        var codes = _translator.BuildCodes(profile, "CGO-IMG;xxx;c00000000", null);

        Assert.Null(codes.Valorant.Code);
        Assert.Null(codes.Cs2.Code);
        Assert.Null(codes.Cs2Console);
        Assert.Equal(Tr.Get("Export_ImageNotSupported"), codes.Valorant.Note);
        Assert.Equal("CGO-IMG;xxx;c00000000", codes.App.Code);
    }

    [Fact]
    public void BuildCodes_AnhKhongDocDuoc_HienLyDoThayViOTrong()
    {
        var profile = Drawn();
        profile.Type = CrosshairType.Image;

        var codes = _translator.BuildCodes(profile, null, "Không tìm thấy file ảnh.");

        Assert.Null(codes.App.Code);
        Assert.Equal("Không tìm thấy file ảnh.", codes.App.Note);
    }
}
