using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CrosshairOverlay.Core.Abstractions;
using CrosshairOverlay.Localization;

namespace CrosshairOverlay.ViewModels;

/// <summary>Panel "Thông tin hệ thống" trong tab Phím tắt.</summary>
/// <remarks>
/// Hiện "Đang đọc…" ngay lập tức, rồi điền kết quả khi dịch vụ đọc xong trên luồng nền — cửa sổ mở
/// không phải chờ. Giữ số liệu thô và định dạng lại khi đổi ngôn ngữ. Màn hình đọc lại mỗi khi cấu hình
/// hiển thị đổi. Không có timer nào: mọi cập nhật đều do sự kiện.
/// </remarks>
public sealed partial class SystemInfoViewModel : ObservableObject, IDisposable
{
    private readonly IMonitorService _monitors;
    private readonly IDisplayModeReader _displayModes;

    private HardwareInfo? _info;
    private bool _disposed;

    public SystemInfoViewModel(IHardwareInfoService hardware, IMonitorService monitors, IDisplayModeReader displayModes)
    {
        ArgumentNullException.ThrowIfNull(hardware);
        _monitors = monitors;
        _displayModes = displayModes;

        TranslationSource.Instance.PropertyChanged += OnLanguageChanged;
        _monitors.DisplayConfigurationChanged += OnDisplayConfigurationChanged;

        _ = LoadAsync(hardware);
    }

    /// <summary>Chờ lần đọc hiện tại xong — dùng trong test.</summary>
    internal Task Loading { get; private set; } = Task.CompletedTask;

    [ObservableProperty] private bool _isLoading = true;

    public string OsInfo => Format(i => i.OsName is null
        ? null
        : Tr.Format("Hw_OsValue", i.OsName, i.OsBuild ?? "?", i.Is64BitOs ? "64-bit" : "32-bit"));

    public string CpuName => Format(i => i.CpuName is null
        ? null
        : Tr.Format("Hw_CpuValue", i.CpuName, i.LogicalProcessors));

    public string GpuName => Format(i => i.Gpus.Count == 0 ? null : string.Join(Environment.NewLine, i.Gpus));

    public string RamInfo => Format(i => i.InstalledRamBytes == 0 && i.UsableRamBytes == 0
        ? null
        : i.InstalledRamBytes == 0
            ? Tr.Format("Hw_RamUsableOnly", Gigabytes(i.UsableRamBytes, 1))
            : Tr.Format("Hw_RamValue", Gigabytes(i.InstalledRamBytes, 0), Gigabytes(i.UsableRamBytes, 1)));

    /// <summary>
    /// Màn hình chính: "1920x1080 @ 144Hz", thêm số màn hình khác nếu có. Đọc thẳng (vài micro-giây),
    /// không cần chờ luồng nền.
    /// </summary>
    public string DisplayInfo
    {
        get
        {
            try
            {
                var primary = _monitors.GetPrimaryMonitor();
                var mode = _displayModes.Read(primary.DeviceName);

                var width = mode?.Width ?? (int)primary.Bounds.Width;
                var height = mode?.Height ?? (int)primary.Bounds.Height;
                if (width <= 0 || height <= 0) return Tr.Get("Hw_Unknown");

                var text = mode is { RefreshRate: > 0 }
                    ? Tr.Format("Hw_DisplayValue", width, height, mode.RefreshRate)
                    : Tr.Format("Hw_DisplayNoRefresh", width, height);

                var others = _monitors.GetMonitors().Count - 1;
                return others > 0 ? text + Tr.Format("Hw_DisplayOthers", others) : text;
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.ExternalException or ArgumentException)
            {
                return Tr.Get("Hw_Unknown");
            }
        }
    }

    private void OnDisplayConfigurationChanged(object? sender, EventArgs e) => OnPropertyChanged(nameof(DisplayInfo));

    private async Task LoadAsync(IHardwareInfoService hardware)
    {
        var task = hardware.GetAsync();
        Loading = task;

        try
        {
            _info = await task;
        }
        catch
        {
            // Dịch vụ đã tự nuốt lỗi từng mục; tới được đây thì cả panel hiện "không xác định".
            _info = null;
        }

        if (_disposed) return;

        IsLoading = false;
        RaiseAll();
    }

    private string Format(Func<HardwareInfo, string?> pick)
    {
        if (IsLoading) return Tr.Get("Hw_Loading");
        return (_info is null ? null : pick(_info)) ?? Tr.Get("Hw_Unknown");
    }

    /// <summary>"16" cho dung lượng lắp đặt, "15,8" cho phần dùng được — theo ngôn ngữ giao diện.</summary>
    private static string Gigabytes(ulong bytes, int decimals) =>
        Math.Round(bytes / (1024d * 1024d * 1024d), decimals)
            .ToString(decimals == 0 ? "0" : "0.#", TranslationSource.Instance.CurrentCulture ?? CultureInfo.CurrentCulture);

    private void RaiseAll()
    {
        OnPropertyChanged(nameof(OsInfo));
        OnPropertyChanged(nameof(CpuName));
        OnPropertyChanged(nameof(GpuName));
        OnPropertyChanged(nameof(RamInfo));
        OnPropertyChanged(nameof(DisplayInfo));
    }

    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs e) => RaiseAll();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        TranslationSource.Instance.PropertyChanged -= OnLanguageChanged;
        _monitors.DisplayConfigurationChanged -= OnDisplayConfigurationChanged;
    }
}
