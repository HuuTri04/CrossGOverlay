using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CrosshairOverlay.Core.Abstractions;
using CrosshairOverlay.Core.Models;
using CrosshairOverlay.Localization;
using Microsoft.Extensions.Logging;

namespace CrosshairOverlay.ViewModels;

/// <summary>ViewModel gốc của cửa sổ Settings.</summary>
public sealed partial class SettingsViewModel : ObservableObject, IDisposable
{
    private readonly IPresetLibrary _library;
    private readonly IOverlayController _overlay;
    private readonly IAppSettingsService _settings;
    private readonly IDialogService _dialogs;
    private readonly ILogger<SettingsViewModel> _logger;

    private bool _syncingSelection;
    private bool _disposed;

    public SettingsViewModel(
        IPresetLibrary library,
        IOverlayController overlay,
        IAppSettingsService settings,
        IMonitorService monitors,
        ICrosshairRenderer renderer,
        IDialogService dialogs,
        IHotkeyService hotkeys,
        IForegroundWindowWatcher watcher,
        IStartupService startup,
        IGameProfileMatcher matcher,
        IRunningApplicationScanner scanner,
        IUpdateService updates,
        ICustomImageStore images,
        ILogger<SettingsViewModel> logger)
    {
        _library = library;
        _overlay = overlay;
        _settings = settings;
        _dialogs = dialogs;
        _logger = logger;

        Editor = new CrosshairEditorViewModel(renderer, dialogs, images);
        General = new GeneralSettingsViewModel(settings, monitors, overlay, startup, updates, dialogs);
        Hotkeys = new HotkeysViewModel(settings, hotkeys);
        Games = new GameProfilesViewModel(settings, library, watcher, matcher, scanner);

        _overlayEnabled = overlay.IsVisible;

        _library.ActiveChanged += OnLibraryActiveChanged;
        SyncFromLibrary();
    }

    private static string PresetFileFilter => Tr.Get("Preset_FileFilter");

    public CrosshairEditorViewModel Editor { get; }

    public GeneralSettingsViewModel General { get; }

    public HotkeysViewModel Hotkeys { get; }

    public GameProfilesViewModel Games { get; }

    public ReadOnlyObservableCollection<CrosshairProfile> Presets => _library.Presets;

    [ObservableProperty] private CrosshairProfile? _selectedPreset;

    [ObservableProperty] private bool _overlayEnabled;

    partial void OnSelectedPresetChanged(CrosshairProfile? value)
    {
        if (_syncingSelection || value is null) return;
        _library.SetActive(value);
    }

    partial void OnOverlayEnabledChanged(bool value)
    {
        _overlay.SetVisible(value);
        _settings.Current.OverlayEnabled = value;
        _settings.RequestSave();
    }

    private void OnLibraryActiveChanged(object? sender, EventArgs e) => SyncFromLibrary();

    private void SyncFromLibrary()
    {
        var active = _library.Active;
        if (active is null) return;

        // Cờ này chặn vòng lặp: đặt SelectedPreset sẽ gọi lại SetActive.
        _syncingSelection = true;
        try
        {
            SelectedPreset = active;
        }
        finally
        {
            _syncingSelection = false;
        }

        // Chỉ đồng bộ phần UI. Việc đẩy preset xuống overlay và ghi ActivePresetId do
        // composition root lo, vì preset có thể đổi khi cửa sổ này đang đóng.
        Editor.Profile = active;
    }

    [RelayCommand]
    private async Task NewPresetAsync()
    {
        try
        {
            await _library.CreateAsync();
        }
        catch (Exception ex)
        {
            Report(ex, Tr.Get("Preset_ErrorCreate"));
        }
    }

    [RelayCommand]
    private async Task DuplicatePresetAsync()
    {
        if (SelectedPreset is not { } source) return;

        try
        {
            await _library.DuplicateAsync(source);
        }
        catch (Exception ex)
        {
            Report(ex, Tr.Get("Preset_ErrorDuplicate"));
        }
    }

    [RelayCommand]
    private async Task DeletePresetAsync()
    {
        if (SelectedPreset is not { } target) return;

        if (!_dialogs.Confirm(Tr.Get("Preset_DeleteTitle"), Tr.Format("Preset_DeleteConfirm", target.Name)))
            return;

        try
        {
            await _library.DeleteAsync(target);
        }
        catch (Exception ex)
        {
            Report(ex, Tr.Get("Preset_ErrorDelete"));
        }
    }

    /// <summary>Tạo preset từ mã chia sẻ crosshair của CS2 hoặc Valorant.</summary>
    [RelayCommand]
    private async Task ImportCodeAsync()
    {
        var profile = _dialogs.PromptForCrosshairCode();
        if (profile is null) return;

        try
        {
            await _library.AddAsync(profile);
        }
        catch (Exception ex)
        {
            Report(ex, Tr.Get("Preset_ErrorFromCode"));
        }
    }

    /// <summary>Dựng mã chia sẻ từ preset đang chọn và chép vào clipboard.</summary>
    [RelayCommand]
    private void ExportCode()
    {
        if (SelectedPreset is not { } preset) return;

        var code = Services.Import.ValorantCrosshairCode.Encode(
            Services.Import.CrosshairCodeConverter.ToValorantCrosshair(preset));

        if (!_dialogs.CopyToClipboard(code))
        {
            _dialogs.ShowMessage(Core.AppInfo.DisplayName, Tr.Get("Preset_ClipboardFailed"), isError: true);
            return;
        }

        var body = Tr.Format("Preset_CodeCopiedBody", code);

        // Nói trước cho người dùng biết sẽ mất gì, thay vì đưa ra một mã trông có vẻ đúng.
        if (!Services.Import.CrosshairCodeConverter.CanExportFaithfully(preset))
            body += Tr.Get("Preset_CodeLossy");

        _dialogs.ShowMessage(Tr.Get("Preset_CodeCopiedTitle"), body);
    }

    [RelayCommand]
    private async Task ImportPresetAsync()
    {
        var path = _dialogs.PickFileToOpen(PresetFileFilter);
        if (path is null) return;

        try
        {
            await _library.ImportAsync(path);
        }
        catch (Exception ex)
        {
            Report(ex, Tr.Format("Preset_ErrorImport", path));
        }
    }

    [RelayCommand]
    private async Task ExportPresetAsync()
    {
        if (SelectedPreset is not { } source) return;

        var suggested = MakeSafeFileName(source.Name) + ".json";
        var path = _dialogs.PickFileToSave(PresetFileFilter, suggested);
        if (path is null) return;

        try
        {
            await _library.ExportAsync(source, path);
            _dialogs.ShowMessage(
                Tr.Get("Preset_ExportTitle"), Tr.Format("Preset_ExportDone", source.Name, path));
        }
        catch (Exception ex)
        {
            Report(ex, Tr.Get("Preset_ErrorExport"));
        }
    }

    /// <summary>
    /// Gỡ mọi đăng ký lên service singleton. Cửa sổ Settings gọi hàm này khi đóng; thiếu nó
    /// thì mỗi lần mở lại cửa sổ là một ViewModel nữa bị giữ sống mãi qua sự kiện.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _library.ActiveChanged -= OnLibraryActiveChanged;

        Editor.Dispose();
        General.Dispose();
        Hotkeys.Dispose();
        Games.Dispose();
    }

    private void Report(Exception ex, string message)
    {
        _logger.LogError(ex, "{Message}", message);
        _dialogs.ShowMessage(Core.AppInfo.DisplayName, $"{message}\n\n{ex.Message}");
    }

    private static string MakeSafeFileName(string name)
    {
        var safe = name.Trim();
        foreach (var invalid in global::System.IO.Path.GetInvalidFileNameChars())
            safe = safe.Replace(invalid, '_');

        return string.IsNullOrWhiteSpace(safe) ? "crosshair" : safe;
    }
}
