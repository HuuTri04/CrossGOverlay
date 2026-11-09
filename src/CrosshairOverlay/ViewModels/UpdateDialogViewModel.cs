using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CrosshairOverlay.Core.Abstractions;
using CrosshairOverlay.Localization;
using Microsoft.Extensions.Logging;

namespace CrosshairOverlay.ViewModels;

/// <summary>Hộp thoại "Đã có phiên bản mới": ghi chú phát hành, tải kèm thanh tiến trình, rồi khởi động lại.</summary>
/// <remarks>
/// Toàn bộ việc tải và thay file nằm ở <see cref="IUpdateService"/>. ViewModel này chỉ giữ trạng thái hiển thị, và
/// nói cho cửa sổ biết lúc nào nên đóng (<see cref="CloseRequested"/>) kèm việc ứng dụng có phải thoát hay không.
/// </remarks>
public sealed partial class UpdateDialogViewModel : ObservableObject
{
    private readonly IUpdateService _updates;
    private readonly IAppSettingsService _settings;
    private readonly UpdateInfo _update;
    private readonly ILogger _logger;

    public UpdateDialogViewModel(
        IUpdateService updates, IAppSettingsService settings, UpdateInfo update, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(update);

        _updates = updates;
        _settings = settings;
        _update = update;
        _logger = logger;

        CurrentVersionText = Core.AppInfo.Short(updates.CurrentVersion);
        LatestVersionText = Core.AppInfo.Short(update.Version);

        HasChangelog = !string.IsNullOrWhiteSpace(update.Changelog);
        Changelog = HasChangelog ? update.Changelog : Tr.Get("Update_NoNotes");
    }

    /// <param name="applied">true = script cập nhật đã chạy, ứng dụng phải thoát ngay.</param>
    public event EventHandler<bool>? CloseRequested;

    /// <summary>Huy hiệu bên trái, vd "v0.1.0".</summary>
    public string CurrentVersionText { get; }

    /// <summary>Huy hiệu bên phải, vd "v0.1.1".</summary>
    public string LatestVersionText { get; }

    /// <summary>Bản phát hành có ghi chú thật hay đang hiện câu thay thế (câu thay thế in nghiêng).</summary>
    public bool HasChangelog { get; }

    public string Changelog { get; }

    /// <summary>Đang tải: khoá hai nút và hiện thanh tiến trình.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UpdateNowCommand))]
    [NotifyCanExecuteChangedFor(nameof(LaterCommand))]
    private bool _isDownloading;

    private sealed record ProgressState(double Percent, string Text, string Status);
    private readonly object _progressSync = new();
    private ProgressState _progress = new(0, string.Empty, string.Empty);

    /// <summary>0..100 cho ProgressBar.</summary>
    public double ProgressPercent => Volatile.Read(ref _progress).Percent;

    /// <summary>Chữ bên trái dưới thanh tiến trình.</summary>
    public string DownloadStatus => Volatile.Read(ref _progress).Status;

    /// <summary>Chữ bên phải: "45%".</summary>
    public string ProgressText => Volatile.Read(ref _progress).Text;

    private void SetProgress(ProgressState next, bool reset = false)
    {
        // Progress<T> posts callbacks asynchronously; a late 25% report must not
        // overwrite a 100% snapshot that was already delivered.
        lock (_progressSync)
        {
            var current = _progress;
            if (!reset && next.Percent < current.Percent) return;
            if (current == next) return;
            Volatile.Write(ref _progress, next); // Publish all three values together.
        }
        OnPropertyChanged(nameof(ProgressPercent));
        OnPropertyChanged(nameof(ProgressText));
        OnPropertyChanged(nameof(DownloadStatus));
    }

    /// <summary>Lỗi khi tải; rỗng khi không có lỗi.</summary>
    [ObservableProperty] private string _errorText = string.Empty;

    private bool CanAct => !IsDownloading;

    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task UpdateNowAsync()
    {
        IsDownloading = true;
        ErrorText = string.Empty;
        SetProgress(new ProgressState(0, "0%", Tr.Get("Update_Downloading")), reset: true);

        // IProgress bắt SynchronizationContext lúc tạo, nên Report từ luồng nền vẫn chạy trên luồng giao diện.
        var progress = new Progress<double>(fraction =>
        {
            if (!IsDownloading) return;
            var percent = Math.Clamp(fraction, 0d, 1d) * 100d;
            SetProgress(new ProgressState(percent, $"{(int)percent}%",
                percent >= 100d ? Tr.Get("Update_Restarting") : Tr.Get("Update_Downloading")));
        });

        // Ghi lại TRƯỚC khi bàn giao cho script: sau đó ứng dụng thoát ngay, không còn lúc nào để ghi nữa.
        // Lần khởi động sau, nếu phiên bản vẫn không nhích lên thì thôi không tự nhắc nữa (xem UpdateOfferPolicy).
        _settings.Current.LastUpdateAttempt = _update.Version.ToString();
        _settings.RequestSave();

        try
        {
            var applied = await _updates.DownloadAndApplyAsync(_update, progress).ConfigureAwait(true);
            if (applied)
            {
                SetProgress(_progress with { Status = Tr.Get("Update_Restarting") });
                CloseRequested?.Invoke(this, true);
                return;
            }

            // Không xác định được thư mục cài đặt: dịch vụ đã ghi log, ở đây chỉ trả quyền lại cho người dùng.
            Fail(_update.ReleaseUrl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cập nhật lên {Version} thất bại.", _update.Version);
            Fail(ex.Message);
        }
    }

    private void Fail(string detail)
    {
        ErrorText = Tr.Format("Update_FailedBody", detail);
        SetProgress(_progress with { Status = string.Empty, Text = string.Empty });
        IsDownloading = false;
    }

    [RelayCommand(CanExecute = nameof(CanAct))]
    private void Later() => CloseRequested?.Invoke(this, false);
}
