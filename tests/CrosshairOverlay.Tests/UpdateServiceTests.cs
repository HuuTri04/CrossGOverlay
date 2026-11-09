using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using CrosshairOverlay.Core.Abstractions;
using CrosshairOverlay.Services.Updates;
using CrosshairOverlay.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CrosshairOverlay.Tests;

/// <summary>Gọi GitHub Releases: địa chỉ, header, đọc JSON, tải kèm tiến trình, và hộp thoại cập nhật.</summary>
public sealed class UpdateServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "cgo-update-" + Guid.NewGuid().ToString("N"));

    public UpdateServiceTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    /// <summary>Handler giả: ghi lại request và trả về câu trả lời dựng sẵn — không đụng tới mạng.</summary>
    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(respond(request));
        }
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static string ReleaseJson(string tag, string notes = "- Sửa lỗi A\n- Thêm B", string asset = "CrossGOverlay-9.9.9-win-x64.zip",
        string? digest = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa") => $$"""
        {
          "tag_name": "{{tag}}",
          "html_url": "https://github.com/HuuTri04/CrossGOverlay/releases/tag/{{tag}}",
          "body": {{System.Text.Json.JsonSerializer.Serialize(notes)}},
          "assets": [
            { "name": "{{asset}}", "browser_download_url": "https://example.test/{{asset}}", "digest": {{System.Text.Json.JsonSerializer.Serialize(digest)}} }
          ]
        }
        """;

    private static (UpdateService Service, FakeHandler Handler) Create(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var handler = new FakeHandler(respond);
        return (new UpdateService(NullLogger<UpdateService>.Instance, handler), handler);
    }

    // ================================================================== kiểm tra

    [Fact]
    public void DiaChi_LaApiGithub_DungKho()
    {
        Assert.Equal("HuuTri04", UpdateService.GithubOwner);
        Assert.Equal("CrossGOverlay", UpdateService.GithubRepo);
        Assert.Equal(
            "https://api.github.com/repos/HuuTri04/CrossGOverlay/releases/latest",
            UpdateService.LatestReleaseUrl);
    }

    [Fact]
    public async Task GuiKemUserAgent_KhongCoThiGithubTra403()
    {
        var (service, handler) = Create(_ => Json(ReleaseJson("v9.9.9")));
        using (service)
        {
            await service.CheckAsync();
        }

        var request = Assert.Single(handler.Requests);
        Assert.Equal(UpdateService.LatestReleaseUrl, request.RequestUri!.ToString());
        Assert.Contains("CrossGOverlay-App", request.Headers.UserAgent.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CoBanMoi_DocPhienBan_GhiChu_VaLienKetTai()
    {
        var (service, _) = Create(_ => Json(ReleaseJson("v9.9.9")));
        using (service)
        {
            var result = await service.CheckAsync();

            Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
            Assert.Equal(new Version(9, 9, 9), result.Update!.Version);
            Assert.Equal("- Sửa lỗi A\n- Thêm B", result.Update.Changelog);
            Assert.EndsWith("win-x64.zip", result.Update.DownloadUrl, StringComparison.Ordinal);
            Assert.Equal(new string('a', 64), result.Update.Sha256);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("sha256:bad")]
    [InlineData("md5:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public async Task BanPhatHanhThieuSha256HopLe_KhongMoiCapNhat(string? digest)
    {
        var (service, _) = Create(_ => Json(ReleaseJson("v9.9.9", digest: digest)));
        using (service)
            Assert.Equal(UpdateCheckStatus.Failed, (await service.CheckAsync()).Status);
    }

    [Theory]
    [InlineData("First-release")]     // thẻ thật của bản phát hành thử: không so sánh được với phiên bản đang chạy
    [InlineData("release")]
    [InlineData("")]
    public async Task TheKhongPhaiSoPhienBan_LaKhongXacDinhDuoc_KhongPhaiDaMoiNhat(string tag)
    {
        var (service, _) = Create(_ => Json(ReleaseJson(tag)));
        using (service)
        {
            Assert.Equal(UpdateCheckStatus.Failed, (await service.CheckAsync()).Status);
        }
    }

    [Fact]
    public async Task BanMoiNhungThieuGoiZip_LaKhongXacDinhDuoc()
    {
        var (service, _) = Create(_ => Json(ReleaseJson("v9.9.9", asset: "CrossGOverlay.exe")));
        using (service)
        {
            Assert.Equal(UpdateCheckStatus.Failed, (await service.CheckAsync()).Status);
        }
    }

    [Fact]
    public async Task BanCuHonHoacBangHienTai_LaDaMoiNhat()
    {
        var (service, _) = Create(_ => Json(ReleaseJson("v0.0.1")));
        using (service)
        {
            Assert.Equal(UpdateCheckStatus.UpToDate, (await service.CheckAsync()).Status);
        }
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task MayChuTraLoi_LaKhongKiemTraDuoc_KhongPhaiDaMoiNhat(HttpStatusCode code)
    {
        var (service, _) = Create(_ => new HttpResponseMessage(code) { Content = new StringContent("{}") });
        using (service)
        {
            // Phân biệt rõ: "không hỏi được" KHÔNG được hiển thị thành "bạn đang dùng bản mới nhất".
            Assert.Equal(UpdateCheckStatus.Failed, (await service.CheckAsync()).Status);
        }
    }

    [Fact]
    public async Task MatMangHoacJsonHong_LaKhongKiemTraDuoc_KhongNem()
    {
        var (broken, _) = Create(_ => Json("khong-phai-json"));
        using (broken)
        {
            Assert.Equal(UpdateCheckStatus.Failed, (await broken.CheckAsync()).Status);
        }

        var (offline, _) = Create(_ => throw new HttpRequestException("mất mạng"));
        using (offline)
        {
            Assert.Equal(UpdateCheckStatus.Failed, (await offline.CheckAsync()).Status);
        }
    }

    // ================================================================== tải

    [Fact]
    public async Task Tai_BaoTienTrinhTangDan_KetThucBang1_VaGhiDungNoiDung()
    {
        var payload = new byte[600_000];
        Random.Shared.NextBytes(payload);

        var (service, _) = Create(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) });
        var destination = Path.Combine(_root, "goi.zip");
        var reported = new List<double>();

        using (service)
        {
            await service.DownloadAsync(
                "https://example.test/goi.zip", destination, new Progress2(reported.Add), CancellationToken.None);
        }

        Assert.Equal(payload, await File.ReadAllBytesAsync(destination));
        Assert.True(reported.Count > 3, $"chỉ báo {reported.Count} lần");
        Assert.Equal(1d, reported[^1], 3);
        Assert.Equal([.. reported.Order()], reported);            // không bao giờ lùi
        Assert.All(reported, value => Assert.InRange(value, 0d, 1d));
        Assert.False(File.Exists(destination + ".part"), "file tải dở phải được đổi tên, không để lại");
    }

    /// <summary>Progress đồng bộ: test không có SynchronizationContext để Progress&lt;T&gt; quay về.</summary>
    private sealed class Progress2(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }

    [Fact]
    public async Task TaiLoi_KhongDeLaiFileCut()
    {
        var (service, _) = Create(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var destination = Path.Combine(_root, "hong.zip");

        using (service)
        {
            await Assert.ThrowsAsync<HttpRequestException>(() =>
                service.DownloadAsync("https://example.test/hong.zip", destination, null, CancellationToken.None));
        }

        Assert.False(File.Exists(destination));
    }

    [Fact]
    public async Task TaiZip_KiemTraSha256TruocKhiCongBoFile()
    {
        var payload = Encoding.UTF8.GetBytes("complete package");
        var hash = Convert.ToHexString(SHA256.HashData(payload));
        var (service, _) = Create(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(payload),
        });
        var destination = Path.Combine(_root, "verified.zip");
        using (service)
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => service.DownloadAsync(
                "https://example.test/package.zip", destination, null, CancellationToken.None, new string('0', 64)));
            Assert.False(File.Exists(destination));
            Assert.False(File.Exists(destination + ".part"));

            await service.DownloadAsync("https://example.test/package.zip", destination, null, CancellationToken.None, hash);
        }
        Assert.Equal(payload, await File.ReadAllBytesAsync(destination));
    }

    // ================================================================== hộp thoại

    private sealed class FakeUpdates(bool applied = true, Exception? failure = null) : IUpdateService
    {
        public Version CurrentVersion { get; } = new(1, 0, 0);

        public List<double> Reported { get; } = [];

        public Task<UpdateCheck> CheckAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new UpdateCheck(UpdateCheckStatus.UpToDate));

        public Task<bool> DownloadAndApplyAsync(
            UpdateInfo update, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
        {
            if (failure is not null) return Task.FromException<bool>(failure);

            foreach (var value in new[] { 0.25d, 0.5d, 1d })
            {
                progress?.Report(value);
                Reported.Add(value);
            }

            return Task.FromResult(applied);
        }
    }

    private static UpdateInfo Sample(string notes = "Ghi chú") =>
        new(new Version(2, 0, 0), "https://example.test/a.zip", "https://example.test/release", notes);

    [Fact]
    public void ZipSwapCopiesApplicationWithoutDeletingUserPresets()
    {
        var script = UpdateService.BuildSwapScript(
            @"C:\Apps\CrossGOverlay", @"C:\Staging\CrossGOverlay", "CrossGOverlay.exe",
            @"C:\Staging", @"C:\Staging\update.zip");

        Assert.Contains("robocopy \"C:\\Staging\\CrossGOverlay\" \"C:\\Apps\\CrossGOverlay\" /E /IS /IT", script);
        Assert.DoesNotContain("/MIR", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/XD \"C:\\Staging\\CrossGOverlay\\presets\"", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/XF \"settings.json\"", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("if %tries% geq 10 goto copy_failed", script);
        Assert.Contains(":copy_failed", script);
        Assert.Contains("goto cleanup", script);
        Assert.DoesNotContain("if %tries% geq 10 goto launch", script);
    }

    private static UpdateDialogViewModel Dialog(
        FakeUpdates updates, UpdateInfo? update = null, IAppSettingsService? settings = null) =>
        new(updates, settings ?? new OverlayBehaviorTests.SettingsStub(), update ?? Sample(), NullLogger.Instance);

    // ================================================================== gói phát hành gắn sai phiên bản

    [Theory]
    // thử lên 0.1.1 rồi mà app vẫn 0.1.0 (gói sai) => không tự nhắc nữa
    [InlineData("0.1.1", false)]
    [InlineData("0.1.2", false)]
    [InlineData("0.1.0", true)]      // lần trước thử lên bản cũ hơn: bản mới này vẫn đáng nhắc
    [InlineData(null, true)]
    [InlineData("khong-phai-phien-ban", true)]
    public void TuNhacLucKhoiDong_BoQuaBanDaThuMaKhongLenDuoc(string? lastAttempt, bool expected)
    {
        Assert.Equal(expected, UpdateOfferPolicy.ShouldOfferAutomatically(new Version(0, 1, 1), lastAttempt));
    }

    [Fact]
    public async Task CapNhatNgay_GhiLaiPhienBanDaThu_TruocKhiThoat()
    {
        var settings = new OverlayBehaviorTests.SettingsStub();
        var vm = Dialog(new FakeUpdates(), settings: settings);

        await vm.UpdateNowCommand.ExecuteAsync(null);

        Assert.Equal("2.0.0", settings.Current.LastUpdateAttempt);
    }

    [Fact]
    public async Task HopThoai_HienHaiPhienBan_VaGhiChu()
    {
        var vm = Dialog(new FakeUpdates());

        Assert.Equal("1.0.0", vm.CurrentVersionText);
        Assert.Equal("2.0.0", vm.LatestVersionText);
        Assert.Equal("Ghi chú", vm.Changelog);
        Assert.True(vm.HasChangelog);
        Assert.False(vm.IsDownloading);
        Assert.Empty(vm.ErrorText);
        await Task.CompletedTask;
    }

    [Theory]
    [InlineData(0, 1, 0, 0, "0.1.0")]     // phiên bản assembly luôn có 4 số: không hiện "0.1.0.0"
    [InlineData(1, 2, 3, 4, "1.2.3.4")]
    [InlineData(9, 9, 9, -1, "9.9.9")]    // thẻ phát hành "v9.9.9" chỉ có 3 số
    [InlineData(2, 0, -1, -1, "2.0")]
    public void PhienBan_HienNganGon(int major, int minor, int build, int revision, string expected)
    {
        var version = build < 0 ? new Version(major, minor)
            : revision < 0 ? new Version(major, minor, build)
            : new Version(major, minor, build, revision);

        Assert.Equal(expected, CrosshairOverlay.Core.AppInfo.Short(version));
    }

    [Fact]
    public void HopThoai_KhongCoGhiChu_HienCauThayThe()
    {
        var vm = Dialog(new FakeUpdates(), Sample(notes: "   "));

        Assert.False(string.IsNullOrWhiteSpace(vm.Changelog));
        Assert.False(vm.HasChangelog);   // chữ nghiêng: đây là câu thay thế, không phải nội dung bản phát hành
    }

    [Fact]
    public async Task HopThoai_CapNhatNgay_BaoTienTrinh_RoiYeuCauDongVaThoat()
    {
        // Exercise the asynchronous Progress<T> callback repeatedly in one test
        // host, including the race between command completion and notification.
        for (var run = 0; run < 50; run++)
        {
            var updates = new FakeUpdates(applied: true);
            var vm = Dialog(updates);
            bool? applied = null;
            vm.CloseRequested += (_, value) => applied = value;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var finalProgress = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            vm.PropertyChanged += (_, e) =>
            {
                // The ViewModel publishes one immutable snapshot. Wait until both
                // values describe the same completed update before asserting.
                if (e.PropertyName is nameof(vm.ProgressText) or nameof(vm.ProgressPercent) &&
                    vm.ProgressPercent == 100d && vm.ProgressText == "100%")
                    finalProgress.TrySetResult();
            };

            await vm.UpdateNowCommand.ExecuteAsync(null).WaitAsync(timeout.Token);
            await finalProgress.Task.WaitAsync(timeout.Token);

            Assert.Equal([0.25d, 0.5d, 1d], updates.Reported);
            Assert.True(applied);
            Assert.Equal(100d, vm.ProgressPercent, 3);
            Assert.Equal("100%", vm.ProgressText);
        }
    }

    [Fact]
    public async Task HopThoai_LoiKhiTai_HienLoi_VaMoKhoaNutBam()
    {
        var vm = Dialog(new FakeUpdates(failure: new HttpRequestException("máy chủ từ chối")));
        var closed = false;
        vm.CloseRequested += (_, _) => closed = true;

        await vm.UpdateNowCommand.ExecuteAsync(null);

        Assert.False(closed, "lỗi thì không được đóng hộp thoại");
        Assert.False(vm.IsDownloading);
        Assert.True(vm.UpdateNowCommand.CanExecute(null), "phải cho thử lại");
        Assert.Contains("máy chủ từ chối", vm.ErrorText, StringComparison.Ordinal);
        Assert.Empty(vm.ProgressText);
    }

    [Fact]
    public void HopThoai_DeSau_DongMaKhongThoatApp()
    {
        var vm = Dialog(new FakeUpdates());
        bool? applied = null;
        vm.CloseRequested += (_, value) => applied = value;

        vm.LaterCommand.Execute(null);

        Assert.False(applied);
    }

    [Fact]
    public void HopThoai_DangTai_KhoaCaHaiNut()
    {
        var vm = Dialog(new FakeUpdates());
        vm.IsDownloading = true;

        Assert.False(vm.UpdateNowCommand.CanExecute(null));
        Assert.False(vm.LaterCommand.CanExecute(null));
    }
}
