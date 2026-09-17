using System.IO;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CrosshairOverlay.Core.Abstractions;
using CrosshairOverlay.Core.Models;
using CrosshairOverlay.Localization;
using CrosshairOverlay.Services.Presets;
using Microsoft.Extensions.Logging;

namespace CrosshairOverlay.ViewModels;

/// <summary>Cửa sổ "Thư viện mẫu": tìm, lọc theo danh mục và thêm mẫu vào preset của người dùng.</summary>
/// <remarks>
/// <para>
/// Lưới thẻ được chia thành các HÀNG (<see cref="CatalogRow"/>), mỗi hàng <see cref="ColumnCount"/> thẻ, rồi đặt
/// trong danh sách dọc ảo hoá. WPF không có WrapPanel ảo hoá; chia hàng như vậy thì dùng được
/// VirtualizingStackPanel dựng sẵn — dù 500 hay 5000 mẫu, chỉ vài hàng đang nhìn thấy mới có thẻ thật.
/// </para>
/// <para>
/// Đối tượng thẻ (<see cref="CatalogItemViewModel"/>) được tạo một lần cho mỗi mẫu và dùng lại qua mọi lần lọc,
/// nên ảnh thu nhỏ đã chụp không phải chụp lại khi gõ tìm kiếm.
/// </para>
/// </remarks>
public sealed partial class PresetLibraryViewModel : ObservableObject, IDisposable
{
    /// <summary>
    /// Số thẻ tối đa mỗi hàng — bằng số ô cố định trong mẫu hàng của cửa sổ (8 thẻ 160 DIP ≈ cửa sổ phóng to trên màn
    /// hình 1536 DIP).
    /// </summary>
    public const int MaxColumns = CatalogRow.SlotCount;

    /// <summary>Thời gian thông báo "Đã thêm…" hiện trên màn hình.</summary>
    internal static readonly TimeSpan ToastDuration = TimeSpan.FromMilliseconds(2500);

    private readonly IPresetCatalogService _catalog;
    private readonly IPresetLibrary _library;
    private readonly ILogger<PresetLibraryViewModel> _logger;

    private readonly Dictionary<Guid, CatalogItemViewModel> _items = [];
    private IReadOnlyList<CatalogItemViewModel> _filtered = [];
    private DispatcherTimer? _toastTimer;
    private string _selectedCategory = CatalogCategories.All;
    private int _columnCount = 4;
    private bool _disposed;

    public PresetLibraryViewModel(
        IPresetCatalogService catalog,
        IPresetLibrary library,
        ICrosshairRenderer renderer,
        ILogger<PresetLibraryViewModel> logger)
    {
        _catalog = catalog;
        _library = library;
        _logger = logger;
        Renderer = renderer;

        Categories = [.. catalog.GetCategories().Select(key => new CategoryChipViewModel(key, CategoryLabel(key), this))];
        Categories[0].IsSelected = true;
    }

    /// <summary>Renderer dùng chung với overlay, cho ảnh thu nhỏ của từng thẻ.</summary>
    public ICrosshairRenderer Renderer { get; }

    public IReadOnlyList<CategoryChipViewModel> Categories { get; }

    [ObservableProperty] private IReadOnlyList<CatalogRow> _rows = [];

    [ObservableProperty] private string _searchText = string.Empty;

    [ObservableProperty] private bool _isLoading = true;

    /// <summary>Không có mẫu nào để hiện (chưa có file, hoặc bộ lọc không khớp gì).</summary>
    [ObservableProperty] private bool _isEmpty;

    [ObservableProperty] private string _emptyText = string.Empty;

    [ObservableProperty] private string _resultText = string.Empty;

    [ObservableProperty] private string? _toastText;

    [ObservableProperty] private bool _isToastVisible;

    public string SelectedCategory => _selectedCategory;

    /// <summary>Số thẻ mỗi hàng, do view tính từ bề ngang vùng danh sách.</summary>
    public int ColumnCount
    {
        get => _columnCount;
        set
        {
            value = Math.Clamp(value, 1, MaxColumns);
            if (_columnCount == value) return;

            _columnCount = value;
            OnPropertyChanged();
            RebuildRows();
        }
    }

    /// <summary>Đọc thư viện (lần đầu: đọc file trên luồng nền; các lần sau: lấy bản đã cache).</summary>
    public async Task LoadAsync()
    {
        IsLoading = true;
        var presets = await _catalog.LoadAsync().ConfigureAwait(true);
        if (_disposed) return;

        foreach (var preset in presets)
            _items[preset.Id] = new CatalogItemViewModel(preset, Renderer);

        foreach (var chip in Categories)
            chip.Count = _catalog.CountIn(chip.Key);

        IsLoading = false;
        ApplyFilter();
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    internal void SelectCategory(string key)
    {
        if (_selectedCategory == key) return;

        _selectedCategory = key;
        foreach (var chip in Categories) chip.SetSelectedSilently(chip.Key == key);

        OnPropertyChanged(nameof(SelectedCategory));
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        if (IsLoading) return;

        _filtered = [.. _catalog.FilterPresets(SearchText, _selectedCategory)
            .Select(p => _items.TryGetValue(p.Id, out var item) ? item : null)
            .OfType<CatalogItemViewModel>()];

        ResultText = Tr.Format("Library_Count", _filtered.Count);
        IsEmpty = _filtered.Count == 0;
        EmptyText = _items.Count == 0 ? Tr.Get("Library_Missing") : Tr.Get("Library_Empty");
        RebuildRows();
    }

    private void RebuildRows()
    {
        var rows = new List<CatalogRow>((_filtered.Count + _columnCount - 1) / _columnCount);
        for (var start = 0; start < _filtered.Count; start += _columnCount)
        {
            var count = Math.Min(_columnCount, _filtered.Count - start);
            var cells = new CatalogItemViewModel[count];
            for (var i = 0; i < count; i++) cells[i] = _filtered[start + i];
            rows.Add(new CatalogRow(cells));
        }

        Rows = rows;
    }

    /// <summary>
    /// Dựng một preset MỚI từ mẫu và thêm vào thư viện của người dùng. Thư viện tự lưu file preset, đặt nó làm
    /// preset đang dùng (danh sách bên ngoài chọn theo), và ghi Id đang dùng cùng thứ tự vào settings.json.
    /// </summary>
    [RelayCommand]
    private async Task UseTemplateAsync(CatalogItemViewModel? item)
    {
        if (item is null) return;

        try
        {
            var profile = CatalogPresetMapper.ToProfile(item.Preset);
            var added = await _library.AddAsync(profile).ConfigureAwait(true);

            _logger.LogInformation("Thêm mẫu '{Template}' từ thư viện thành preset '{Name}'.", item.Name, added.Name);
            ShowToast(Tr.Format("Library_Added", added.Name));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _logger.LogError(ex, "Không thêm được mẫu '{Template}'.", item.Name);
            ShowToast(Tr.Format("Library_AddFailed", ex.Message));
        }
    }

    private void ShowToast(string text)
    {
        ToastText = text;
        IsToastVisible = true;

        // Một timer duy nhất, chỉ chạy trong 2,5 giây sau mỗi lần thêm rồi tự dừng: không có nhịp nền nào.
        _toastTimer ??= CreateToastTimer();
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    private DispatcherTimer CreateToastTimer()
    {
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = ToastDuration };
        timer.Tick += OnToastTimerTick;
        return timer;
    }

    private void OnToastTimerTick(object? sender, EventArgs e)
    {
        _toastTimer?.Stop();
        IsToastVisible = false;
    }

    internal static string CategoryLabel(string key) => key switch
    {
        CatalogCategories.All => Tr.Get("Library_CatAll"),
        CatalogCategories.ProPlayers => Tr.Get("Library_CatPro"),
        CatalogCategories.Dots => Tr.Get("Library_CatDots"),
        CatalogCategories.Crosses => Tr.Get("Library_CatCrosses"),
        CatalogCategories.Circles => Tr.Get("Library_CatCircles"),
        _ => Tr.Get("Library_CatOther"),
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_toastTimer is not null)
        {
            _toastTimer.Stop();
            _toastTimer.Tick -= OnToastTimerTick;
            _toastTimer = null;
        }
    }
}

/// <summary>Một hàng của lưới thẻ.</summary>
/// <remarks>
/// Mẫu hàng trong XAML có <see cref="SlotCount"/> ô <c>ContentPresenter</c> CỐ ĐỊNH gắn vào <see cref="Slot0"/>…
/// <see cref="Slot7"/>, không phải một ItemsControl lồng bên trong. Lý do đo được: khi danh sách ảo hoá tái dùng một
/// hàng cho dữ liệu khác, ItemsControl lồng bên trong nhận danh sách mới và dựng lại TOÀN BỘ cây giao diện của từng
/// thẻ, nên cuộn liên tục có khung hình trễ tới 50 ms. ContentPresenter giữ nguyên cây giao diện khi chỉ đổi Content
/// (cùng mẫu thẻ), nên tái dùng hàng chỉ còn là gán lại binding. Ô trống (null) không vẽ gì.
/// </remarks>
public sealed record CatalogRow(IReadOnlyList<CatalogItemViewModel> Items)
{
    public const int SlotCount = 8;

    public CatalogItemViewModel? Slot0 => Slot(0);
    public CatalogItemViewModel? Slot1 => Slot(1);
    public CatalogItemViewModel? Slot2 => Slot(2);
    public CatalogItemViewModel? Slot3 => Slot(3);
    public CatalogItemViewModel? Slot4 => Slot(4);
    public CatalogItemViewModel? Slot5 => Slot(5);
    public CatalogItemViewModel? Slot6 => Slot(6);
    public CatalogItemViewModel? Slot7 => Slot(7);

    private CatalogItemViewModel? Slot(int index) => index < Items.Count ? Items[index] : null;
}

/// <summary>Một thẻ mẫu. Preset xem trước chỉ được dựng khi thẻ thật sự hiện lên (ảo hoá).</summary>
public sealed class CatalogItemViewModel(CatalogPreset preset, ICrosshairRenderer renderer)
{
    private CrosshairProfile? _previewProfile;

    public CatalogPreset Preset { get; } = preset;

    public string Name => Preset.Name;

    public ICrosshairRenderer Renderer { get; } = renderer;

    /// <summary>Preset CHỈ để vẽ ảnh thu nhỏ — "Dùng mẫu" luôn dựng một preset mới, không dùng lại cái này.</summary>
    public CrosshairProfile PreviewProfile => _previewProfile ??= CatalogPresetMapper.ToProfile(Preset);

    public override string ToString() => Name;
}

/// <summary>Chip lọc danh mục (RadioButton).</summary>
public sealed partial class CategoryChipViewModel(string key, string label, PresetLibraryViewModel owner) : ObservableObject
{
    private bool _isSelected;

    public string Key { get; } = key;

    public string Label { get; } = label;

    [ObservableProperty] private int _count;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (!SetProperty(ref _isSelected, value) || !value) return;
            owner.SelectCategory(Key);
        }
    }

    internal void SetSelectedSilently(bool value) => SetProperty(ref _isSelected, value, nameof(IsSelected));

    public override string ToString() => Label;
}
