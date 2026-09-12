using System.Windows;
using CrosshairOverlay.Core.Models;
using CrosshairOverlay.Services.Process;
using Xunit;

namespace CrosshairOverlay.Tests;

/// <summary>
/// So khớp profile game là logic thuần, không đụng Win32 — phần dễ test nhất của toàn dự án,
/// và cũng là phần mà một lỗi lặng lẽ sẽ khiến preset không bao giờ tự đổi.
/// </summary>
public class GameProfileMatcherTests
{
    private readonly GameProfileMatcher _matcher = new();

    private static ForegroundWindowInfo Window(
        string processName = "cs2.exe",
        string title = "Counter-Strike 2",
        string? path = @"C:\Games\cs2\cs2.exe") =>
        new(
            Handle: 1234,
            ProcessId: 42,
            ProcessName: processName,
            ExecutablePath: path,
            WindowTitle: title,
            Bounds: new Rect(0, 0, 1920, 1080),
            Fullscreen: FullscreenKind.Borderless);

    private static GameProfile Profile(
        string pattern,
        ProcessMatchMode mode = ProcessMatchMode.ProcessName,
        bool enabled = true,
        int priority = 0,
        string name = "rule") =>
        new() { Name = name, Pattern = pattern, MatchMode = mode, Enabled = enabled, Priority = priority };

    [Fact]
    public void KhopTenTienTrinh()
    {
        var match = _matcher.Match(Window(), [Profile("cs2.exe")]);
        Assert.NotNull(match);
    }

    [Fact]
    public void TenTienTrinh_KhongPhanBietHoaThuong()
    {
        var match = _matcher.Match(Window(processName: "CS2.EXE"), [Profile("cs2.exe")]);
        Assert.NotNull(match);
    }

    [Fact]
    public void TenTienTrinh_BoQuaKhoangTrangThua()
    {
        // Người dùng gõ tay vào ô Pattern thì rất dễ dính dấu cách ở đầu/cuối.
        var match = _matcher.Match(Window(), [Profile("  cs2.exe  ")]);
        Assert.NotNull(match);
    }

    [Fact]
    public void TenTienTrinh_KhongKhopMotPhan()
    {
        // "cs2" không được khớp "cs2.exe" — khớp một phần sẽ gây dương tính giả hàng loạt.
        var match = _matcher.Match(Window(), [Profile("cs2")]);
        Assert.Null(match);
    }

    [Fact]
    public void KhopTieuDeCuaSo_ChiCanChua()
    {
        var match = _matcher.Match(
            Window(title: "Counter-Strike 2 — Deathmatch"),
            [Profile("Counter-Strike", ProcessMatchMode.WindowTitleContains)]);

        Assert.NotNull(match);
    }

    [Fact]
    public void KhopDuongDanDayDu()
    {
        var match = _matcher.Match(
            Window(),
            [Profile(@"c:\games\cs2\CS2.EXE", ProcessMatchMode.ExecutablePath)]);

        Assert.NotNull(match);
    }

    [Fact]
    public void BoQuaProfileDaTat()
    {
        var match = _matcher.Match(Window(), [Profile("cs2.exe", enabled: false)]);
        Assert.Null(match);
    }

    [Fact]
    public void BoQuaPatternRong()
    {
        // Rule vừa tạo bằng nút "Thêm trống" có Pattern rỗng — không được khớp mọi thứ.
        var match = _matcher.Match(Window(), [Profile("   ")]);
        Assert.Null(match);
    }

    [Fact]
    public void CuaSoKhongHopLe_KhongKhopGiCa()
    {
        var match = _matcher.Match(ForegroundWindowInfo.Empty, [Profile("cs2.exe")]);
        Assert.Null(match);
    }

    [Fact]
    public void NhieuRuleKhop_LayPriorityNhoNhat()
    {
        var thap = Profile("cs2.exe", priority: 10, name: "thap");
        var cao = Profile("Counter-Strike", ProcessMatchMode.WindowTitleContains, priority: 1, name: "cao");

        var match = _matcher.Match(Window(), [thap, cao]);

        Assert.NotNull(match);
        Assert.Equal("cao", match.Name);
    }

    [Fact]
    public void KhongDocDuocTenExe_VanKhopDuocTheoTieuDe()
    {
        // Game chạy quyền admin: Windows từ chối đọc tên file thực thi, nhưng tiêu đề vẫn đọc được.
        var window = Window(processName: string.Empty, path: null);

        Assert.Null(_matcher.Match(window, [Profile("cs2.exe")]));
        Assert.NotNull(_matcher.Match(
            window, [Profile("Counter-Strike", ProcessMatchMode.WindowTitleContains)]));
    }

    [Fact]
    public void DanhSachRong_TraVeNull()
    {
        Assert.Null(_matcher.Match(Window(), []));
    }
}
