using System.ComponentModel;
using System.IO;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CrosshairOverlay.Core.Abstractions;
using CrosshairOverlay.Core.Models;
using CrosshairOverlay.Localization;
using CrosshairOverlay.Services.System;

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
    private const int PreviewBackgroundMaxDecodedSide = 1280;
    private readonly IDialogService _dialogs;
    private readonly ICustomImageStore _images;
    private readonly IAppSettingsService _settings;
    private int _previewBackgroundVersion;
    private CancellationTokenSource? _previewBackgroundLoadCancellation;
    private bool _previewBackgroundViewActive;
    private bool _disposed;

    public CrosshairEditorViewModel(
        ICrosshairRenderer renderer, IDialogService dialogs, ICustomImageStore images, IAppSettingsService settings)
    {
        Renderer = renderer;
        _dialogs = dialogs;
        _images = images;
        _settings = settings;

        // Gán thẳng vào field: đây là ĐỌC giá trị đã lưu, không được kích hoạt lưu ngược lại.
        _showCheckerboard = settings.Current.PreviewShowCheckerboard;
        _useDarkPreviewBackground = settings.Current.PreviewShowDarkBackground;
        Types = BuildTypes();
        Shapes = BuildShapes();
        PropertyChangedEventManager.AddHandler(TranslationSource.Instance, OnLanguageChanged, string.Empty);
    }

    /// <summary>Khung preview dùng chung renderer với overlay, nên hai bên không thể lệch nhau.</summary>
    public ICrosshairRenderer Renderer { get; }

    [ObservableProperty] private CrosshairProfile? _profile;

    /// <summary>Độ phóng của khung preview. Crosshair thật chỉ vài chục pixel nên cần zoom để chỉnh.</summary>
    [ObservableProperty] private double _previewZoom = 4d;

    /// <summary>Nền ca-rô của khung preview. Được ghi nhớ qua các lần mở app.</summary>
    [ObservableProperty] private bool _showCheckerboard;

    /// <summary>Nền tối của khung preview. Được ghi nhớ qua các lần mở app.</summary>
    [ObservableProperty] private bool _useDarkPreviewBackground;

    /// <summary>Ảnh chỉ nạp khi mở Settings hoặc khi người dùng chọn; không tham gia vòng render của overlay.</summary>
    [ObservableProperty] private BitmapSource? _customPreviewBackgroundImage;

    // A path alone is insufficient: the file can be missing or fail to decode.
    public bool HasCustomPreviewBackground => CustomPreviewBackgroundImage is not null;
    public bool CanResetPreviewBackground => !string.IsNullOrWhiteSpace(_settings.Current.CustomPreviewBgPath);
    public bool ArePreviewBackdropOptionsEnabled => !HasCustomPreviewBackground;
    public bool IsCheckerboardBgVisible => !HasCustomPreviewBackground && ShowCheckerboard;
    public bool IsDarkBgVisible => !HasCustomPreviewBackground && UseDarkPreviewBackground;

    /// <summary>Only retain the preview bitmap while Settings is visible.</summary>
    public void SetPreviewBackgroundViewActive(bool active)
    {
        if (_disposed || _previewBackgroundViewActive == active) return;
        _previewBackgroundViewActive = active;
        if (active)
        {
            if (CustomPreviewBackgroundImage is null)
                _ = LoadSavedPreviewBackgroundAsync(_settings.Current.CustomPreviewBgPath);
            return;
        }

        Interlocked.Increment(ref _previewBackgroundVersion);
        CancelPreviewBackgroundLoad();
        CustomPreviewBackgroundImage = null;
    }

    partial void OnCustomPreviewBackgroundImageChanged(BitmapSource? value)
    {
        OnPropertyChanged(nameof(HasCustomPreviewBackground));
        OnPropertyChanged(nameof(ArePreviewBackdropOptionsEnabled));
        OnPropertyChanged(nameof(IsCheckerboardBgVisible));
        OnPropertyChanged(nameof(IsDarkBgVisible));
    }

    [RelayCommand]
    private async Task ChoosePreviewBackgroundAsync()
    {
        var path = _dialogs.PickFileToOpen(
            Tr.Get("Preview_ImageFilter"),
            Path.GetDirectoryName(_settings.Current.CustomPreviewBgPath));
        if (path is null) return;

        var version = Interlocked.Increment(ref _previewBackgroundVersion);
        var image = await TryDecodePreviewAsync(path, reportError: true);
        if (image is null || _disposed || version != _previewBackgroundVersion) return;

        CustomPreviewBackgroundImage = image;
        _settings.Current.CustomPreviewBgPath = path;
        OnPropertyChanged(nameof(CanResetPreviewBackground));
        SavePreviewOptions();
    }

    [RelayCommand]
    private void ResetPreviewBackground()
    {
        Interlocked.Increment(ref _previewBackgroundVersion);
        CancelPreviewBackgroundLoad();
        CustomPreviewBackgroundImage = null;
        _settings.Current.CustomPreviewBgPath = null;
        OnPropertyChanged(nameof(CanResetPreviewBackground));
        SavePreviewOptions();
    }

    private async Task LoadSavedPreviewBackgroundAsync(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
        var version = _previewBackgroundVersion;
        var image = await TryDecodePreviewAsync(path, reportError: false);
        if (!_disposed && _previewBackgroundViewActive && version == _previewBackgroundVersion
            && _settings.Current.CustomPreviewBgPath == path)
            CustomPreviewBackgroundImage = image;
    }

    private async Task<BitmapSource?> TryDecodePreviewAsync(string path, bool reportError)
    {
        var cancellation = new CancellationTokenSource();
        Interlocked.Exchange(ref _previewBackgroundLoadCancellation, cancellation)?.Cancel();
        try
        {
            return await AppBackgroundImageLoader.DecodeAsync(
                path, maxDecodedSide: PreviewBackgroundMaxDecodedSide, cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { return null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException
                                   or ArgumentException or InvalidOperationException
                                   or System.Runtime.InteropServices.COMException)
        {
            if (reportError && !_disposed) _dialogs.ShowMessage(Tr.Get("Preview_ImageErrorTitle"),
                Tr.Format("Preview_ImageError", ex.Message), isError: true);
            return null;
        }
        finally
        {
            Interlocked.CompareExchange(ref _previewBackgroundLoadCancellation, null, cancellation);
            cancellation.Dispose();
        }
    }

    private void CancelPreviewBackgroundLoad() =>
        Interlocked.Exchange(ref _previewBackgroundLoadCancellation, null)?.Cancel();

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

    partial void OnShowCheckerboardChanged(bool value)
    {
        OnPropertyChanged(nameof(IsCheckerboardBgVisible));
        if (_settings.Current.PreviewShowCheckerboard == value) return;

        _settings.Current.PreviewShowCheckerboard = value;
        SavePreviewOptions();
    }

    partial void OnUseDarkPreviewBackgroundChanged(bool value)
    {
        OnPropertyChanged(nameof(IsDarkBgVisible));
        if (_settings.Current.PreviewShowDarkBackground == value) return;

        _settings.Current.PreviewShowDarkBackground = value;
        SavePreviewOptions();
    }

    /// <summary>Persist the preview choice immediately; the writer opens the file off the UI thread.</summary>
    private void SavePreviewOptions() => _ = _settings.SaveAsync();

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
        (int Width, int Height)? size;

        try
        {
            (stored, size) = await Task.Run(() =>
            {
                var result = _images.Import(path);
                return (result, _images.GetPixelSize(result));
            });
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            _dialogs.ShowMessage(Tr.Get("Image_ErrTitle"), ex.Message, isError: true);
            return;
        }

        // Chỉnh Scale TRƯỚC khi gán ảnh: overlay và preview dựng lại với đúng cỡ ngay lần đầu, không
        // loé lên ảnh kích thước gốc che màn hình. Người dùng vẫn kéo thanh trượt lại được.
        if (size is { } pixels)
            profile.Image.Scale = CustomImageSettings.SuggestScale(pixels.Width, pixels.Height);

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
        Interlocked.Increment(ref _previewBackgroundVersion);
        CancelPreviewBackgroundLoad();
        CustomPreviewBackgroundImage = null;

        PropertyChangedEventManager.RemoveHandler(TranslationSource.Instance, OnLanguageChanged, string.Empty);
        if (Profile is not null) ProfileNotifications.Hook(Profile, OnProfilePartChanged, subscribe: false);
    }
}
