using CrosshairOverlay.Core.Abstractions;
using CrosshairOverlay.Localization;

namespace CrosshairOverlay.Services.Updates;

/// <summary>
/// Luồng hỏi–tải–thay thế khi đã phát hiện có bản mới.
/// </summary>
/// <remarks>
/// Tách riêng vì có hai nơi dùng: lần kiểm tra tự động lúc khởi động, và nút "Kiểm tra cập
/// nhật" trong tab Chung. Cả hai phải hành xử giống hệt nhau.
/// </remarks>
internal static class UpdatePrompt
{
    /// <summary>Trả về true khi script cập nhật đã chạy — khi đó ứng dụng phải tự thoát ngay.</summary>
    public static async Task<bool> OfferAsync(
        IUpdateService updates, IDialogService dialogs, UpdateInfo update)
    {
        var accepted = dialogs.Confirm(
            Tr.Get("Update_Title"),
            Tr.Format("Update_Body", update.Version, updates.CurrentVersion),
            Tr.Get("Update_Primary"),
            Tr.Get("Update_Later"));

        if (!accepted) return false;

        try
        {
            return await updates.DownloadAndApplyAsync(update).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            dialogs.ShowMessage(
                Tr.Get("Update_FailedTitle"),
                Tr.Format("Update_FailedBody", ex.Message),
                isError: true);

            return false;
        }
    }
}
