using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CrosshairOverlay.Core.Abstractions;
using CrosshairOverlay.Core.Models;
using CrosshairOverlay.Localization;
using CrosshairOverlay.Services.Import;

namespace CrosshairOverlay.ViewModels;

public enum PixelTool { Brush, Eraser }

/// <summary>One small bitmap and an ARGB array; no WPF element is allocated for individual pixels.</summary>
public sealed partial class PixelArtViewModel : ObservableObject
{
    private readonly IPresetLibrary _library;
    private readonly ICustomImageStore _images;
    private readonly IOverlayController _overlay;
    private uint[] _pixels;
    private readonly uint[] _singlePixel = new uint[1];
    private Color _selectedColor = Colors.Lime;
    private Brush _selectedColorBrush;
    private CrosshairProfile? _lastSavedPreset;
    private string? _lastSavedCode;
    private string? _lastSavedName;
    private bool _strokeChanged;
    private bool _saving;

    public PixelArtViewModel(IPresetLibrary library, ICustomImageStore images, IOverlayController overlay)
    {
        _library = library;
        _images = images;
        _overlay = overlay;
        _pixels = new uint[GridSize * GridSize];
        Bitmap = CreateBitmap(GridSize, _pixels);
        _selectedColorBrush = CreateFrozenBrush(_selectedColor);
        PresetName = Tr.Get("Pixel_DefaultName");
        ShareCode = PixelArtCode.Encode(GridSize, _pixels);
    }

    public IReadOnlyList<int> GridSizes { get; } = [16, 24, 32];

    public WriteableBitmap Bitmap { get; private set; }

    public Brush SelectedColorBrush => _selectedColorBrush;

    public bool IsBrushSelected => Tool == PixelTool.Brush;

    public bool IsEraserSelected => Tool == PixelTool.Eraser;

    [ObservableProperty] private int _gridSize = 24;
    [ObservableProperty] private PixelTool _tool = PixelTool.Brush;
    [ObservableProperty] private bool _showCenterGuide = true;
    [ObservableProperty] private string _customHex = "#00FF00";
    [ObservableProperty] private string _shareCode = string.Empty;
    [ObservableProperty] private string _presetName = string.Empty;
    [ObservableProperty] private string _statusMessage = string.Empty;

    partial void OnToolChanged(PixelTool value)
    {
        OnPropertyChanged(nameof(IsBrushSelected));
        OnPropertyChanged(nameof(IsEraserSelected));
    }

    partial void OnGridSizeChanged(int value)
    {
        if (!PixelArtCode.IsSupportedSize(value)) return;

        var oldSize = (int)Math.Sqrt(_pixels.Length);
        var resized = new uint[value * value];
        var count = Math.Min(oldSize, value);
        var sourceStart = (oldSize - count) / 2;
        var targetStart = (value - count) / 2;
        for (var row = 0; row < count; row++)
            Array.Copy(_pixels, (sourceStart + row) * oldSize + sourceStart,
                resized, (targetStart + row) * value + targetStart, count);

        _pixels = resized;
        Bitmap = CreateBitmap(value, resized);
        OnPropertyChanged(nameof(Bitmap));
        RefreshShareCode();
    }

    [RelayCommand] private void UseBrush() => Tool = PixelTool.Brush;

    [RelayCommand] private void UseEraser() => Tool = PixelTool.Eraser;

    [RelayCommand] private void ToggleCenterGuide() => ShowCenterGuide = !ShowCenterGuide;

    [RelayCommand]
    private void ClearCanvas()
    {
        Array.Clear(_pixels);
        Bitmap.WritePixels(new Int32Rect(0, 0, GridSize, GridSize), _pixels, GridSize * 4, 0);
        RefreshShareCode();
        StatusMessage = Tr.Get("Pixel_Cleared");
    }

    [RelayCommand]
    private void SelectPaletteColor(string? hex)
    {
        if (TryReadHex(hex, out var color)) SelectColor(color);
    }

    [RelayCommand]
    private void SelectCustomColor()
    {
        if (TryReadHex(CustomHex, out var color)) SelectColor(color);
        else StatusMessage = Tr.Get("Pixel_InvalidColor");
    }

    private void SelectColor(Color color)
    {
        _selectedColor = color;
        _selectedColorBrush = CreateFrozenBrush(color);
        OnPropertyChanged(nameof(SelectedColorBrush));
        CustomHex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        Tool = PixelTool.Brush;
    }

    /// <summary>Draw a continuous stroke even when mouse events skip intermediate cells.</summary>
    public void DrawLine(int fromX, int fromY, int toX, int toY, bool erase)
    {
        var dx = Math.Abs(toX - fromX);
        var sx = fromX < toX ? 1 : -1;
        var dy = -Math.Abs(toY - fromY);
        var sy = fromY < toY ? 1 : -1;
        var error = dx + dy;
        var color = erase || Tool == PixelTool.Eraser
            ? 0u
            : 0xFF000000u | (uint)_selectedColor.R << 16 | (uint)_selectedColor.G << 8 | _selectedColor.B;

        while (true)
        {
            _strokeChanged |= PaintPixel(fromX, fromY, color);
            if (fromX == toX && fromY == toY) break;
            var twiceError = 2 * error;
            if (twiceError >= dy) { error += dy; fromX += sx; }
            if (twiceError <= dx) { error += dx; fromY += sy; }
        }
    }

    public void FinishStroke()
    {
        if (!_strokeChanged) return;
        _strokeChanged = false;
        RefreshShareCode();
    }

    private bool PaintPixel(int x, int y, uint color)
    {
        if ((uint)x >= GridSize || (uint)y >= GridSize) return false;
        var index = y * GridSize + x;
        if (_pixels[index] == color) return false;

        _pixels[index] = color;
        _singlePixel[0] = color;
        Bitmap.WritePixels(new Int32Rect(x, y, 1, 1), _singlePixel, 4, 0);
        return true;
    }

    private void RefreshShareCode() => ShareCode = PixelArtCode.Encode(GridSize, _pixels);

    [RelayCommand]
    private void CopyCode()
    {
        RefreshShareCode();
        try
        {
            Clipboard.SetText(ShareCode);
            StatusMessage = Tr.Get("Pixel_Copied");
        }
        catch (Exception ex) when (ex is ExternalException or InvalidOperationException)
        {
            StatusMessage = Tr.Get("Pixel_CopyFailed");
        }
    }

    [RelayCommand]
    private void LoadCode()
    {
        if (!PixelArtCode.TryDecode(ShareCode, out var size, out var pixels))
        {
            StatusMessage = Tr.Get("Pixel_InvalidCode");
            return;
        }

        _pixels = pixels;
        if (GridSize != size) GridSize = size;
        Bitmap = CreateBitmap(size, _pixels);
        OnPropertyChanged(nameof(Bitmap));
        RefreshShareCode();
        StatusMessage = Tr.Get("Pixel_Loaded");
    }

    [RelayCommand] private Task SavePresetAsync() => SaveOrApplyAsync(saveToLibrary: true);

    [RelayCommand] private Task ApplyDirectAsync() => SaveOrApplyAsync(saveToLibrary: false);

    private async Task SaveOrApplyAsync(bool saveToLibrary)
    {
        if (_saving) return;
        if (_pixels.All(pixel => pixel == 0))
        {
            StatusMessage = Tr.Get("Pixel_EmptyCanvas");
            return;
        }

        var name = string.IsNullOrWhiteSpace(PresetName) ? Tr.Get("Pixel_DefaultName") : PresetName.Trim();
        var code = PixelArtCode.Encode(GridSize, _pixels);
        if (!saveToLibrary && _lastSavedCode == code && _lastSavedName == name &&
            _lastSavedPreset is not null && _library.Presets.Contains(_lastSavedPreset))
        {
            _library.SetActive(_lastSavedPreset);
            StatusMessage = Tr.Get("Pixel_Applied");
            return;
        }

        _saving = true;
        try
        {
            var size = GridSize;
            var png = EncodePng(Bitmap);
            var stored = await Task.Run(() => _images.ImportBytes("pixel-art.png", png));
            var preset = new CrosshairProfile { Name = name, Type = CrosshairType.Image };
            preset.Image.FilePath = stored;
            preset.Image.Scale = CustomImageSettings.SuggestScale(size, size);
            if (saveToLibrary)
            {
                _lastSavedPreset = await _library.AddAsync(preset);
                _lastSavedCode = code;
                _lastSavedName = name;
                StatusMessage = Tr.Get("Pixel_SavedApplied");
            }
            else
            {
                _overlay.SetProfile(preset);
                StatusMessage = Tr.Get("Pixel_Applied");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException
                                   or ArgumentException or InvalidOperationException)
        {
            StatusMessage = Tr.Format("Pixel_SaveFailed", ex.Message);
        }
        finally { _saving = false; }
    }

    private static byte[] EncodePng(BitmapSource bitmap)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static WriteableBitmap CreateBitmap(int size, uint[] pixels)
    {
        var bitmap = new WriteableBitmap(size, size, 96, 96, PixelFormats.Bgra32, null);
        bitmap.WritePixels(new Int32Rect(0, 0, size, size), pixels, size * 4, 0);
        return bitmap;
    }

    private static Brush CreateFrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static bool TryReadHex(string? value, out Color color)
    {
        color = default;
        if (value is not { Length: 7 } || value[0] != '#') return false;
        if (!uint.TryParse(value.AsSpan(1), NumberStyles.AllowHexSpecifier,
                CultureInfo.InvariantCulture, out var rgb)) return false;
        color = Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        return true;
    }
}
