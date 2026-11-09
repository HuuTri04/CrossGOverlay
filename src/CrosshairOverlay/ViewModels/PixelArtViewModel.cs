using System.Buffers;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CrosshairOverlay.Core.Abstractions;
using CrosshairOverlay.Core.Models;
using CrosshairOverlay.Localization;
using CrosshairOverlay.Services.Import;

namespace CrosshairOverlay.ViewModels;

public enum PixelTool { Brush, Eraser }
public enum DrawingInputMode { Mouse, Stylus }
internal readonly record struct PixelStrokeSegment(int FromX, int FromY, int ToX, int ToY, uint Color);

/// <summary>One bitmap and a bounded delta history; no per-pixel WPF elements.</summary>
public sealed partial class PixelArtViewModel : ObservableObject, IDisposable
{
    private readonly IPresetLibrary _library;
    private readonly ICustomImageStore _images;
    private readonly IOverlayController _overlay;
    private readonly IDialogService? _dialogs;
    private uint[] _pixels;
    private int _coloredPixelCount;
    private Color _selectedColor = Colors.Lime;
    private Brush _selectedColorBrush;
    private CrosshairProfile? _lastSavedPreset;
    private long _lastSavedRevision = -1;
    private string? _lastSavedName;
    private long _canvasRevision;
    private long _historyBytes;
    private readonly LinkedList<PixelCommand> _undoHistory = new();
    private readonly LinkedList<PixelCommand> _redoHistory = new();
    private readonly Dictionary<int, PixelChange> _stroke = [];
    private const int MaxHistory = 50;
    private const int MaxStrokeChanges = 65_536;
    private const long MaxHistoryBytes = 32L * 1024 * 1024;
    private readonly record struct PixelChange(int Index, uint Before, uint After);
    private sealed record PixelCommand(int Size, PixelChange[] Changes);
    private bool _saving;
    private readonly DispatcherTimer _codeUpdateTimer = new(DispatcherPriority.Background)
    {
        Interval = TimeSpan.FromMilliseconds(250),
    };
    private bool _isGeneratingCode;
    private bool _codeRefreshRequested;
    private bool _isLoadingFromCode;
    private bool _disposed;
    private string? _generatedLargeCode;
    private long _generatedLargeCodeRevision = -1;
    private bool _settingGeneratedShareCode;
    private bool _shareCodeClearedByUser;
    internal bool IsEditingShareCode { get; set; }

    public PixelArtViewModel(IPresetLibrary library, ICustomImageStore images, IOverlayController overlay,
        IDialogService? dialogs = null)
    {
        _library = library;
        _images = images;
        _overlay = overlay;
        _dialogs = dialogs;
        _pixels = new uint[GridSize * GridSize];
        Bitmap = CreateBitmap(GridSize, _pixels);
        _selectedColorBrush = CreateFrozenBrush(_selectedColor);
        PresetName = Tr.Get("Pixel_DefaultName");
        _codeUpdateTimer.Tick += OnCodeUpdateTimerTick;
    }

    public WriteableBitmap Bitmap { get; private set; }

    public int CanvasDisplaySize => Math.Max(384, GridSize);

    public Brush SelectedColorBrush => _selectedColorBrush;

    public bool IsBrushSelected => Tool == PixelTool.Brush;

    public bool IsEraserSelected => Tool == PixelTool.Eraser;

    public bool IsMouseInputSelected => CurrentInputMode == DrawingInputMode.Mouse;

    public bool IsStylusInputSelected => CurrentInputMode == DrawingInputMode.Stylus;

    public string GridSizeInput
    {
        get => _gridSizeInput;
        set => SetProperty(ref _gridSizeInput, value);
    }

    private string _gridSizeInput = "24";

    private int _gridSize = 24;
    public int GridSize
    {
        get => _gridSize;
        set
        {
            if (!PixelArtCode.IsSupportedSize(value) || value == _gridSize) return;
            var oldSize = _gridSize;
            var resized = new uint[value * value];
            var count = Math.Min(oldSize, value);
            var sourceStart = (oldSize - count) / 2;
            var targetStart = (value - count) / 2;
            for (var row = 0; row < count; row++)
                Array.Copy(_pixels, (sourceStart + row) * oldSize + sourceStart,
                    resized, (targetStart + row) * value + targetStart, count);

            var bitmap = CreateBitmap(value, resized);
            _pixels = resized;
            _coloredPixelCount = CountColoredPixels(resized);
            Bitmap = bitmap;
            SetProperty(ref _gridSize, value);
            GridSizeInput = value.ToString(CultureInfo.InvariantCulture);
            OnPropertyChanged(nameof(Bitmap));
            OnPropertyChanged(nameof(CanvasDisplaySize));
            ResetHistory();
            _canvasRevision++;
            // Resizing is an explicit edit: publish the resized canvas even if
            // the code box had focus before the Apply action.
            IsEditingShareCode = false;
            RefreshShareCode(immediate: true);
        }
    }
    [ObservableProperty] private PixelTool _tool = PixelTool.Brush;
    [ObservableProperty] private DrawingInputMode _currentInputMode = DrawingInputMode.Mouse;
    [ObservableProperty] private bool _showCenterGuide = true;
    [ObservableProperty] private string _customHex = "#00FF00";
    [ObservableProperty] private string _shareCode = string.Empty;
    [ObservableProperty] private string _generatedCodeStatus = string.Empty;
    [ObservableProperty] private string _codeLoadMessage = string.Empty;
    [ObservableProperty] private string _presetName = string.Empty;
    [ObservableProperty] private string _statusMessage = string.Empty;

    // Keep the complete code in the model, but never send a multi-megabyte PX2
    // string to WPF's TextBox layout engine. The displayed prefix is a preview;
    // CopyCode always uses the complete ShareCode.
    public string VisibleShareCode
    {
        get => ShareCode.Length <= 32_768 ? ShareCode : string.Concat(ShareCode.AsSpan(0, 4_096), "\n…");
        set
        {
            if (value != VisibleShareCode) ShareCode = value;
        }
    }

    partial void OnShareCodeChanged(string value)
    {
        OnPropertyChanged(nameof(VisibleShareCode));
        if (_settingGeneratedShareCode || !string.IsNullOrWhiteSpace(value)) return;

        // The editor has cleared the code. Internal PX2 generation also uses an
        // empty display string temporarily, so only external edits clear pixels.
        _shareCodeClearedByUser = true;
        StopCodeGeneration(clearText: false);
        ResetHistory();
        Array.Clear(_pixels);
        _coloredPixelCount = 0;
        Bitmap.WritePixels(new Int32Rect(0, 0, GridSize, GridSize), _pixels, GridSize * 4, 0);
        _canvasRevision++;
        CodeLoadMessage = string.Empty;
    }

    private void SetGeneratedShareCode(string code)
    {
        _settingGeneratedShareCode = true;
        try { ShareCode = code; }
        finally { _settingGeneratedShareCode = false; }
    }

    partial void OnToolChanged(PixelTool value)
    {
        OnPropertyChanged(nameof(IsBrushSelected));
        OnPropertyChanged(nameof(IsEraserSelected));
    }

    partial void OnCurrentInputModeChanged(DrawingInputMode value)
    {
        OnPropertyChanged(nameof(IsMouseInputSelected));
        OnPropertyChanged(nameof(IsStylusInputSelected));
    }

    [RelayCommand] private void UseMouseInput() => CurrentInputMode = DrawingInputMode.Mouse;

    [RelayCommand] private void UseStylusInput() => CurrentInputMode = DrawingInputMode.Stylus;

    [RelayCommand] private void UseBrush() => Tool = PixelTool.Brush;

    [RelayCommand] private void UseEraser() => Tool = PixelTool.Eraser;

    [RelayCommand] private void ToggleCenterGuide() => ShowCenterGuide = !ShowCenterGuide;

    [RelayCommand]
    private void ApplyGridSize()
    {
        if (!int.TryParse(GridSizeInput?.Trim(), NumberStyles.None,
                CultureInfo.InvariantCulture, out var size) || !PixelArtCode.IsSupportedSize(size))
        {
            StatusMessage = Tr.Get("Pixel_InvalidGridSize");
            GridSizeInput = GridSize.ToString(CultureInfo.InvariantCulture);
            return;
        }
        GridSize = size;
        StatusMessage = string.Empty;
    }

    [RelayCommand]
    private void ClearCanvas()
    {
        // Clearing is an editing boundary: release older deltas before allocating the
        // clear command, while still allowing one Undo of the clear itself.
        ResetHistory();
        var count = _coloredPixelCount;
        if (count == 0)
        {
            StopCodeGeneration(clearText: true);
            return;
        }
        var changes = new PixelChange[count];
        var next = 0;
        for (var index = 0; index < _pixels.Length; index++)
            if (_pixels[index] != 0) changes[next++] = new PixelChange(index, _pixels[index], 0);
        PushUndo(new PixelCommand(GridSize, changes));
        Array.Clear(_pixels);
        _coloredPixelCount = 0;
        Bitmap.WritePixels(new Int32Rect(0, 0, GridSize, GridSize), _pixels, GridSize * 4, 0);
        _canvasRevision++;
        StopCodeGeneration(clearText: true);
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
        if (_selectedColor != color)
        {
            _selectedColor = color;
            _selectedColorBrush = CreateFrozenBrush(color);
            OnPropertyChanged(nameof(SelectedColorBrush));
        }
        CustomHex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        Tool = PixelTool.Brush;
    }

    /// <summary>Draw a continuous stroke even when mouse events skip intermediate cells.</summary>
    public void DrawLine(int fromX, int fromY, int toX, int toY, bool erase)
    {
        Span<PixelStrokeSegment> segment = stackalloc PixelStrokeSegment[1];
        segment[0] = new PixelStrokeSegment(fromX, fromY, toX, toY, StrokeColor(erase));
        DrawSegments(segment);
    }

    internal uint StrokeColor(bool erase) => erase || Tool == PixelTool.Eraser
            ? 0u
            : 0xFF000000u | (uint)_selectedColor.R << 16 | (uint)_selectedColor.G << 8 | _selectedColor.B;

    /// <summary>Apply every input sample, then publish one bitmap dirty region per frame.</summary>
    internal void DrawSegments(ReadOnlySpan<PixelStrokeSegment> segments)
    {
        var minX = GridSize;
        var minY = GridSize;
        var maxX = -1;
        var maxY = -1;

        foreach (var segment in segments)
        {
            var fromX = segment.FromX;
            var fromY = segment.FromY;
            var toX = segment.ToX;
            var toY = segment.ToY;
            var dx = Math.Abs(toX - fromX);
            var sx = fromX < toX ? 1 : -1;
            var dy = -Math.Abs(toY - fromY);
            var sy = fromY < toY ? 1 : -1;
            var error = dx + dy;
            while (true)
            {
                if (PaintPixel(fromX, fromY, segment.Color))
                {
                    minX = Math.Min(minX, fromX);
                    minY = Math.Min(minY, fromY);
                    maxX = Math.Max(maxX, fromX);
                    maxY = Math.Max(maxY, fromY);
                }
                if (fromX == toX && fromY == toY) break;
                var twiceError = 2 * error;
                if (twiceError >= dy) { error += dy; fromX += sx; }
                if (twiceError <= dx) { error += dx; fromY += sy; }
            }
        }

        // Source and destination use the same grid coordinates. The five-argument
        // overload applies the source rectangle once; an additional byte offset
        // here would skip pixels and leave the requested destination cell blank.
        if (maxX >= 0)
        {
            Bitmap.WritePixels(new Int32Rect(minX, minY, maxX - minX + 1, maxY - minY + 1),
                _pixels, GridSize * 4, minX, minY);
            _canvasRevision++;
            if (GridSize > 32) QueueLiveCodeRefresh();
        }
    }

    public void FinishStroke()
    {
        if (_stroke.Count == 0) return;
        PushUndo(new PixelCommand(GridSize, [.. _stroke.Values]));
        _stroke.Clear();
        _canvasRevision++;
        RefreshShareCode();
    }

    private bool CanUndo() => _undoHistory.Count > 0 || _stroke.Count > 0;
    private bool CanRedo() => _redoHistory.Count > 0;

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo()
    {
        FinishStroke();
        if (_undoHistory.Last is not { } last) return;
        var command = last.Value;
        _undoHistory.RemoveLast();
        Apply(command, undo: true);
        _redoHistory.AddLast(command);
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo()
    {
        if (_redoHistory.Last is not { } last) return;
        var command = last.Value;
        _redoHistory.RemoveLast();
        Apply(command, undo: false);
        _undoHistory.AddLast(command);
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }

    private void Apply(PixelCommand command, bool undo)
    {
        if (command.Size != GridSize) return;
        foreach (var change in command.Changes)
        {
            var next = undo ? change.Before : change.After;
            var previous = _pixels[change.Index];
            if (previous == 0 && next != 0) _coloredPixelCount++;
            else if (previous != 0 && next == 0) _coloredPixelCount--;
            _pixels[change.Index] = next;
        }
        Bitmap.WritePixels(new Int32Rect(0, 0, GridSize, GridSize), _pixels, GridSize * 4, 0);
        _canvasRevision++;
        RefreshShareCode();
    }

    private void PushUndo(PixelCommand command)
    {
        foreach (var redo in _redoHistory) _historyBytes -= redo.Changes.LongLength * 12;
        _redoHistory.Clear();
        _undoHistory.AddLast(command);
        _historyBytes += command.Changes.LongLength * 12;
        while (_undoHistory.Count > 1 &&
               (_undoHistory.Count > MaxHistory || _historyBytes > MaxHistoryBytes))
        {
            _historyBytes -= _undoHistory.First!.Value.Changes.LongLength * 12;
            _undoHistory.RemoveFirst();
        }
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }

    private void ResetHistory()
    {
        _stroke.Clear();
        _undoHistory.Clear();
        _redoHistory.Clear();
        _historyBytes = 0;
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }

    private bool PaintPixel(int x, int y, uint color)
    {
        if ((uint)x >= GridSize || (uint)y >= GridSize) return false;
        var index = y * GridSize + x;
        if (_pixels[index] == color) return false;

        if (!_stroke.ContainsKey(index) && _stroke.Count >= MaxStrokeChanges)
            FinishStroke();

        var hadPendingStroke = _stroke.Count > 0;
        var before = _pixels[index];
        if (_stroke.TryGetValue(index, out var previous))
        {
            if (previous.Before == color) _stroke.Remove(index);
            else _stroke[index] = previous with { After = color };
        }
        else _stroke.Add(index, new PixelChange(index, before, color));
        if (hadPendingStroke != (_stroke.Count > 0)) UndoCommand.NotifyCanExecuteChanged();
        if (before == 0 && color != 0) _coloredPixelCount++;
        else if (before != 0 && color == 0) _coloredPixelCount--;
        _pixels[index] = color;
        return true;
    }

    private static int CountColoredPixels(uint[] pixels)
    {
        var count = 0;
        foreach (var pixel in pixels)
            if (pixel != 0) count++;
        return count;
    }

    private void StopCodeGeneration(bool clearText)
    {
        _codeUpdateTimer.Stop();
        _codeRefreshRequested = false;
        _generatedLargeCode = null;
        _generatedLargeCodeRevision = -1;
        GeneratedCodeStatus = string.Empty;
        if (clearText) SetGeneratedShareCode(string.Empty);
    }

    private void RefreshShareCode(bool immediate = false)
    {
        _shareCodeClearedByUser = false;
        if (_coloredPixelCount == 0)
        {
            StopCodeGeneration(clearText: true);
            return;
        }
        if (GridSize > 32)
        {
            QueueLiveCodeRefresh();
            // A committed resize must start encoding right away. Only rapid
            // drawing uses the 250 ms debounce to avoid redundant snapshots.
            if (immediate) OnCodeUpdateTimerTick(this, EventArgs.Empty);
            return;
        }

        _codeUpdateTimer.Stop();
        _generatedLargeCode = null;
        _generatedLargeCodeRevision = -1;
        GeneratedCodeStatus = string.Empty;
        SetGeneratedShareCode(PixelArtCode.Encode(GridSize, _pixels));
    }

    private void QueueLiveCodeRefresh()
    {
        if (_disposed || _isLoadingFromCode) return;
        if (_coloredPixelCount == 0)
        {
            StopCodeGeneration(clearText: true);
            return;
        }
        _shareCodeClearedByUser = false;
        _codeRefreshRequested = true;
        _generatedLargeCode = null;
        _generatedLargeCodeRevision = -1;
        if (!IsEditingShareCode && ShareCode.Length != 0) SetGeneratedShareCode(string.Empty);
        GeneratedCodeStatus = Tr.Get("Pixel_CodeGenerating");
        _codeUpdateTimer.Stop();
        _codeUpdateTimer.Start();
    }

    private async void OnCodeUpdateTimerTick(object? sender, EventArgs e)
    {
        _codeUpdateTimer.Stop();
        if (_disposed || _isGeneratingCode || _isLoadingFromCode || !_codeRefreshRequested || GridSize <= 32) return;

        _isGeneratingCode = true;
        _codeRefreshRequested = false;
        var revision = _canvasRevision;
        var size = GridSize;
        var count = _pixels.Length;
        var snapshot = ArrayPool<uint>.Shared.Rent(count);
        try
        {
            Array.Copy(_pixels, snapshot, count);
            var code = await Task.Run(() => PixelArtCode.Encode(size, snapshot.AsSpan(0, count)));
            if (_disposed || revision != _canvasRevision) return;

            _generatedLargeCode = code;
            _generatedLargeCodeRevision = revision;
            if (!IsEditingShareCode)
                SetGeneratedShareCode(code);
            GeneratedCodeStatus = code.Length <= 32_768
                ? string.Empty
                : Tr.Format("Pixel_CodeReadyForCopy", code.Length);
        }
        catch (Exception ex)
        {
            global::System.Diagnostics.Trace.WriteLine($"Pixel share code generation failed: {ex}");
            if (!_disposed && revision == _canvasRevision)
            {
                GeneratedCodeStatus = string.Empty;
                StatusMessage = Tr.Get("Pixel_CopyFailed");
            }
        }
        finally
        {
            ArrayPool<uint>.Shared.Return(snapshot);
            _isGeneratingCode = false;
            if (!_disposed && !_shareCodeClearedByUser && _codeRefreshRequested && !_codeUpdateTimer.IsEnabled)
                _codeUpdateTimer.Start();
        }
    }

    [RelayCommand]
    private void CopyCode()
    {
        if (_coloredPixelCount == 0)
        {
            StatusMessage = Tr.Get("Pixel_EmptyCanvas");
            return;
        }
        // Copy is deliberately side-effect free: generation belongs to the
        // edit/resize pipeline, never to the clipboard action.
        if (GridSize > 32 &&
            (_codeRefreshRequested || _isGeneratingCode ||
             _generatedLargeCodeRevision != _canvasRevision))
        {
            StatusMessage = Tr.Get("Pixel_CodeGenerating");
            return;
        }
        var code = ShareCode;
        if (string.IsNullOrEmpty(code))
        {
            StatusMessage = Tr.Get("Pixel_CodeGenerating");
            return;
        }
        try
        {
            Clipboard.SetText(code);
            StatusMessage = Tr.Get("Pixel_Copied");
        }
        catch (Exception ex) when (ex is ExternalException or InvalidOperationException)
        {
            StatusMessage = Tr.Get("Pixel_CopyFailed");
        }
    }

    [RelayCommand]
    private async Task LoadCodeAsync(string? submittedCode)
    {
        // The button passes TextBox.Text directly. A pending binding update must not
        // cause us to decode the previous code (or an unrelated clipboard value).
        var code = submittedCode ?? ShareCode;
        if (code == VisibleShareCode && ShareCode.Length > 32_768 &&
            _generatedLargeCodeRevision == _canvasRevision)
            code = ShareCode;
        if (string.IsNullOrWhiteSpace(code))
        {
            try { code = Clipboard.GetText(); }
            catch (Exception ex) when (ex is ExternalException or InvalidOperationException)
            {
                ReportLoadError(Tr.Get("Pixel_InvalidCode"));
                return;
            }
        }
        if (code.Length == 32_768)
        {
            // The TextBox intentionally caps display at 32 Ki characters. When a
            // large PX2 code was pasted, use the complete clipboard text only if
            // its prefix exactly matches what remains visible in the TextBox.
            try { code = PreferFullClipboardCode(code, Clipboard.GetText()); }
            catch (Exception ex) when (ex is ExternalException or InvalidOperationException)
            {
                global::System.Diagnostics.Trace.WriteLine($"Pixel code clipboard fallback unavailable: {ex}");
            }
        }
        if (AppCrosshairCode.IsAppCode(code) || ImageCrosshairCode.IsImageCode(code))
        {
            ReportLoadError(Tr.Get("Pixel_WrongCodeType"));
            return;
        }
        var codeHeader = code.TrimStart();
        if (!codeHeader.StartsWith("PX1|", StringComparison.Ordinal) &&
            !codeHeader.StartsWith("PX2|", StringComparison.Ordinal))
        {
            ReportLoadError(Tr.Get("Pixel_MissingHeader"));
            return;
        }
        var revision = _canvasRevision;
        try
        {
            (bool valid, int size, uint[] pixels) decoded;
            if (code.Length > 32_768)
                decoded = await Task.Run(() =>
                {
                    var valid = PixelArtCode.TryDecode(code, out var size, out var pixels);
                    return (valid, size, pixels);
                });
            else
            {
                var valid = PixelArtCode.TryDecode(code, out var size, out var pixels);
                decoded = (valid, size, pixels);
            }
            if (!decoded.valid)
            {
                var errorDispatcher = Bitmap.Dispatcher;
                if (errorDispatcher.CheckAccess()) ReportLoadError(Tr.Get("Pixel_InvalidCode"));
                else await errorDispatcher.InvokeAsync(() => ReportLoadError(Tr.Get("Pixel_InvalidCode")));
                return;
            }

            var dispatcher = Bitmap.Dispatcher;
            if (dispatcher.CheckAccess()) ApplyLoadedCode(code, decoded.size, decoded.pixels, revision);
            else await dispatcher.InvokeAsync(() => ApplyLoadedCode(code, decoded.size, decoded.pixels, revision));
        }
        catch (Exception ex)
        {
            global::System.Diagnostics.Trace.WriteLine($"Pixel code load failed: {ex}");
            var dispatcher = Bitmap.Dispatcher;
            var message = Tr.Format("Pixel_LoadFailed", ex.Message);
            if (dispatcher.CheckAccess()) ReportLoadError(message);
            else await dispatcher.InvokeAsync(() => ReportLoadError(message));
        }
    }

    internal static string PreferFullClipboardCode(string visibleCode, string clipboardCode)
    {
        if (visibleCode.Length == 32_768 &&
            (visibleCode.StartsWith("PX1|", StringComparison.Ordinal) ||
             visibleCode.StartsWith("PX2|", StringComparison.Ordinal)) &&
            clipboardCode.Length > visibleCode.Length &&
            clipboardCode.Length <= PixelArtCode.MaxCompressedCodeLength &&
            clipboardCode.AsSpan().StartsWith(visibleCode, StringComparison.Ordinal))
            return clipboardCode;
        return visibleCode;
    }

    private void ApplyLoadedCode(string code, int size, uint[] pixels, long requestRevision)
    {
        if (_disposed || requestRevision != _canvasRevision)
        {
            CodeLoadMessage = Tr.Get("Pixel_CodeStale");
            return;
        }

        _isLoadingFromCode = true;
        try
        {
            // A PX1 payload is RLE data, not raw BGRA bytes. Create the correctly
            // sized bitmap from decoded pixels, then publish it on the UI thread.
            var bitmap = CreateBitmap(size, pixels);
            _pixels = pixels;
            _coloredPixelCount = CountColoredPixels(pixels);
            if (_gridSize != size) SetProperty(ref _gridSize, size, nameof(GridSize));
            Bitmap = bitmap;
            GridSizeInput = size.ToString(CultureInfo.InvariantCulture);
            OnPropertyChanged(nameof(Bitmap));
            OnPropertyChanged(nameof(CanvasDisplaySize));
            ResetHistory();
            _canvasRevision++;
            StopCodeGeneration(clearText: _coloredPixelCount == 0);
            if (_coloredPixelCount != 0)
            {
                SetGeneratedShareCode(code);
                if (code.Length > 32_768)
                {
                    _generatedLargeCode = code;
                    _generatedLargeCodeRevision = _canvasRevision;
                    GeneratedCodeStatus = Tr.Format("Pixel_CodeReadyForCopy", code.Length);
                }
            }
            CodeLoadMessage = Tr.Get("Pixel_Loaded");
            StatusMessage = CodeLoadMessage;
        }
        finally { _isLoadingFromCode = false; }
    }

    private void ReportLoadError(string message)
    {
        CodeLoadMessage = message;
        StatusMessage = message;
        try { _dialogs?.ShowMessage(Tr.Get("Pixel_ShareTitle"), message, isError: true); }
        catch (Exception ex) { global::System.Diagnostics.Trace.WriteLine($"Pixel code error dialog failed: {ex}"); }
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
        var revision = _canvasRevision;
        if (!saveToLibrary && _lastSavedRevision == _canvasRevision && _lastSavedName == name &&
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
                _lastSavedRevision = revision;
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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _codeUpdateTimer.Stop();
        _codeUpdateTimer.Tick -= OnCodeUpdateTimerTick;
        _generatedLargeCode = null;
    }
}
