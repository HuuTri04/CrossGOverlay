using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using CrosshairOverlay.Core.Abstractions;
using CrosshairOverlay.Core.Models;
using CrosshairOverlay.Services.Presets;
using CrosshairOverlay.Services.Rendering;
using CrosshairOverlay.Services.Storage;
using CrosshairOverlay.ViewModels;
using CrosshairOverlay.Views;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CrosshairOverlay.Tests;

/// <summary>Thư viện 500+ mẫu: file dữ liệu, dịch vụ đọc lười + lọc, chuyển mẫu thành preset, ViewModel cửa sổ.</summary>
public sealed class PresetCatalogTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "cgo-catalog-" + Guid.NewGuid().ToString("N"));
    private readonly CrosshairRenderer _renderer = new(NullLogger<CrosshairRenderer>.Instance);

    public PresetCatalogTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    // ================================================================== file dữ liệu đi kèm

    private static string BundledPath()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "src", "CrosshairOverlay", "Data", "builtin_presets.json");
            if (File.Exists(candidate)) return candidate;
        }

        throw new FileNotFoundException("Không tìm thấy Data/builtin_presets.json từ " + AppContext.BaseDirectory);
    }

    private static async Task<(PresetCatalogService Service, IReadOnlyList<CatalogPreset> Presets)> LoadBundled()
    {
        var service = new PresetCatalogService(BundledPath(), NullLogger<PresetCatalogService>.Instance);
        return (service, await service.LoadAsync());
    }

    [Fact]
    public async Task FileDiKem_Tren500Mau_HopLe_IdKhongTrung_DuDanhMuc()
    {
        var raw = JsonSerializer.Deserialize<List<CatalogPreset>>(await File.ReadAllTextAsync(BundledPath()))!;
        var (service, presets) = await LoadBundled();

        Assert.True(presets.Count >= 500, $"chỉ có {presets.Count} mẫu");
        Assert.Equal(raw.Count, presets.Count);                       // không mục nào bị loại vì hỏng
        Assert.Equal(presets.Count, presets.Select(p => p.Id).Distinct().Count());
        Assert.Equal(presets.Count, presets.Select(p => p.Name.ToLowerInvariant()).Distinct().Count());

        foreach (var category in new[] { CatalogCategories.Dots, CatalogCategories.Crosses, CatalogCategories.Circles, CatalogCategories.Tactical })
            Assert.True(service.CountIn(category) >= 50, $"{category}: {service.CountIn(category)}");
    }

    [Fact]
    public async Task FileDiKem_MoiMauDungThanhPreset_VaVeRaHinh()
    {
        var (_, presets) = await LoadBundled();
        var options = new CrosshairRenderOptions(1d, true, 200d);

        foreach (var preset in presets)
        {
            var profile = CatalogPresetMapper.ToProfile(preset);
            var drawing = _renderer.Build(profile, options);

            Assert.False(drawing.Bounds.IsEmpty, $"'{preset.Name}' vẽ ra hình rỗng");
            Assert.Equal(preset.Name, profile.Name);
        }
    }

    /// <summary>
    /// Mẫu có mã Valorant: trường rút gọn do script Python tính phải khớp với bộ giải mã C# — hai bộ giải mã
    /// (Python sinh file, C# dùng lúc chạy) không được lệch nhau.
    /// </summary>
    [Fact]
    public async Task MauCoMaValorant_TruongRutGon_KhopBoGiaiMaCSharp()
    {
        var (_, presets) = await LoadBundled();
        var withCode = presets.Where(p => !string.IsNullOrEmpty(p.Code)).ToList();
        Assert.NotEmpty(withCode);

        foreach (var preset in withCode)
        {
            var fromCode = CatalogPresetMapper.ToProfile(preset);
            var fromFields = CatalogPresetMapper.ToProfile(preset with { Code = null });

            Assert.Equal(fromFields.Color, fromCode.Color);
            Assert.Equal(fromFields.CenterDot.Enabled, fromCode.CenterDot.Enabled);
            Assert.Equal(fromFields.Outline.Enabled, fromCode.Outline.Enabled);
            if (fromCode.CenterDot.Enabled) Assert.Equal(fromFields.CenterDot.Size, fromCode.CenterDot.Size);
            if (fromCode.Outline.Enabled)
            {
                Assert.Equal(fromFields.Outline.Thickness, fromCode.Outline.Thickness);
                Assert.Equal(fromFields.Outline.Opacity, fromCode.Outline.Opacity, 3);
            }

            if (preset.ShapeType == "ClassicCross")
            {
                var lines = fromCode.InnerLines.Enabled ? fromCode.InnerLines : fromCode.OuterLines;
                Assert.True(lines.Enabled, preset.Name);
                Assert.Equal(preset.Thickness, lines.Thickness);
                Assert.Equal(preset.Size, lines.Length);
                Assert.Equal(preset.Gap, lines.Offset);
            }
            else
            {
                Assert.Equal("Dot", preset.ShapeType);
                Assert.False(fromCode.InnerLines.Enabled || fromCode.OuterLines.Enabled, preset.Name);
            }
        }
    }

    [Fact]
    public void Csproj_FileLaContent_CopyIfNewer()
    {
        var csproj = File.ReadAllText(Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(BundledPath()))!, "CrosshairOverlay.csproj"));

        Assert.Matches(@"<Content Include=""Data\\builtin_presets\.json"">\s*<CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>", csproj);
    }

    // ================================================================== đọc lười & cache

    private static string Json(params object[] entries) => JsonSerializer.Serialize(entries);

    private static object Entry(string name, string category = "Crosses", string shape = "ClassicCross",
        string color = "#00FFFF", Guid? id = null, bool dot = false) => new
    {
        Id = id ?? Guid.NewGuid(), Name = name, Category = category, ShapeType = shape, Color = color,
        Thickness = 2, Size = 6, Gap = 3, HasDot = dot, DotSize = 2, HasOutline = true, OutlineThickness = 1,
        OutlineOpacity = 1,
    };

    [Fact]
    public async Task KhongDocFileKhiKhoiTao_ChiDocKhiLoadAsync_RoiDungBanCache()
    {
        var path = Path.Combine(_root, "catalog.json");
        var service = new PresetCatalogService(path, NullLogger<PresetCatalogService>.Instance);

        // File chưa tồn tại lúc dựng dịch vụ: nếu constructor đọc thì sẽ ra rỗng.
        await File.WriteAllTextAsync(path, Json(Entry("A"), Entry("B")));
        Assert.False(service.IsLoaded);
        Assert.Empty(service.FilterPresets(null, null));

        var first = await service.LoadAsync();
        Assert.True(service.IsLoaded);
        Assert.Equal(2, first.Count);

        // Đổi file sau khi đã đọc: vẫn là bản trong RAM, không đọc lại.
        await File.WriteAllTextAsync(path, Json(Entry("C")));
        var second = await service.LoadAsync();
        Assert.Same(first, second);
        Assert.Equal(2, service.FilterPresets(null, null).Count);
    }

    [Fact]
    public async Task ThieuFile_TraVeRong_KhongNem()
    {
        var service = new PresetCatalogService(Path.Combine(_root, "khong-co.json"), NullLogger<PresetCatalogService>.Instance);

        Assert.Empty(await service.LoadAsync());
        Assert.True(service.IsLoaded);
    }

    [Fact]
    public async Task FileHong_TraVeRong_KhongNem()
    {
        var path = Path.Combine(_root, "hong.json");
        await File.WriteAllTextAsync(path, "[{ \"Name\": ");
        var service = new PresetCatalogService(path, NullLogger<PresetCatalogService>.Instance);

        Assert.Empty(await service.LoadAsync());
    }

    [Fact]
    public async Task MucHong_BiBoRieng_PhanConLaiVanDoc()
    {
        var dup = Guid.NewGuid();
        var path = Path.Combine(_root, "mix.json");
        await File.WriteAllTextAsync(path, Json(
            Entry("Tốt 1", id: dup),
            Entry("Trùng Id", id: dup),
            Entry("", category: "Dots"),
            Entry("Hình lạ", shape: "Hexagon"),
            Entry("Danh mục lạ", category: "Snipers"),
            Entry("Màu hỏng", color: "xanh"),
            Entry("Tốt 2", category: "Circles", shape: "Circle")));
        var service = new PresetCatalogService(path, NullLogger<PresetCatalogService>.Instance);

        var presets = await service.LoadAsync();

        Assert.Equal(["Tốt 1", "Tốt 2"], presets.Select(p => p.Name));
    }

    // ================================================================== lọc

    private async Task<PresetCatalogService> Sample()
    {
        var path = Path.Combine(_root, "sample.json");
        await File.WriteAllTextAsync(path, Json(
            Entry("Dot 2px · Cyan", "Dots", "Dot"),
            Entry("Dot 3px · Red", "Dots", "Dot", "#FF0000"),
            Entry("Cross L6 T2 G3 · Cyan", "Crosses"),
            Entry("Cross L4 T1 G2 + Dot · Green", "Crosses", color: "#00FF00", dot: true),
            Entry("Circle R8 T1 · Cyan", "Circles", "Circle"),
            Entry("T-Shape L5 T2 G3 · Yellow", "Tactical", "TShape", "#FFFF00"),
            Entry("Sample Player", "Pro Players")));
        var service = new PresetCatalogService(path, NullLogger<PresetCatalogService>.Instance);
        await service.LoadAsync();
        return service;
    }

    private static string[] Names(IEnumerable<CatalogPreset> presets) => [.. presets.Select(p => p.Name)];

    [Fact]
    public async Task DanhMuc_ThuTuCoDinh_TatCaDungDau_VaDemDung()
    {
        var service = await Sample();

        Assert.Equal(["All", "Pro Players", "Dots", "Crosses", "Circles", "Tactical", "Accessibility"], service.GetCategories());
        Assert.Equal(7, service.CountIn(CatalogCategories.All));
        Assert.Equal(2, service.CountIn(CatalogCategories.Dots));
        Assert.Equal(1, service.CountIn(CatalogCategories.ProPlayers));
    }

    [Theory]
    [InlineData(null, "All", 7)]
    [InlineData("", null, 7)]
    [InlineData(null, "Dots", 2)]
    [InlineData("cyan", "All", 3)]
    [InlineData("CYAN", "Dots", 1)]
    [InlineData("cross cyan", "All", 1)]          // mọi từ khoá đều phải khớp
    [InlineData("cham", "All", 3)]                // không dấu khớp "chấm" (2 chấm + 1 chữ thập có chấm giữa)
    [InlineData("chữ thập", "All", 3)]            // tên tiếng Anh, gõ tiếng Việt vẫn ra (kể cả mẫu tuyển thủ hình chữ thập)
    [InlineData("vong tron", null, 1)]
    [InlineData("chữ t", "Tactical", 1)]
    [InlineData("tuyen thu", null, 1)]
    [InlineData("  dot    red  ", null, 1)]
    [InlineData("không-có-gì", null, 0)]
    public async Task Loc_TheoTuKhoaVaDanhMuc(string? search, string? category, int expected)
    {
        var service = await Sample();

        Assert.Equal(expected, service.FilterPresets(search, category).Count);
    }

    [Theory]
    [InlineData("Chấm Nhỏ", "cham nho")]
    [InlineData("Đỏ  đậm", "do dam")]
    [InlineData("  ", "")]
    [InlineData(null, "")]
    public void ChuanHoa_BoDau_ChuThuong(string? input, string expected)
    {
        Assert.Equal(expected, PresetCatalogService.Normalize(input));
    }

    // ================================================================== mẫu → preset

    private static CatalogPreset Template(string shape, bool dot = false, string? code = null) => new()
    {
        Id = Guid.NewGuid(), Name = "Mẫu " + shape, Category = CatalogCategories.Crosses, ShapeType = shape,
        Color = "#FF8800", Thickness = 2, Size = 7, Gap = 4, HasDot = dot, DotSize = 3, HasOutline = true,
        OutlineThickness = 1, OutlineOpacity = 0.5, Code = code,
    };

    [Theory]
    [InlineData("ClassicCross", CrosshairShape.Cross, true, false)]
    [InlineData("TShape", CrosshairShape.TShape, true, false)]
    [InlineData("XShape", CrosshairShape.XShape, true, false)]
    [InlineData("Dot", CrosshairShape.Dot, false, false)]
    [InlineData("Circle", CrosshairShape.Circle, false, true)]
    [InlineData("Square", CrosshairShape.Square, false, true)]
    public void MauThanhPreset_TungHinh(string shapeType, CrosshairShape shape, bool lines, bool ring)
    {
        var profile = CatalogPresetMapper.ToProfile(Template(shapeType));

        Assert.Equal(shape, profile.Shape);
        Assert.Equal(CrosshairType.Standard, profile.Type);
        Assert.Equal(Color.FromRgb(0xFF, 0x88, 0x00), profile.Color);
        Assert.Equal(lines, profile.InnerLines.Enabled);
        Assert.False(profile.OuterLines.Enabled);
        Assert.Equal(ring, profile.Ring.Enabled);
        Assert.True(profile.Outline.Enabled);
        Assert.Equal(0.5, profile.Outline.Opacity, 3);

        if (lines)
        {
            Assert.Equal(2d, profile.InnerLines.Thickness);
            Assert.Equal(7d, profile.InnerLines.Length);
            Assert.Equal(4d, profile.InnerLines.Offset);
        }

        if (ring) Assert.Equal(7d, profile.Ring.Radius);
        if (shapeType == "Dot") Assert.Equal(3d, profile.CenterDot.Size);
    }

    [Fact]
    public void MoiLanDungMau_LaPresetMoi_IdKhacNhau()
    {
        var template = Template("ClassicCross", dot: true);

        var a = CatalogPresetMapper.ToProfile(template);
        var b = CatalogPresetMapper.ToProfile(template);

        Assert.NotSame(a, b);
        Assert.NotEqual(a.Id, b.Id);
        Assert.NotEqual(template.Id, a.Id);
        a.Color = Colors.Red;
        Assert.NotEqual(Colors.Red, b.Color);
    }

    [Fact]
    public void MauCoMa_DungBoNhapMaValorant_GiuNhanhNgoai()
    {
        // Mã có nhánh ngoài: trường rút gọn không mô tả được, nhưng preset dựng từ mã thì có.
        var profile = CatalogPresetMapper.ToProfile(Template("ClassicCross", code: "0;P;c;5;0t;1;0l;3;0o;2;1t;1;1l;2;1o;8;1a;0.5"));

        Assert.True(profile.OuterLines.Enabled);
        Assert.Equal(8d, profile.OuterLines.Offset);
        Assert.Equal(Colors.Cyan, profile.Color);   // màu lấy từ mã, không từ trường Color
    }

    // ================================================================== ảnh thu nhỏ & lưới

    [Theory]
    [InlineData(20, 20, 3)]        // hình nhỏ: phóng nguyên lần, tối đa 3
    [InlineData(40, 40, 2)]
    [InlineData(70, 70, 1)]
    [InlineData(200, 100, 0.56)]   // hình to hơn khung: thu nhỏ vừa 80% khung
    public void AnhThuNho_HeSoPhong(double width, double height, double expected)
    {
        Assert.Equal(expected, Controls.CrosshairThumbnail.Zoom(width, height, new Size(140, 100)), 2);
    }

    [Theory]
    [InlineData(700, 4)]
    [InlineData(520, 3)]
    [InlineData(100, 1)]
    [InlineData(1200, 6)]
    public void SoCotTheoBeNgang(double width, int expected)
    {
        Assert.Equal(expected, PresetLibraryView.ColumnsFor(width));
    }

    // ================================================================== ViewModel cửa sổ

    private sealed class MemoryRepository : IPresetRepository
    {
        public List<CrosshairProfile> Saved { get; } = [];

        public Task<IReadOnlyList<CrosshairProfile>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CrosshairProfile>>([CrosshairProfile.CreateDefault()]);

        public Task<CrosshairProfile?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult<CrosshairProfile?>(null);

        public Task SaveAsync(CrosshairProfile profile, CancellationToken cancellationToken = default)
        {
            Saved.Add(profile);
            return Task.CompletedTask;
        }

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public event EventHandler? PresetsChanged { add { } remove { } }

        public bool LastLoadSkippedFiles => false;
    }

    private async Task<(PresetLibraryViewModel Vm, PresetLibrary Library, MemoryRepository Repo)> LibraryVm()
    {
        var repo = new MemoryRepository();
        var library = new PresetLibrary(repo, NullLogger<PresetLibrary>.Instance);
        await library.InitializeAsync(Guid.Empty);

        var vm = new PresetLibraryViewModel(await Sample(), library, _renderer, NullLogger<PresetLibraryViewModel>.Instance);
        await vm.LoadAsync();
        return (vm, library, repo);
    }

    [Fact]
    public async Task ViewModel_ChiaHang_TheoSoCot()
    {
        var (vm, _, _) = await LibraryVm();

        vm.ColumnCount = 3;
        Assert.Equal([3, 3, 1], vm.Rows.Select(r => r.Items.Count));

        vm.ColumnCount = 5;
        Assert.Equal([5, 2], vm.Rows.Select(r => r.Items.Count));
        Assert.False(vm.IsLoading);
        Assert.False(vm.IsEmpty);
    }

    [Fact]
    public async Task ViewModel_HangCoOCoDinh_ToiDa8Cot_OThuaLaNull()
    {
        var (vm, _, _) = await LibraryVm();

        vm.ColumnCount = 50;
        Assert.Equal(PresetLibraryViewModel.MaxColumns, vm.ColumnCount);
        Assert.Equal(8, CatalogRow.SlotCount);

        vm.ColumnCount = 3;
        var row = vm.Rows[^1];                    // hàng cuối chỉ có 1 thẻ
        Assert.Same(row.Items[0], row.Slot0);
        Assert.Null(row.Slot1);
        Assert.Null(row.Slot7);
        Assert.Same(vm.Rows[0].Items[2], vm.Rows[0].Slot2);
    }

    [Fact]
    public async Task ViewModel_ChipDanhMuc_CoSoLuong_ChonThiLoc()
    {
        var (vm, _, _) = await LibraryVm();
        vm.ColumnCount = 10;

        Assert.True(vm.Categories[0].IsSelected);
        Assert.Equal(7, vm.Categories[0].Count);

        var dots = vm.Categories.Single(c => c.Key == CatalogCategories.Dots);
        dots.IsSelected = true;

        Assert.Equal(CatalogCategories.Dots, vm.SelectedCategory);
        Assert.False(vm.Categories[0].IsSelected);
        Assert.Equal(2, vm.Rows.Sum(r => r.Items.Count));

        vm.SearchText = "khong co";
        Assert.True(vm.IsEmpty);
        Assert.Empty(vm.Rows);
    }

    [Fact]
    public async Task FavoriteCatalogChipFiltersTemplatesAndPersistsIds()
    {
        var settings = new TestCrosshairTests.FakeSettings();
        using var library = new PresetLibrary(new MemoryRepository(), NullLogger<PresetLibrary>.Instance);
        await library.InitializeAsync(Guid.Empty);
        using var vm = new PresetLibraryViewModel(await Sample(), library, _renderer,
            NullLogger<PresetLibraryViewModel>.Instance, settings);
        await vm.LoadAsync();
        var rowSource = vm.Rows;

        var selected = vm.Rows.SelectMany(row => row.Items).First();
        vm.ToggleFavoriteCommand.Execute(selected);
        Assert.True(selected.IsFavorite);
        Assert.Contains(selected.Preset.Id, settings.Current.FavoriteCatalogPresetIds);

        var favoriteChip = vm.Categories.Single(chip => chip.Key == CatalogCategories.Favorites);
        Assert.Equal(1, favoriteChip.Count);
        favoriteChip.IsSelected = true;
        Assert.Same(selected, Assert.Single(vm.Rows.SelectMany(row => row.Items)));

        vm.ToggleFavoriteCommand.Execute(selected);
        Assert.Empty(vm.Rows);
        Assert.Empty(settings.Current.FavoriteCatalogPresetIds);
        Assert.Same(rowSource, vm.Rows); // Keep the virtualizing ItemsSource alive.
    }

    [Fact]
    public async Task ViewModel_TheGiuNguyenQuaCacLanLoc_KhongChupLaiAnh()
    {
        var (vm, _, _) = await LibraryVm();
        vm.ColumnCount = 10;
        var before = vm.Rows.SelectMany(r => r.Items).First(i => i.Name.StartsWith("Circle", StringComparison.Ordinal));

        vm.SearchText = "circle";
        var after = Assert.Single(vm.Rows.SelectMany(r => r.Items));

        Assert.Same(before, after);
        Assert.Same(before.PreviewProfile, after.PreviewProfile);
    }

    [Fact]
    public async Task DungMau_ThemPresetMoi_DatLamDangDung_LuuFile_VaBaoToast()
    {
        var (vm, library, repo) = await LibraryVm();
        vm.ColumnCount = 10;
        var item = vm.Rows.SelectMany(r => r.Items).First(i => i.Name == "Circle R8 T1 · Cyan");
        var countBefore = library.Presets.Count;

        await vm.UseTemplateCommand.ExecuteAsync(item);

        Assert.Equal(countBefore + 1, library.Presets.Count);
        var added = library.Presets[^1];
        Assert.Same(added, library.Active);
        Assert.Contains(added, repo.Saved);
        Assert.Equal("Circle R8 T1 · Cyan", added.Name);
        Assert.Equal(CrosshairShape.Circle, added.Shape);
        Assert.NotSame(item.PreviewProfile, added);   // preset xem trước không bị đem đi dùng
        Assert.True(vm.Toast.IsVisible);
        Assert.Contains(added.Name, vm.Toast.Text);

        // Dùng lại cùng mẫu: preset thứ hai độc lập, tên không trùng.
        await vm.UseTemplateCommand.ExecuteAsync(item);
        Assert.Equal(countBefore + 2, library.Presets.Count);
        Assert.NotEqual(added.Name, library.Presets[^1].Name);
        Assert.NotEqual(added.Id, library.Presets[^1].Id);
    }

    /// <summary>Đếm số lần ai đó thật sự yêu cầu đọc thư viện.</summary>
    private sealed class CountingCatalog(IPresetCatalogService inner) : IPresetCatalogService
    {
        public int Loads { get; private set; }

        public bool IsLoaded => inner.IsLoaded;

        public Task<IReadOnlyList<CatalogPreset>> LoadAsync(CancellationToken cancellationToken = default)
        {
            Loads++;
            return inner.LoadAsync(cancellationToken);
        }

        public IReadOnlyList<string> GetCategories() => inner.GetCategories();

        public int CountIn(string category) => inner.CountIn(category);

        public IReadOnlyList<CatalogPreset> FilterPresets(string? searchText, string? category) =>
            inner.FilterPresets(searchText, category);
    }

    private sealed class FailingCatalog : IPresetCatalogService
    {
        public bool IsLoaded => false;
        public Task<IReadOnlyList<CatalogPreset>> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromException<IReadOnlyList<CatalogPreset>>(new IOException("Catalog unavailable"));
        public IReadOnlyList<string> GetCategories() => CatalogCategories.Ordered;
        public int CountIn(string category) => 0;
        public IReadOnlyList<CatalogPreset> FilterPresets(string? searchText, string? category) => [];
    }

    [Fact]
    public async Task FailedCatalogLoadClearsLoadingState()
    {
        using var library = new PresetLibrary(new MemoryRepository(), NullLogger<PresetLibrary>.Instance);
        using var vm = new PresetLibraryViewModel(new FailingCatalog(), library, _renderer,
            NullLogger<PresetLibraryViewModel>.Instance);

        await Assert.ThrowsAsync<IOException>(() => vm.LoadAsync());
        Assert.False(vm.IsLoading);
    }

    [Fact]
    public async Task MoCuaSoSettings_ChuaDocThuVien_ChiDocKhiTabDuocMo()
    {
        var repo = new MemoryRepository();
        using var library = new PresetLibrary(repo, NullLogger<PresetLibrary>.Instance);
        await library.InitializeAsync(Guid.Empty);
        var catalog = new CountingCatalog(await Sample());

        // Dựng ViewModel của cửa sổ Settings = mở cửa sổ: KHÔNG được chạm tới thư viện mẫu.
        using var tab = new PresetLibraryViewModel(catalog, library, _renderer, NullLogger<PresetLibraryViewModel>.Instance);
        Assert.Equal(0, catalog.Loads);
        Assert.True(tab.IsLoading);
        Assert.Empty(tab.Rows);

        // Bấm sang tab "Thư viện" lần đầu.
        await tab.LoadAsync();
        Assert.Equal(1, catalog.Loads);
        Assert.False(tab.IsLoading);
        Assert.NotEmpty(tab.Rows);

        // Đổi tab qua lại: dữ liệu đã nằm trong RAM, dịch vụ chỉ trả bản cache.
        await tab.LoadAsync();
        Assert.Equal(2, catalog.Loads);
        Assert.NotEmpty(tab.Rows);
    }

    [Fact]
    public async Task ThuVienTrong_HienThongBaoThieuFile()
    {
        var repo = new MemoryRepository();
        var library = new PresetLibrary(repo, NullLogger<PresetLibrary>.Instance);
        var catalog = new PresetCatalogService(Path.Combine(_root, "khong-co.json"), NullLogger<PresetCatalogService>.Instance);
        using var vm = new PresetLibraryViewModel(catalog, library, _renderer, NullLogger<PresetLibraryViewModel>.Instance);

        await vm.LoadAsync();

        Assert.True(vm.IsEmpty);
        Assert.Contains("builtin_presets.json", vm.EmptyText);
    }
}
