namespace CrosshairOverlay.Services.Updates;

/// <summary>Có nên TỰ ĐỘNG mời cập nhật lúc khởi động hay không.</summary>
/// <remarks>
/// Bảo vệ khỏi một cái bẫy có thật: bản phát hành gắn thẻ <c>v0.1.1</c> nhưng gói .zip đính kèm lại là build
/// <c>0.1.0</c>. Cập nhật chạy trót lọt, nhưng phiên bản không nhích lên, nên lần khởi động sau app lại thấy "có bản
/// mới" và lại mở hộp thoại — mỗi lần mở app một lần, mãi mãi. Đã thử lên đúng phiên bản đó mà không lên được thì
/// thôi không nhắc nữa; nút "Kiểm tra cập nhật" vẫn mở hộp thoại bình thường, vì đó là người dùng chủ động hỏi.
/// </remarks>
internal static class UpdateOfferPolicy
{
    /// <param name="lastAttempt">Phiên bản đã thử cập nhật lên ở lần chạy trước (lưu trong cài đặt).</param>
    public static bool ShouldOfferAutomatically(Version latest, string? lastAttempt) =>
        !(Version.TryParse(lastAttempt, out var attempted) && attempted >= latest);
}
