using System.ComponentModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CrosshairOverlay.Core.Abstractions;
using CrosshairOverlay.Core.Models;
using CrosshairOverlay.Localization;

namespace CrosshairOverlay.ViewModels;

/// <summary>
/// ViewModel của khu vực chỉnh sửa crosshair.
/// </summary>
/// <remarks>
/// Cố tình rất mỏng. View bind THẲNG vào <see cref="CrosshairProfile"/> và các khối con của nó,
/// nên không có lớp property trung gian nào ở đây.
/// </remarks>
public sealed partial class CrosshairEditorViewModel : ObservableObject, IDisposable
{
    private readonly IDialogService _dialogs;
    private readonly ICustomImageStore _images;
    private bool _disposed;

    public CrosshairEditorViewModel(ICrosshairRenderer renderer, IDialogService dialogs, ICustomImageStore images)
    {
        Renderer = renderer;
        _dialogs = dialogs;
        _images = images;

        Types = BuildTypes();
        Shapes = BuildShapes();
        TranslationSource.Instance.PropertyChanged += OnLanguageChanged;
    }

    /// <summary>Khung preview dùng chung renderer với overlay, nên hai bên không thể lệch nhau.</summary>
    public ICrosshairRenderer Renderer { get; }

    [ObservableProperty] private CrosshairProfile? _profile;

    /// <summary>Độ phóng của khung preview. Crosshair thật chỉ vài chục pixel nên cần zoom để chỉnh.</summary>
    [ObservableProperty] private double _previewZoom = 4d;

    [ObservableProperty] private bool _showCheckerboard = true;

    [ObservableProperty] private bool _useDarkPreviewBackground = true;

    /// <summary>Hai chế độ: tâm ngắm tiêu chuẩn, hoặc dùng hình ảnh.</summary>
    public IReadOnlyList<LocalizedOption<CrosshairType>> Types { get; }

    /// <summary>Danh sách giữ nguyên khi đổi ngôn ngữ; từng mục tự báo nhãn đã đổi.</summary>
    public IReadOnlyList<LocalizedOption<CrosshairShape>> Shapes { get; }

    /// <summary>Đang ở chế độ tiêu chuẩn — hiện hình dạng, màu, các nhóm nhánh, vòng, viền, vị trí.</summary>
    public bool IsStandardMode => Profile?.Type == CrosshairType.Standard;

    /// <summary>Đang ở chế độ ảnh — ẩn toàn bộ cài đặt vẽ tay, chỉ hiện nhóm ảnh.</summary>
    public bool IsImageMode => Profile?.Type == CrosshairType.Image;

    /// <summary>
    /// Tên ảnh đang dùng, hoặc nhãn "chưa chọn"/"không tìm thấy" đã dịch.
    /// </summary>
    /// <remarks>
    /// Chỉ hiện tên file, không hiện đường dẫn kho. Báo rõ "không tìm thấy" thay vì để khung
    /// preview trống trơn: preset nhận từ người khác trỏ tới ảnh không có trên máy này.
    /// </remarks>
    public string ImagePathDisplay
    {
        get
        {
            var stored = Profile?.Image.FilePath;
            if (string.IsNullOrWhiteSpace(stored)) return Tr.Get("Editor_NoImage");

            var name = Path.GetFileName(stored);
            var resolved = _images.Resolve(stored);

            return resolved is not null && File.Exists(resolved)
                ? name
                : Tr.Format("Editor_ImageMissing", name);
        }
    }

    public bool ShowLineSettings => IsStandardMode && Profile?.Shape
        is CrosshairShape.Cross or CrosshairShape.TShape or CrosshairShape.XShape;

    public bool ShowRingSettings => IsStandardMode;

    public bool ShowImageSettings => IsImageMode;

    public bool ShowOutlineSettings => IsStandardMode;

    private static IReadOnlyList<LocalizedOption<CrosshairType>> BuildTypes() =>
    [
        new(CrosshairType.Standard, "Type_Standard"),
        new(CrosshairType.Image, "Type_Image"),
    ];

    private static IReadOnlyList<LocalizedOption<CrosshairShape>> BuildShapes() =>
    [
        new(CrosshairShape.Cross, "Shape_Cross"),
        new(CrosshairShape.TShape, "Shape_TShape"),
        new(CrosshairShape.XShape, "Shape_XShape"),
        new(CrosshairShape.Dot, "Shape_Dot"),
        new(CrosshairShape.Circle, "Shape_Circle"),
        new(CrosshairShape.CircleDot, "Shape_CircleDot"),
        new(CrosshairShape.Square, "Shape_Square"),
    ];

    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs e)
    {
        foreach (var type in Types) type.Refresh();
        foreach (var shape in Shapes) shape.Refresh();
        OnPropertyChanged(nameof(ImagePathDisplay));
    }

    partial void OnProfileChanged(CrosshairProfile? oldValue, CrosshairProfile? newValue)
    {
        if (oldValue is not null) ProfileNotifications.Hook(oldValue, OnProfilePartChanged, subscribe: false);
        if (newValue is not null) ProfileNotifications.Hook(newValue, OnProfilePartChanged, subscribe: true);

        RaiseDerived();
    }

    private void OnProfilePartChanged(object? sender, PropertyChangedEventArgs e) => RaiseDerived();

    private void RaiseDerived()
    {
        OnPropertyChanged(nameof(IsStandardMode));
        OnPropertyChanged(nameof(IsImageMode));
        OnPropertyChanged(nameof(ShowLineSettings));
        OnPropertyChanged(nameof(ShowRingSettings));
        OnPropertyChanged(nameof(ShowImageSettings));
        OnPropertyChanged(nameof(ShowOutlineSettings));
        OnPropertyChanged(nameof(ImagePathDisplay));
    }

    [RelayCommand]
    private void ResetOffset()
    {
        if (Profile is null) return;
        Profile.OffsetX = 0;
        Profile.OffsetY = 0;
    }

    [RelayCommand]
    private void ResetRotation()
    {
        if (Profile is null) return;
        Profile.Rotation = 0;
    }

    [RelayCommand]
    private void ResetImageOffset()
    {
        if (Profile is null) return;
        Profile.Image.OffsetX = 0;
        Profile.Image.OffsetY = 0;
    }

    /// <summary>
    /// Chọn ảnh, chép vào kho của ứng dụng, rồi trỏ preset vào BẢN CHÉP.
    /// </summary>
    /// <remarks>
    /// Kiểm tra và chép chạy trên thread pool: ảnh tới 20 MB phải đọc, giải mã thử và băm —
    /// làm trên luồng giao diện thì cửa sổ đứng hình trong lúc đó.
    /// </remarks>
    [RelayCommand]
    private async Task BrowseImageAsync()
    {
        if (Profile is null) return;

        var path = _dialogs.PickFileToOpen(Tr.Get("Editor_ImageFilter"));
        if (path is null) return;

        var profile = Profile;
        string stored;

        try
        {
            stored = await Task.Run(() => _images.Import(path));
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            _dialogs.ShowMessage(Tr.Get("Image_ErrTitle"), ex.Message, isError: true);
            return;
        }

        profile.Image.FilePath = stored;
        profile.Type = CrosshairType.Image;
        OnPropertyChanged(nameof(ImagePathDisplay));
    }

    [RelayCommand]
    private void ClearImage()
    {
        if (Profile is null) return;
        Profile.Image.FilePath = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        TranslationSource.Instance.PropertyChanged -= OnLanguageChanged;
        if (Profile is not null) ProfileNotifications.Hook(Profile, OnProfilePartChanged, subscribe: false);
    }
}
