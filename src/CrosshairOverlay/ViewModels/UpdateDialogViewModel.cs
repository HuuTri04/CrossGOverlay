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

        CurrentVersionText = "v" + Core.AppInfo.Short(updates.CurrentVersion);
        LatestVersionText = "v" + Core.AppInfo.Short(update.Version);

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

    /// <summary>0..100 cho ProgressBar.</summary>
    [ObservableProperty] private double _progressPercent;

    /// <summary>Chữ bên trái dưới thanh tiến trình: "Đang tải xuống bản cài đặt..." / "Đang khởi động lại...".</summary>
    [ObservableProperty] private string _downloadStatus = string.Empty;

    /// <summary>Chữ bên phải: "45%".</summary>
    [ObservableProperty] private string _progressText = string.Empty;

    /// <summary>Lỗi khi tải; rỗng khi không có lỗi.</summary>
    [ObservableProperty] private string _errorText = string.Empty;

    private bool CanAct => !IsDownloading;

    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task UpdateNowAsync()
    {
        IsDownloading = true;
        ErrorText = string.Empty;
        ProgressPercent = 0;
        DownloadStatus = Tr.Get("Update_Downloading");
        ProgressText = "0%";

        // IProgress bắt SynchronizationContext lúc tạo, nên Report từ luồng nền vẫn chạy trên luồng giao diện.
        var progress = new Progress<double>(fraction =>
        {
            ProgressPercent = Math.Clamp(fraction, 0d, 1d) * 100d;
            ProgressText = $"{(int)ProgressPercent}%";
            DownloadStatus = ProgressPercent >= 100d ? Tr.Get("Update_Restarting") : Tr.Get("Update_Downloading");
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
                DownloadStatus = Tr.Get("Update_Restarting");
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
        DownloadStatus = string.Empty;
        ProgressText = string.Empty;
        IsDownloading = false;
    }

    [RelayCommand(CanExecute = nameof(CanAct))]
    private void Later() => CloseRequested?.Invoke(this, false);
}
