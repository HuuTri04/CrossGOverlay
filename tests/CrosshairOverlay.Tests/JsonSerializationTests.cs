using System.Text.Json;
using System.Windows.Input;
using System.Windows.Media;
using CrosshairOverlay.Core.Models;
using CrosshairOverlay.Services.Storage;
using Xunit;

namespace CrosshairOverlay.Tests;

/// <summary>
/// File preset là thứ người dùng sửa tay và gửi cho nhau, nên định dạng của nó là một hợp đồng
/// công khai — đổi lặng lẽ sẽ làm hỏng preset người khác đã chia sẻ.
/// </summary>
public class JsonSerializationTests
{
    [Theory]
    [InlineData("#FF00FF00", 0xFF, 0x00, 0xFF, 0x00)]
    [InlineData("#00FF00", 0xFF, 0x00, 0xFF, 0x00)]   // thiếu alpha -> mặc định đục
    [InlineData("00FF00", 0xFF, 0x00, 0xFF, 0x00)]    // thiếu dấu '#'
    [InlineData("#0F0", 0xFF, 0x00, 0xFF, 0x00)]      // dạng rút gọn
    [InlineData("#80FF0000", 0x80, 0xFF, 0x00, 0x00)]
    public void ParseMauChapNhanNhieuDang(string text, byte a, byte r, byte g, byte b)
    {
        var color = JsonColorConverter.Parse(text);

        Assert.NotNull(color);
        Assert.Equal(Color.FromArgb(a, r, g, b), color!.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("khong-phai-mau")]
    [InlineData("#GG0000")]
    [InlineData("#FF00")]
    [InlineData(null)]
    public void ParseMauSai_TraVeNull(string? text) => Assert.Null(JsonColorConverter.Parse(text));

    [Fact]
    public void MauGhiRaDangHexTrongJson()
    {
        var profile = CrosshairProfile.CreateDefault();
        profile.Color = Color.FromArgb(0x80, 0x12, 0x34, 0x56);

        var json = JsonSerializer.Serialize(profile, AppJson.Options);

        Assert.Contains("\"Color\": \"#80123456\"", json);
    }

    [Fact]
    public void EnumGhiRaDangTenChuKhongPhaiSo()
    {
        var profile = CrosshairProfile.CreateDefault();
        profile.Shape = CrosshairShape.TShape;

        var json = JsonSerializer.Serialize(profile, AppJson.Options);

        Assert.Contains("\"Shape\": \"TShape\"", json);
    }

    [Fact]
    public void ThongTinNhanDangDungTruocChiTiet()
    {
        // Mở file ra phải thấy ngay Name/Shape/Color, không phải lội qua các khối con.
        var json = JsonSerializer.Serialize(CrosshairProfile.CreateDefault(), AppJson.Options);

        Assert.True(json.IndexOf("\"Name\"", StringComparison.Ordinal)
                    < json.IndexOf("\"Lines\"", StringComparison.Ordinal));
    }

    [Fact]
    public void PresetGiuNguyenSauMotVongGhiDoc()
    {
        var goc = new CrosshairProfile
        {
            Name = "Bài thử",
            Shape = CrosshairShape.CircleDot,
            Color = Color.FromArgb(0xC0, 0x11, 0x22, 0x33),
            Opacity = 0.75,
            Scale = 1.8,
            Rotation = 45,
            OffsetX = -12,
            OffsetY = 7,
            Lines = new CrosshairLines { Length = 13, Thickness = 3, Gap = 5, ShowTop = false, RoundedCaps = true },
            CenterDot = new CenterDotSettings { Enabled = true, Size = 4, UseProfileColor = false, Color = Colors.Red },
            Outline = new OutlineSettings { Enabled = true, Thickness = 2, Color = Colors.Blue, Opacity = 0.5 },
            Ring = new RingSettings { Enabled = true, Radius = 17, Thickness = 4, Filled = true },
            Image = new CustomImageSettings { FilePath = @"C:\anh.png", Scale = 2.5, Opacity = 0.9 },
        };

        var json = JsonSerializer.Serialize(goc, AppJson.Options);
        var doc = JsonSerializer.Deserialize<CrosshairProfile>(json, AppJson.Options);

        Assert.NotNull(doc);
        Assert.Equal(goc.Name, doc!.Name);
        Assert.Equal(goc.Shape, doc.Shape);
        Assert.Equal(goc.Color, doc.Color);
        Assert.Equal(goc.Opacity, doc.Opacity);
        Assert.Equal(goc.Rotation, doc.Rotation);
        Assert.Equal(goc.OffsetX, doc.OffsetX);
        Assert.False(doc.Lines.ShowTop);
        Assert.True(doc.Lines.RoundedCaps);
        Assert.Equal(goc.CenterDot.Color, doc.CenterDot.Color);
        Assert.False(doc.CenterDot.UseProfileColor);
        Assert.Equal(goc.Outline.Opacity, doc.Outline.Opacity);
        Assert.True(doc.Ring.Filled);
        Assert.Equal(goc.Image.FilePath, doc.Image.FilePath);
    }

    [Fact]
    public void TruongChiSongLucChay_KhongLotVaoJson()
    {
        var binding = new HotkeyBinding
        {
            Action = HotkeyAction.ToggleOverlay,
            Key = Key.X,
            Modifiers = ModifierKeys.Alt,
            IsRegistered = true,
            RegistrationError = "phím đã bị chiếm",
        };

        var json = JsonSerializer.Serialize(binding, AppJson.Options);

        Assert.DoesNotContain("IsRegistered", json);
        Assert.DoesNotContain("RegistrationError", json);
        Assert.DoesNotContain("IsAssigned", json);
    }

    [Fact]
    public void CauHinhChungGiuNguyenSauMotVongGhiDoc()
    {
        var goc = AppSettings.CreateDefault();
        goc.OverlayEnabled = false;
        goc.MonitorSelectionMode = MonitorSelectionMode.Specific;
        goc.TargetMonitorDeviceName = @"\\.\DISPLAY2";
        goc.GameProfiles.Add(new GameProfile { Name = "CS2", Pattern = "cs2.exe", Priority = 3 });

        var json = JsonSerializer.Serialize(goc, AppJson.Options);
        var doc = JsonSerializer.Deserialize<AppSettings>(json, AppJson.Options);

        Assert.NotNull(doc);
        Assert.False(doc!.OverlayEnabled);
        Assert.Equal(MonitorSelectionMode.Specific, doc.MonitorSelectionMode);
        Assert.Equal(@"\\.\DISPLAY2", doc.TargetMonitorDeviceName);
        Assert.Equal(goc.Hotkeys.Count, doc.Hotkeys.Count);
        Assert.Single(doc.GameProfiles);
        Assert.Equal("cs2.exe", doc.GameProfiles[0].Pattern);
        Assert.Equal(3, doc.GameProfiles[0].Priority);
    }

    [Fact]
    public void TruongLa_BiBoQuaChuKhongNemLoi()
    {
        // Preset do phiên bản mới hơn tạo ra vẫn phải mở được ở bản cũ.
        const string json = """
            { "Name": "Tương lai", "Shape": "Cross", "TinhNangMoi": { "a": 1 }, "Color": "#FF00FF00" }
            """;

        var doc = JsonSerializer.Deserialize<CrosshairProfile>(json, AppJson.Options);

        Assert.NotNull(doc);
        Assert.Equal("Tương lai", doc!.Name);
    }
}
