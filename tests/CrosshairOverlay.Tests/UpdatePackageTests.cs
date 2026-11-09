using System.IO;
using System.Text.Json;
using CrosshairOverlay.Services.Updates;
using Xunit;

namespace CrosshairOverlay.Tests;

/// <summary>
/// Cập nhật cho bản phát hành dạng thư mục: chọn đúng gói .zip, tìm đúng thư mục ứng dụng trong gói,
/// và script chép đè cả thư mục thay vì chỉ thay một file .exe.
/// </summary>
public sealed class UpdatePackageTests : IDisposable
{
    private static readonly Version Current = new(1, 0, 0);
    private readonly string _root = Path.Combine(Path.GetTempPath(), "cgo-update-test-" + Guid.NewGuid().ToString("N"));

    public UpdatePackageTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        // Windows can briefly retain a file handle after cmd/robocopy exits.
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(200);
            }
        }
    }

    private static JsonElement Release(string tag, params string[] assetNames)
    {
        var assets = string.Join(",", assetNames.Select(n =>
            $$"""{"name":"{{n}}","browser_download_url":"https://example.com/{{n}}","digest":"sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"}"""));
        var json = $$"""{"tag_name":"{{tag}}","html_url":"https://example.com/release","assets":[{{assets}}]}""";
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    // ================================================================== chọn gói

    [Fact]
    public void BanMoi_CoGoiZip_ChonGoiZip()
    {
        var update = UpdateService.ParseRelease(Release("v1.2.0", "CrossGOverlay-1.2.0-win-x64.zip"), Current);

        Assert.NotNull(update);
        Assert.Equal(new Version(1, 2, 0), update!.Version);
        Assert.EndsWith(".zip", update.DownloadUrl, StringComparison.Ordinal);
        Assert.Equal("https://example.com/release", update.ReleaseUrl);
    }

    [Fact]
    public void CoCaExeVaZip_BoQuaExe_ChonZip()
    {
        var update = UpdateService.ParseRelease(Release("v1.2.0", "CrossGOverlay.exe", "CrossGOverlay-1.2.0-win-x64.zip"), Current);

        Assert.EndsWith("CrossGOverlay-1.2.0-win-x64.zip", update!.DownloadUrl, StringComparison.Ordinal);
    }

    [Fact]
    public void ChiCoExe_KhongCapNhat()
    {
        // Chép một file .exe đè lên bản thư mục chỉ thay được file khởi chạy, mã ứng dụng vẫn là bản cũ.
        Assert.Null(UpdateService.ParseRelease(Release("v1.2.0", "CrossGOverlay.exe"), Current));
    }

    [Fact]
    public void NhieuZip_UuTienGoiWinX64()
    {
        var update = UpdateService.ParseRelease(
            Release("v1.2.0", "source-code.zip", "CrossGOverlay-1.2.0-win-x64.zip", "symbols.zip"), Current);

        Assert.EndsWith("win-x64.zip", update!.DownloadUrl, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("v1.0.0")]
    [InlineData("v0.9.5")]
    [InlineData("khong-phai-phien-ban")]
    public void KhongMoiHonHoacTheLa_KhongCapNhat(string tag)
    {
        Assert.Null(UpdateService.ParseRelease(Release(tag, "CrossGOverlay-win-x64.zip"), Current));
    }

    // ================================================================== tìm thư mục ứng dụng trong gói

    [Fact]
    public void ExeONgayGocGoi_LaGoc()
    {
        File.WriteAllText(Path.Combine(_root, "CrossGOverlay.exe"), "x");
        File.WriteAllText(Path.Combine(_root, "CrossGOverlay.dll"), "x");

        Assert.Equal(_root, UpdateService.FindAppRoot(_root, "CrossGOverlay.exe"));
    }

    [Fact]
    public void ExeTrongMotThuMucCon_LaThuMucCon()
    {
        // Kiểu nén "chuột phải thư mục → Compress": gói chứa đúng một thư mục bên trong.
        var inner = Directory.CreateDirectory(Path.Combine(_root, "CrossGOverlay-1.2.0")).FullName;
        File.WriteAllText(Path.Combine(inner, "CrossGOverlay.exe"), "x");

        Assert.Equal(inner, UpdateService.FindAppRoot(_root, "CrossGOverlay.exe"));
    }

    [Fact]
    public void KhongCoExe_TraNull()
    {
        Directory.CreateDirectory(Path.Combine(_root, "a"));
        Directory.CreateDirectory(Path.Combine(_root, "b"));
        File.WriteAllText(Path.Combine(_root, "readme.txt"), "x");

        Assert.Null(UpdateService.FindAppRoot(_root, "CrossGOverlay.exe"));
    }

    // ================================================================== script chép đè

    [Fact]
    public void Script_ChepCaThuMuc_KhongXoaFileNguoiDung()
    {
        var script = UpdateService.BuildSwapScript(
            @"C:\Games\CrossGOverlay\", @"C:\Temp\stage\CrossGOverlay-1.2.0\", "CrossGOverlay.exe",
            @"C:\Temp\stage", @"C:\Temp\pkg.zip");

        // Dấu \ cuối bị bỏ: trong dòng lệnh, \" bị hiểu là ngoặc kép thoát ký tự.
        Assert.Contains("robocopy \"C:\\Temp\\stage\\CrossGOverlay-1.2.0\" \"C:\\Games\\CrossGOverlay\" /E /IS /IT", script, StringComparison.Ordinal);
        Assert.DoesNotContain("/MIR", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("move /y", script, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("start \"\" \"C:\\Games\\CrossGOverlay\\CrossGOverlay.exe\"", script, StringComparison.Ordinal);
        Assert.Contains("imagename eq CrossGOverlay.exe", script, StringComparison.Ordinal);
        Assert.Contains("chcp 65001", script, StringComparison.Ordinal);
        Assert.Contains("rmdir /s /q \"C:\\Temp\\stage\"", script, StringComparison.Ordinal);

        // cmd.exe tìm nhãn goto sai với file .bat chỉ có LF.
        Assert.DoesNotContain("\n", script.Replace("\r\n", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
    }

    /// <summary>
    /// Chạy THẬT script do ứng dụng sinh ra trên thư mục tạm: bản mới phải đè lên bản cũ, file người dùng
    /// để trong thư mục cài đặt phải còn nguyên, và gói tạm phải được dọn.
    /// </summary>
    [Fact]
    public void Script_ChayThat_GhiDeBanCu_GiuFileNguoiDung_DonGoiTam()
    {
        // "Ứng dụng" giả: một file exe có sẵn của Windows, đổi tên duy nhất để vòng chờ tasklist không
        // nhầm với tiến trình nào đang chạy. hostname.exe in một dòng rồi thoát ngay.
        var exeName = "cgo-update-probe-" + Guid.NewGuid().ToString("N")[..8] + ".exe";
        var probe = Path.Combine(Environment.SystemDirectory, "hostname.exe");

        var install = Directory.CreateDirectory(Path.Combine(_root, "install")).FullName;
        File.Copy(probe, Path.Combine(install, exeName));
        File.WriteAllText(Path.Combine(install, "CrossGOverlay.dll"), "BAN CU");
        File.WriteAllText(Path.Combine(install, "ghi-chu-cua-nguoi-dung.txt"), "dung xoa");
        Directory.CreateDirectory(Path.Combine(install, "presets"));
        File.WriteAllText(Path.Combine(install, "presets", "user.json"), "user preset");
        File.WriteAllText(Path.Combine(install, "settings.json"), "user settings");

        var staging = Directory.CreateDirectory(Path.Combine(_root, "staging")).FullName;
        var appRoot = Directory.CreateDirectory(Path.Combine(staging, "CrossGOverlay-1.2.0")).FullName;
        File.Copy(probe, Path.Combine(appRoot, exeName));
        File.WriteAllText(Path.Combine(appRoot, "CrossGOverlay.dll"), "BAN MOI");
        Directory.CreateDirectory(Path.Combine(appRoot, "presets"));
        File.WriteAllText(Path.Combine(appRoot, "presets", "user.json"), "package preset");
        File.WriteAllText(Path.Combine(appRoot, "settings.json"), "package settings");
        Directory.CreateDirectory(Path.Combine(appRoot, "vi"));
        File.WriteAllText(Path.Combine(appRoot, "vi", "file-moi.resources.dll"), "moi");

        var package = Path.Combine(_root, "pkg.zip");
        File.WriteAllText(package, "zip");

        // Thư mục cài đặt có dấu tiếng Việt: script phải chạy đúng với chcp 65001.
        var vietInstall = Path.Combine(_root, "Trò chơi");
        Directory.Move(install, vietInstall);

        var script = Path.Combine(_root, "update.bat");
        File.WriteAllText(
            script,
            UpdateService.BuildSwapScript(vietInstall, appRoot, exeName, staging, package,
                launchAfterUpdate: false),
            new System.Text.UTF8Encoding(false));

        using (var cmd = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c \"{script}\"",
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!)
        {
            Assert.True(cmd.WaitForExit(60_000), "Script cập nhật không kết thúc trong 60 giây.");
        }

        Assert.Equal("BAN MOI", File.ReadAllText(Path.Combine(vietInstall, "CrossGOverlay.dll")));
        Assert.True(File.Exists(Path.Combine(vietInstall, "vi", "file-moi.resources.dll")), "thư mục con của bản mới phải được chép");
        Assert.Equal("dung xoa", File.ReadAllText(Path.Combine(vietInstall, "ghi-chu-cua-nguoi-dung.txt")));
        Assert.Equal("user preset", File.ReadAllText(Path.Combine(vietInstall, "presets", "user.json")));
        Assert.Equal("user settings", File.ReadAllText(Path.Combine(vietInstall, "settings.json")));

        Assert.False(Directory.Exists(staging), "thư mục giải nén tạm phải được dọn");
        Assert.False(File.Exists(package), "gói .zip tạm phải được dọn");
        Assert.False(File.Exists(script), "script phải tự xoá");
    }

    [Fact]
    public void Script_ThuLaiKhiRobocopyLoi_KhongMoBanChepDo()
    {
        var script = UpdateService.BuildSwapScript(@"C:\A", @"C:\B", "CrossGOverlay.exe", @"C:\S", @"C:\P.zip");

        // robocopy: mã >= 8 là lỗi. Sau 10 lần thất bại, không khởi chạy bản có thể đã chép dở.
        Assert.Contains("if not errorlevel 8 goto launch", script, StringComparison.Ordinal);
        Assert.Contains("if %tries% geq 10 goto copy_failed", script, StringComparison.Ordinal);
        Assert.Contains("if errorlevel 8 goto backup_failed", script, StringComparison.Ordinal);
        Assert.Contains("robocopy \"C:\\S-backup\" \"C:\\A\" /E /IS /IT", script, StringComparison.Ordinal);
        Assert.Contains("if errorlevel 8 goto restore_failed", script, StringComparison.Ordinal);
        Assert.Contains("/XD \"C:\\B\\presets\" \"C:\\B\\userdata\"", script, StringComparison.Ordinal);
        Assert.Contains("/XF \"settings.json\" \"appsettings.json\"", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Script_DeployLoi_KhoiPhucBanCuVaGiuDuLieuNguoiDung()
    {
        var install = Directory.CreateDirectory(Path.Combine(_root, "rollback-install")).FullName;
        var staging = Directory.CreateDirectory(Path.Combine(_root, "rollback-stage")).FullName;
        var source = Directory.CreateDirectory(Path.Combine(staging, "app")).FullName;
        var exeName = "cgo-update-probe-" + Guid.NewGuid().ToString("N")[..8] + ".exe";
        var probe = Path.Combine(Environment.SystemDirectory, "hostname.exe");
        File.Copy(probe, Path.Combine(install, exeName));
        File.Copy(probe, Path.Combine(source, exeName));
        File.WriteAllText(Path.Combine(install, "CrossGOverlay.dll"), "old app");
        File.WriteAllText(Path.Combine(install, "settings.json"), "user data");
        Directory.CreateDirectory(Path.Combine(install, "presets"));
        File.WriteAllText(Path.Combine(install, "presets", "user.json"), "user preset");
        File.WriteAllText(Path.Combine(source, "CrossGOverlay.dll"), "new app");
        File.WriteAllText(Path.Combine(source, "settings.json"), "package data");
        var package = Path.Combine(_root, "rollback.zip");
        File.WriteAllText(package, "zip");
        var script = Path.Combine(_root, "rollback.bat");
        File.WriteAllText(script, UpdateService.BuildSwapScript(install, source, exeName,
            staging, package, maxRetries: 1), new System.Text.UTF8Encoding(false));

        // The old install is readable for backup; the new DLL cannot be opened for deploy.
        using (var lockNewDll = new FileStream(Path.Combine(source, "CrossGOverlay.dll"),
                   FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        using (var cmd = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "cmd.exe", Arguments = $"/c \"{script}\"",
            CreateNoWindow = true, UseShellExecute = false,
            RedirectStandardOutput = true, RedirectStandardError = true,
        })!)
        {
            Assert.True(cmd.WaitForExit(30_000), "Rollback script did not finish in 30 seconds.");
        }

        Assert.Equal("old app", File.ReadAllText(Path.Combine(install, "CrossGOverlay.dll")));
        Assert.Equal("user data", File.ReadAllText(Path.Combine(install, "settings.json")));
        Assert.Equal("user preset", File.ReadAllText(Path.Combine(install, "presets", "user.json")));
        // The test intentionally held a source file open; Windows cannot remove that staging
        // directory until this test releases the lock, even though rollback already completed.
        Assert.False(Directory.Exists(staging + "-backup"));
        Assert.False(File.Exists(script));
    }

    [Fact]
    public void Script_LockedInstalledExe_AbortsBeforeDeployAndPreservesUserData()
    {
        var install = Directory.CreateDirectory(Path.Combine(_root, "locked-install")).FullName;
        var staging = Directory.CreateDirectory(Path.Combine(_root, "locked-stage")).FullName;
        var source = Directory.CreateDirectory(Path.Combine(staging, "app")).FullName;
        var exeName = "cgo-update-probe-" + Guid.NewGuid().ToString("N")[..8] + ".exe";
        var probe = Path.Combine(Environment.SystemDirectory, "hostname.exe");
        File.Copy(probe, Path.Combine(install, exeName));
        File.Copy(probe, Path.Combine(source, exeName));
        File.WriteAllText(Path.Combine(install, "CrossGOverlay.dll"), "old app");
        File.WriteAllText(Path.Combine(source, "CrossGOverlay.dll"), "new app");
        File.WriteAllText(Path.Combine(install, "settings.json"), "user settings");
        Directory.CreateDirectory(Path.Combine(install, "presets"));
        File.WriteAllText(Path.Combine(install, "presets", "user.json"), "user preset");
        var package = Path.Combine(_root, "locked.zip");
        File.WriteAllText(package, "zip");
        var script = Path.Combine(_root, "locked.bat");
        File.WriteAllText(script, UpdateService.BuildSwapScript(install, source, exeName,
            staging, package, maxRetries: 1), new System.Text.UTF8Encoding(false));

        using (var lockedExe = new FileStream(Path.Combine(install, exeName),
                   FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        using (var cmd = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "cmd.exe", Arguments = $"/c \"{script}\"",
            CreateNoWindow = true, UseShellExecute = false,
            RedirectStandardOutput = true, RedirectStandardError = true,
        })!)
        {
            Assert.True(cmd.WaitForExit(30_000), "Update script did not stop after backup failure.");
        }

        Assert.Equal("old app", File.ReadAllText(Path.Combine(install, "CrossGOverlay.dll")));
        Assert.Equal("user settings", File.ReadAllText(Path.Combine(install, "settings.json")));
        Assert.Equal("user preset", File.ReadAllText(Path.Combine(install, "presets", "user.json")));
        Assert.False(File.Exists(script));
    }
}
