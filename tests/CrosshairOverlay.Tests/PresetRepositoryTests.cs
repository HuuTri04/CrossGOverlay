using System.IO;
using System.IO.Compression;
using CrosshairOverlay.Core.Models;
using System.Windows.Threading;
using CrosshairOverlay.Services.Overlay;
using CrosshairOverlay.Services.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CrosshairOverlay.Tests;

/// <summary>
/// Kho preset là nơi dữ liệu người dùng có thể MẤT. Mỗi test chạy trên một thư mục tạm riêng.
/// </summary>
public class PresetRepositoryTests : IDisposable
{
    private readonly string _root;
    private readonly AppPathProvider _paths;
    private readonly PresetRepository _repository;

    public PresetRepositoryTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "CrosshairOverlayTests", Guid.NewGuid().ToString("N"));

        // AppPathProvider nhận rootOverride chính là để test trỏ sang thư mục tạm được.
        _paths = new AppPathProvider(_root);
        _paths.EnsureCreated();

        _repository = new PresetRepository(_paths, new CustomImageStore(_paths, NullLogger<CustomImageStore>.Instance), NullLogger<PresetRepository>.Instance);
    }

    [Fact]
    public async Task ZipBackup_RestoresSettingsPresetsAndImagesWithoutStalePresets()
    {
        var profile = CrosshairProfile.CreateDefault();
        await AtomicFile.WriteJsonAtomicAsync(_paths.SettingsFilePath, AppSettings.CreateDefault());
        await _repository.SaveAsync(profile);
        var images = Path.Combine(_root, "CustomImages");
        Directory.CreateDirectory(images);
        File.WriteAllText(Path.Combine(images, "scope.png"), "image");
        var archive = Path.Combine(_root, "backup.zip");

        DataBackupArchive.Create(_paths, archive);
        File.WriteAllText(_paths.SettingsFilePath, "broken");
        var stale = Path.Combine(_paths.PresetsDirectory, Guid.NewGuid() + ".json");
        File.WriteAllText(stale, "stale");
        File.Delete(Path.Combine(images, "scope.png"));

        DataBackupArchive.Restore(_paths, archive);

        Assert.NotNull(await AtomicFile.LoadJsonAsync<AppSettings>(_paths.SettingsFilePath));
        Assert.True(File.Exists(Path.Combine(_paths.PresetsDirectory, profile.Id + ".json")));
        Assert.False(File.Exists(stale));
        Assert.True(File.Exists(Path.Combine(images, "scope.png")));
    }

    [Fact]
    public void ZipBackup_RejectsPathTraversalBeforeWriting()
    {
        var archive = Path.Combine(_root, "malicious.zip");
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
        using (var writer = new StreamWriter(zip.CreateEntry("../settings.json").Open()))
            writer.Write("{}");

        Assert.Throws<InvalidDataException>(() => DataBackupArchive.Restore(_paths, archive));
        Assert.False(File.Exists(_paths.SettingsFilePath));
    }

    [Fact]
    public async Task ThuMucRong_TaoSanThuVienMacDinh()
    {
        var presets = await _repository.GetAllAsync();

        Assert.NotEmpty(presets);
        Assert.Contains(presets, p => p.Shape == CrosshairShape.Cross);

        // Và phải nằm lại trên đĩa, không chỉ trong bộ nhớ.
        Assert.Equal(presets.Count, Directory.GetFiles(_paths.PresetsDirectory, "*.json").Length);
    }

    [Fact]
    public async Task LuuRoiDocLai_GiuNguyenNoiDung()
    {
        var preset = CrosshairProfile.CreateDefault();
        preset.Name = "Của tôi";
        preset.InnerLines.Thickness = 7;

        await _repository.SaveAsync(preset);
        var doc = await _repository.GetAsync(preset.Id);

        Assert.NotNull(doc);
        Assert.Equal("Của tôi", doc!.Name);
        Assert.Equal(7, doc.InnerLines.Thickness);
    }

    [Fact]
    public async Task Luu_CapNhatThoiDiemSua()
    {
        var preset = CrosshairProfile.CreateDefault();
        var truoc = preset.ModifiedUtc;

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await Task.Delay(10, timeout.Token);
        await _repository.SaveAsync(preset);

        Assert.True(preset.ModifiedUtc > truoc);
    }

    [Fact]
    public async Task Xoa_BoHanKhoiDia()
    {
        var preset = CrosshairProfile.CreateDefault();
        await _repository.SaveAsync(preset);

        await _repository.DeleteAsync(preset.Id);

        Assert.Null(await _repository.GetAsync(preset.Id));
    }

    [Fact]
    public async Task XoaPresetKhongTonTai_KhongNemLoi()
    {
        await _repository.DeleteAsync(Guid.NewGuid());
    }

    [Fact]
    public async Task FileHongTrongThuVien_BiBoQua_KhongKeoSapCaKho()
    {
        var tot = CrosshairProfile.CreateDefault();
        tot.Name = "Lành lặn";
        await _repository.SaveAsync(tot);

        await File.WriteAllTextAsync(
            Path.Combine(_paths.PresetsDirectory, "hong.json"), "{{{ khong doc duoc");

        var presets = await _repository.GetAllAsync();

        Assert.Contains(presets, p => p.Name == "Lành lặn");
    }

    [Fact]
    public async Task GhiLaNguyenTu_KhongDeLaiFileTam()
    {
        await _repository.SaveAsync(CrosshairProfile.CreateDefault());

        Assert.Empty(Directory.GetFiles(_paths.PresetsDirectory, "*.tmp"));
    }

    [Fact]
    public async Task FileChinhBiHong_PhuHoiBanCuVaKhongGhiDeBackupTot()
    {
        var preset = CrosshairProfile.CreateDefault();
        preset.Name = "Previous";
        await _repository.SaveAsync(preset);
        preset.Name = "Current";
        await _repository.SaveAsync(preset);
        var path = Path.Combine(_paths.PresetsDirectory, $"{preset.Id:D}.json");

        await File.WriteAllTextAsync(path, string.Empty);
        Assert.Equal("Previous", (await _repository.GetAsync(preset.Id))?.Name);

        await _repository.SaveAsync(preset);
        Assert.Equal("Previous", (await AtomicFile.LoadJsonAsync<CrosshairProfile>(path + ".bak"))?.Name);
        Assert.Equal("Current", (await _repository.GetAsync(preset.Id))?.Name);

        await _repository.DeleteAsync(preset.Id);
        Assert.Null(await _repository.GetAsync(preset.Id));
        Assert.False(File.Exists(path + ".bak"));
    }

    [Fact]
    public async Task HaiBanSettingsHong_DungMacDinhAnToan()
    {
        await File.WriteAllTextAsync(_paths.SettingsFilePath, "{");
        await File.WriteAllTextAsync(_paths.SettingsFilePath + ".bak", string.Empty);
        using var settings = new AppSettingsService(_paths, NullLogger<AppSettingsService>.Instance);

        await settings.LoadAsync();

        Assert.True(settings.Current.OverlayEnabled);
        Assert.NotEmpty(settings.Current.Hotkeys);
    }

    [Fact]
    public async Task FlushSettings_ChoPhepLuuTiepSauKhiChuyenThuMuc()
    {
        using var settings = new AppSettingsService(_paths, NullLogger<AppSettingsService>.Instance);
        settings.Current.Language = "en";
        settings.RequestSave();
        await settings.FlushAsync();

        settings.Current.Language = "vi";
        settings.RequestSave();
        await settings.FlushAsync();

        Assert.Equal("vi", (await AtomicFile.LoadJsonAsync<AppSettings>(_paths.SettingsFilePath))?.Language);
    }

    [Fact]
    public void DongHoOled_DungKhiOverlayAn()
    {
        using var timer = new OledPixelShiftTimer(Dispatcher.CurrentDispatcher, (_, _) => { },
            TimeSpan.FromMinutes(2));
        timer.Configure(enabled: true, visible: true);
        Assert.True(timer.IsRunning);

        timer.Configure(enabled: true, visible: false);
        Assert.False(timer.IsRunning);
    }

    [Fact]
    public async Task DanhSachSapXepTheoTen()
    {
        foreach (var name in new[] { "Zulu", "Alpha", "Mike" })
        {
            var p = CrosshairProfile.CreateDefault();
            p.Name = name;
            await _repository.SaveAsync(p);
        }

        var presets = await _repository.GetAllAsync();
        var names = presets.Select(p => p.Name).ToList();

        Assert.Equal(names.OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase), names);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
