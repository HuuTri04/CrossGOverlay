using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CrosshairOverlay.Core.Abstractions;
using CrosshairOverlay.Core.Models;
using CrosshairOverlay.Localization;

namespace CrosshairOverlay.ViewModels;

/// <summary>Một dòng trong bảng phím tắt: binding thật cộng nhãn đã dịch của hành động.</summary>
public sealed class HotkeyRow
{
    public HotkeyRow(HotkeyBinding binding, string labelKey)
    {
        Binding = binding;
        LabelKey = labelKey;
    }

    public HotkeyBinding Binding { get; }

    public string LabelKey { get; }

    public string Label => Tr.Get(LabelKey);
}

/// <summary>ViewModel cho tab "Phím tắt".</summary>
public sealed partial class HotkeysViewModel : ObservableObject, IDisposable
{
    private static readonly Dictionary<HotkeyAction, string> LabelKeys = new()
    {
        [HotkeyAction.ToggleOverlay] = "Action_ToggleOverlay",
        [HotkeyAction.NextPreset] = "Action_NextPreset",
        [HotkeyAction.PreviousPreset] = "Action_PreviousPreset",
        [HotkeyAction.ShowSettingsWindow] = "Action_ShowSettings",
        [HotkeyAction.ExitApplication] = "Action_Exit",
    };

    private readonly IAppSettingsService _settings;
    private readonly IHotkeyService _hotkeys;
    private bool _disposed;

    public HotkeysViewModel(IAppSettingsService settings, IHotkeyService hotkeys)
    {
        _settings = settings;
        _hotkeys = hotkeys;

        _rows = BuildRows();
        TranslationSource.Instance.PropertyChanged += OnLanguageChanged;
    }

    /// <summary>Dựng lại khi đổi ngôn ngữ để nhãn hành động đọc lại resource.</summary>
    [ObservableProperty] private IReadOnlyList<HotkeyRow> _rows;

    [ObservableProperty] private string _statusMessage = string.Empty;

    [ObservableProperty] private bool _hasFailures;

    /// <summary>Nhãn đã dịch của một hành động, dùng chung cho cả thông báo lỗi.</summary>
    public static string LabelFor(HotkeyAction action) =>
        Tr.Get(LabelKeys.GetValueOrDefault(action, action.ToString()));

    private IReadOnlyList<HotkeyRow> BuildRows() =>
        [.. _settings.Current.Hotkeys.Select(
            b => new HotkeyRow(b, LabelKeys.GetValueOrDefault(b.Action, b.Action.ToString())))];

    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs e)
    {
        Rows = BuildRows();
        StatusMessage = string.Empty;
    }

    /// <summary>
    /// Đăng ký lại toàn bộ hotkey với Windows. Người dùng phải bấm tay sau khi sửa, vì đăng ký
    /// lại sau MỖI lần gõ phím sẽ liên tục chiếm rồi nhả tổ hợp phím của cả hệ thống.
    /// </summary>
    [RelayCommand]
    private void ApplyHotkeys()
    {
        _settings.RequestSave();

        var result = _hotkeys.Apply(_settings.Current.Hotkeys);
        HasFailures = !result.AllSucceeded;

        if (result.AllSucceeded)
        {
            StatusMessage = Tr.Format("Hotkeys_AllOk", result.Registered.Count);
            return;
        }

        var details = string.Join(
            "  ·  ",
            result.Failures.Select(f => $"{LabelFor(f.Binding.Action)}: {f.Reason}"));

        StatusMessage = Tr.Format("Hotkeys_SomeFailed", result.Registered.Count, details);
    }

    [RelayCommand]
    private void ResetToDefaults()
    {
        var defaults = HotkeyBinding.CreateDefaults();

        foreach (var row in Rows)
        {
            var match = defaults.FirstOrDefault(d => d.Action == row.Binding.Action);
            if (match is null) continue;

            row.Binding.Modifiers = match.Modifiers;
            row.Binding.Key = match.Key;
            row.Binding.Enabled = match.Enabled;
        }

        ApplyHotkeys();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        TranslationSource.Instance.PropertyChanged -= OnLanguageChanged;
    }
}
