using System.IO;
using CrosshairOverlay.Core.Models;
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

        await Task.Delay(10);
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
    public async Task ExportRoiImport_CapIdMoi()
    {
        var goc = CrosshairProfile.CreateDefault();
        goc.Name = "Chia sẻ";
        await _repository.SaveAsync(goc);

        var file = Path.Combine(_root, "export.json");
        await _repository.ExportAsync(goc, file);

        var nhap = await _repository.ImportAsync(file);

        // Id mới là bắt buộc: trùng Id sẽ ghi đè lên preset đang có.
        Assert.NotEqual(goc.Id, nhap.Id);
        Assert.Equal(goc.Name, nhap.Name);
        Assert.Equal(goc.InnerLines.Thickness, nhap.InnerLines.Thickness);

        Assert.NotNull(await _repository.GetAsync(goc.Id));
        Assert.NotNull(await _repository.GetAsync(nhap.Id));
    }

    [Fact]
    public async Task ImportFileHong_NemInvalidData()
    {
        var file = Path.Combine(_root, "hong.json");
        await File.WriteAllTextAsync(file, "{ day khong phai json");

        await Assert.ThrowsAnyAsync<Exception>(() => _repository.ImportAsync(file));
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
