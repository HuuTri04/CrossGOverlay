using System.ComponentModel;
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
    private bool _disposed;

    public CrosshairEditorViewModel(ICrosshairRenderer renderer, IDialogService dialogs)
    {
        Renderer = renderer;
        _dialogs = dialogs;

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

    /// <summary>Danh sách giữ nguyên khi đổi ngôn ngữ; từng mục tự báo nhãn đã đổi.</summary>
    public IReadOnlyList<LocalizedOption<CrosshairShape>> Shapes { get; }

    /// <summary>Đường dẫn ảnh, hoặc nhãn "chưa chọn" đã dịch.</summary>
    public string ImagePathDisplay =>
        string.IsNullOrWhiteSpace(Profile?.Image.FilePath)
            ? Tr.Get("Editor_NoImage")
            : Profile!.Image.FilePath!;

    public bool ShowLineSettings => Profile?.Shape
        is CrosshairShape.Cross or CrosshairShape.TShape or CrosshairShape.XShape;

    public bool ShowRingSettings => Profile is not null && Profile.Shape != CrosshairShape.CustomImage;

    public bool ShowImageSettings => Profile?.Shape == CrosshairShape.CustomImage;

    /// <summary>Ảnh bitmap không vẽ viền được, nên ẩn cả nhóm outline khi đang ở chế độ ảnh.</summary>
    public bool ShowOutlineSettings => Profile is not null && Profile.Shape != CrosshairShape.CustomImage;

    private static IReadOnlyList<LocalizedOption<CrosshairShape>> BuildShapes() =>
    [
        new(CrosshairShape.Cross, "Shape_Cross"),
        new(CrosshairShape.TShape, "Shape_TShape"),
        new(CrosshairShape.XShape, "Shape_XShape"),
        new(CrosshairShape.Dot, "Shape_Dot"),
        new(CrosshairShape.Circle, "Shape_Circle"),
        new(CrosshairShape.CircleDot, "Shape_CircleDot"),
        new(CrosshairShape.Square, "Shape_Square"),
        new(CrosshairShape.CustomImage, "Shape_CustomImage"),
    ];

    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs e)
    {
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
    private void BrowseImage()
    {
        if (Profile is null) return;

        var path = _dialogs.PickFileToOpen(Tr.Get("Editor_ImageFilter"));
        if (path is null) return;

        Profile.Image.FilePath = path;
        Profile.Shape = CrosshairShape.CustomImage;
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
