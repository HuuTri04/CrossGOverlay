using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CrosshairOverlay.Core.Abstractions;
using CrosshairOverlay.Core.Models;
using CrosshairOverlay.Localization;
using CrosshairOverlay.Services.Presets;
using CrosshairOverlay.Services.Storage;
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

    private readonly IPresetCatalogService _catalog;
    private readonly IPresetLibrary _library;
    private readonly IAppSettingsService? _settings;
    private readonly HashSet<Guid> _favoriteCatalogIds;
    private readonly ILogger<PresetLibraryViewModel> _logger;
    private readonly ITrayIconController? _tray;

    private readonly Dictionary<Guid, CatalogItemViewModel> _items = [];
    private readonly ObservableCollection<CatalogRow> _rows = [];
    private ICollectionView? _itemsView;
    private HashSet<Guid> _matchingIds = [];
    private bool _refreshingView;
    private int _favoriteCount;
    private string _selectedCategory = CatalogCategories.All;
    private LibraryColorFilter _selectedColorFilter;
    private int _columnCount = 4;
    private bool _disposed;

    public PresetLibraryViewModel(
        IPresetCatalogService catalog,
        IPresetLibrary library,
        ICrosshairRenderer renderer,
        ILogger<PresetLibraryViewModel> logger,
        ITrayIconController? tray = null)
        : this(catalog, library, renderer, logger, settings: null, tray) { }

    public PresetLibraryViewModel(
        IPresetCatalogService catalog,
        IPresetLibrary library,
        ICrosshairRenderer renderer,
        ILogger<PresetLibraryViewModel> logger,
        IAppSettingsService? settings,
        ITrayIconController? tray = null)
    {
        _catalog = catalog;
        _library = library;
        _settings = settings;
        _favoriteCatalogIds = settings?.Current.FavoriteCatalogPresetIds.ToHashSet() ?? [];
        _logger = logger;
        _tray = tray;
        Renderer = renderer;
        ColorFilters =
        [
            new(LibraryColorFilter.All, "Library_ColorAll"),
            new(LibraryColorFilter.Green, "Library_ColorGreen"),
            new(LibraryColorFilter.Red, "Library_ColorRed"),
            new(LibraryColorFilter.Blue, "Library_ColorBlue"),
            new(LibraryColorFilter.Yellow, "Library_ColorYellow"),
            new(LibraryColorFilter.White, "Library_ColorWhite"),
        ];

        // Dịch vụ luôn trả danh sách cố định, nhưng ViewModel không được sập nếu nó trả rỗng.
        var keys = catalog.GetCategories() is { Count: > 0 } fromService ? fromService : CatalogCategories.Ordered;
        Categories = [.. keys.Take(1).Concat([CatalogCategories.Favorites]).Concat(keys.Skip(1))
            .Select(key => new CategoryChipViewModel(key, CategoryLabel(key), this))];

        // Chip đầu tiên ("Tất cả") ứng với bộ lọc mặc định; đánh dấu thẳng, không đi vòng qua SelectCategory.
        Categories[0].SetSelectedSilently(true);
        PropertyChangedEventManager.AddHandler(TranslationSource.Instance, OnLanguageChanged, string.Empty);
    }

    /// <summary>Renderer dùng chung với overlay, cho ảnh thu nhỏ của từng thẻ.</summary>
    public ICrosshairRenderer Renderer { get; }

    public IReadOnlyList<CategoryChipViewModel> Categories { get; }
    public IReadOnlyList<LocalizedOption<LibraryColorFilter>> ColorFilters { get; }

    public LibraryColorFilter SelectedColorFilter
    {
        get => _selectedColorFilter;
        set
        {
            if (!SetProperty(ref _selectedColorFilter, value)) return;
            ApplyFilter();
        }
    }

    /// <summary>Thông báo "Đã thêm …" ở góc dưới; người dùng ở lại tab để chọn tiếp mẫu khác.</summary>
    public ToastViewModel Toast { get; } = new();

    /// <summary>Stable row source: the recycling panel never receives a new ItemsSource on filter changes.</summary>
    public ObservableCollection<CatalogRow> Rows => _rows;

    [ObservableProperty] private string _searchText = string.Empty;

    [ObservableProperty] private bool _isLoading = true;

    /// <summary>Không có mẫu nào để hiện (chưa có file, hoặc bộ lọc không khớp gì).</summary>
    [ObservableProperty] private bool _isEmpty;

    [ObservableProperty] private string _emptyText = string.Empty;

    [ObservableProperty] private string _resultText = string.Empty;

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
        IReadOnlyList<CatalogPreset> presets;
        try { presets = await _catalog.LoadAsync().ConfigureAwait(true); }
        catch
        {
            IsLoading = false;
            throw;
        }
        if (_disposed) return;

        if (_itemsView is INotifyCollectionChanged oldView)
            oldView.CollectionChanged -= OnViewCollectionChanged;
        _items.Clear();
        var source = new List<CatalogItemViewModel>(presets.Count);
        foreach (var preset in presets)
        {
            var item = new CatalogItemViewModel(preset, Renderer)
            {
                IsFavorite = _favoriteCatalogIds.Contains(preset.Id),
            };
            _items[preset.Id] = item;
            source.Add(item);
        }

        _itemsView = CollectionViewSource.GetDefaultView(source);
        _itemsView.Filter = IsVisibleItem;
        // LiveShapingList owns DispatcherObjects. Only enable it for a real WPF
        // window; headless async consumers may resume on a different thread.
        if (Application.Current is { } app && app.Dispatcher.CheckAccess() &&
            _itemsView is ICollectionViewLiveShaping { CanChangeLiveFiltering: true } live)
        {
            live.LiveFilteringProperties.Add(nameof(CatalogItemViewModel.IsFavorite));
            live.IsLiveFiltering = true;
        }
        if (_itemsView is INotifyCollectionChanged viewChanges)
            viewChanges.CollectionChanged += OnViewCollectionChanged;

        _favoriteCount = source.Count(item => item.IsFavorite);
        foreach (var chip in Categories)
            chip.Count = chip.Key == CatalogCategories.Favorites
                ? _favoriteCount : _catalog.CountIn(chip.Key);

        IsLoading = false;
        ApplyFilter();
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs e)
    {
        foreach (var option in ColorFilters) option.Refresh();
        foreach (var chip in Categories) chip.RefreshLabel();
        ApplyFilter();
    }

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
        if (IsLoading || _itemsView is null) return;

        // Search/category/color criteria are recalculated only when those inputs
        // change. A star toggle is handled by WPF live filtering instead.
        _matchingIds = _catalog.FilterPresets(SearchText,
                _selectedCategory == CatalogCategories.Favorites ? CatalogCategories.All : _selectedCategory)
            .Where(preset => MatchesColor(preset.Color, _selectedColorFilter))
            .Select(preset => preset.Id)
            .ToHashSet();
        _refreshingView = true;
        try { _itemsView.Refresh(); }
        finally { _refreshingView = false; }
        RebuildRows();
    }

    private bool IsVisibleItem(object value) =>
        value is CatalogItemViewModel item && _matchingIds.Contains(item.Preset.Id)
        && (_selectedCategory != CatalogCategories.Favorites || item.IsFavorite);

    private void OnViewCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (!_disposed && !_refreshingView) RebuildRows();
    }

    internal static bool MatchesColor(string hex, LibraryColorFilter filter)
    {
        if (filter == LibraryColorFilter.All) return true;
        var color = JsonColorConverter.Parse(hex);
        if (color is null) return false;
        var (r, g, b) = ((int)color.Value.R, (int)color.Value.G, (int)color.Value.B);
        return filter switch
        {
            LibraryColorFilter.White => Math.Min(r, Math.Min(g, b)) >= 180 && Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b)) <= 50,
            LibraryColorFilter.Yellow => r >= 150 && g >= 120 && b < Math.Min(r, g) * 0.65,
            LibraryColorFilter.Green => g >= 80 && g > r * 1.2 && g > b * 1.2,
            LibraryColorFilter.Red => r >= 80 && r > g * 1.2 && r > b * 1.2,
            LibraryColorFilter.Blue => b >= 80 && b > r * 1.2 && b >= g * 0.9,
            _ => false,
        };
    }

    private void RebuildRows()
    {
        var rowIndex = 0;
        var visibleCount = 0;
        var cells = new CatalogItemViewModel[_columnCount];
        var cellCount = 0;

        void CommitRow()
        {
            if (cellCount == 0) return;
            if (cellCount != cells.Length) Array.Resize(ref cells, cellCount);

            var same = rowIndex < _rows.Count && _rows[rowIndex].Items.Count == cellCount;
            if (same)
                for (var index = 0; index < cellCount; index++)
                    if (!ReferenceEquals(_rows[rowIndex].Items[index], cells[index]))
                    {
                        same = false;
                        break;
                    }
            if (!same)
            {
                var row = new CatalogRow(cells);
                if (rowIndex < _rows.Count) _rows[rowIndex] = row;
                else _rows.Add(row);
            }
            rowIndex++;
            cells = new CatalogItemViewModel[_columnCount];
            cellCount = 0;
        }

        if (_itemsView is not null)
            foreach (CatalogItemViewModel item in _itemsView)
            {
                cells[cellCount++] = item;
                visibleCount++;
                if (cellCount == _columnCount) CommitRow();
            }
        CommitRow();
        while (_rows.Count > rowIndex) _rows.RemoveAt(_rows.Count - 1);

        ResultText = Tr.Format("Library_Count", visibleCount);
        IsEmpty = visibleCount == 0;
        EmptyText = _items.Count == 0 ? Tr.Get("Library_Missing") : Tr.Get("Library_Empty");
    }

    [RelayCommand]
    private void ToggleFavorite(CatalogItemViewModel? item)
    {
        if (item is null) return;
        item.IsFavorite = !item.IsFavorite;
        if (item.IsFavorite) _favoriteCatalogIds.Add(item.Preset.Id);
        else _favoriteCatalogIds.Remove(item.Preset.Id);
        _favoriteCount += item.IsFavorite ? 1 : -1;
        if (_settings is not null)
        {
            _settings.Current.FavoriteCatalogPresetIds = [.. _favoriteCatalogIds];
            _settings.RequestSave();
        }
        var chip = Categories.First(category => category.Key == CatalogCategories.Favorites);
        chip.Count = _favoriteCount;
        if (_selectedCategory == CatalogCategories.Favorites &&
            _itemsView is ICollectionViewLiveShaping { IsLiveFiltering: not true })
            _itemsView.Refresh();
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
            profile.IsFavorite = item.IsFavorite;
            var added = await _library.AddAsync(profile).ConfigureAwait(true);

            _logger.LogInformation("Thêm mẫu '{Template}' từ thư viện thành preset '{Name}'.", item.Name, added.Name);
            Toast.Show(Tr.Format("Library_Added", added.Name));
            _tray?.ShowNotification(Tr.Get("Library_Title"), Tr.Format("Library_AddedToast", added.Name));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _logger.LogError(ex, "Không thêm được mẫu '{Template}'.", item.Name);
            Toast.Show(Tr.Format("Library_AddFailed", ex.Message));
        }
    }

    internal static string CategoryLabel(string key) => key switch
    {
        CatalogCategories.All => Tr.Get("Library_CatAll"),
        CatalogCategories.Favorites => Tr.Get("Library_CatFavorites"),
        CatalogCategories.ProPlayers => Tr.Get("Library_CatPro"),
        CatalogCategories.Dots => Tr.Get("Library_CatDots"),
        CatalogCategories.Crosses => Tr.Get("Library_CatCrosses"),
        CatalogCategories.Circles => Tr.Get("Library_CatCircles"),
        CatalogCategories.Accessibility => Tr.Get("Library_CatAccessibility"),
        _ => Tr.Get("Library_CatOther"),
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_itemsView is INotifyCollectionChanged viewChanges)
            viewChanges.CollectionChanged -= OnViewCollectionChanged;
        PropertyChangedEventManager.RemoveHandler(TranslationSource.Instance, OnLanguageChanged, string.Empty);
        Toast.Dispose();
    }
}

public enum LibraryColorFilter { All, Green, Red, Blue, Yellow, White }

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
public sealed partial class CatalogItemViewModel(CatalogPreset preset, ICrosshairRenderer renderer) : ObservableObject
{
    private CrosshairProfile? _previewProfile;

    [ObservableProperty] private bool _isFavorite;

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
    private string _label = label;

    public string Key { get; } = key;

    public string Label => _label;

    internal void RefreshLabel() =>
        SetProperty(ref _label, PresetLibraryViewModel.CategoryLabel(Key), nameof(Label));

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
