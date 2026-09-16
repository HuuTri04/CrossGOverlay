using System.Windows;
using System.Windows.Controls;
using CrosshairOverlay.Core.Abstractions;
using CrosshairOverlay.Localization;

namespace CrosshairOverlay.Views;

/// <summary>
/// Hộp thoại "Xuất mã tâm ngắm": hiện mã Valorant, CS2 và mã nội bộ của ứng dụng cùng lúc.
/// </summary>
/// <remarks>
/// Trước đây nút "Xuất mã" chép thẳng MỘT mã vào clipboard, nên người dùng không biết mình vừa
/// nhận được mã của định dạng nào, cũng không lấy được mã cho game khác. Ở đây cả ba mã đều hiện
/// ra, bôi đen và Ctrl+C được, mỗi mã kèm một câu nói rõ nó giữ được gì.
/// </remarks>
public partial class ExportCodeWindow : Window
{
    private readonly CrosshairExportCodes _codes;

    public ExportCodeWindow(CrosshairExportCodes codes)
    {
        ArgumentNullException.ThrowIfNull(codes);

        _codes = codes;
        InitializeComponent();

        PresetText.Text = Tr.Format("Export_PresetName", codes.PresetName);

        Fill(ValorantBox, ValorantCopy, ValorantNote, codes.Valorant);
        Fill(Cs2Box, Cs2Copy, Cs2Note, codes.Cs2);
        Fill(AppBox, AppCopy, AppNote, codes.App);

        if (codes.Cs2Console?.Code is { Length: > 0 } commands)
            Cs2ConsoleBox.Text = commands;
        else
            Cs2ConsolePanel.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Đổ một mã vào ô của nó.
    /// </summary>
    /// <remarks>
    /// Không dựng được mã thì ô hiện luôn LÝ DO ngay tại chỗ và nút Copy bị khoá — ô trống không
    /// nói cho người dùng biết vì sao Valorant không nhận được tâm ngắm ảnh.
    /// </remarks>
    private static void Fill(TextBox box, Button copy, TextBlock note, CrosshairExportCode entry)
    {
        var available = entry.Code is { Length: > 0 };

        box.Text = available ? entry.Code! : Tr.Get("Export_Unavailable");
        box.IsEnabled = available;
        copy.IsEnabled = available;
        note.Text = entry.Note ?? string.Empty;
        note.Visibility = string.IsNullOrEmpty(note.Text) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnCopyValorant(object sender, RoutedEventArgs e) => Copy(_codes.Valorant);

    private void OnCopyCs2(object sender, RoutedEventArgs e) => Copy(_codes.Cs2);

    private void OnCopyCs2Console(object sender, RoutedEventArgs e) => Copy(_codes.Cs2Console);

    private void OnCopyApp(object sender, RoutedEventArgs e) => Copy(_codes.App);

    /// <summary>
    /// Chép vào clipboard rồi báo ngay tại chỗ.
    /// </summary>
    /// <remarks>
    /// Clipboard của Windows do MỘT tiến trình giữ tại một thời điểm; trình quản lý clipboard hoặc
    /// máy ảo đang giữ thì lệnh chép ném lỗi. Bắt lỗi và nói thật, thay vì để hộp thoại sập.
    /// </remarks>
    private void Copy(CrosshairExportCode? entry)
    {
        if (entry?.Code is not { Length: > 0 } code) return;

        try
        {
            Clipboard.SetText(code);
            StatusText.Text = Tr.Format("Export_Copied", entry.Title);
            StatusText.Foreground = (System.Windows.Media.Brush)FindResource("AccentBright");
        }
        catch (Exception)
        {
            StatusText.Text = Tr.Get("Preset_ClipboardFailed");
            StatusText.Foreground = (System.Windows.Media.Brush)FindResource("Danger");
        }
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
