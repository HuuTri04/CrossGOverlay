using System.Windows;
using System.Windows.Controls;
using CrosshairOverlay.Core.Models;
using CrosshairOverlay.Localization;
using CrosshairOverlay.Services.Import;
using CrosshairOverlay.Services.Storage;

namespace CrosshairOverlay.Views;

/// <summary>
/// Hộp thoại dán mã crosshair của game. Tự nhận diện CS2 hay Valorant và hiện trước kết quả
/// đọc được, để người dùng biết mình sắp tạo ra cái gì trước khi bấm.
/// </summary>
public partial class ImportCodeWindow : Window
{
    public ImportCodeWindow() => InitializeComponent();

    /// <summary>Preset đã dựng, chỉ có giá trị khi hộp thoại trả về true.</summary>
    public CrosshairProfile? Result { get; private set; }

    private CrosshairProfile? _parsed;

    private void OnCodeChanged(object sender, TextChangedEventArgs e) => Parse();

    private void Parse()
    {
        _parsed = null;
        OkButton.IsEnabled = false;
        DetailText.Text = "—";

        var text = CodeBox.Text?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            StatusText.Text = string.Empty;
            return;
        }

        // Mã Valorant là danh sách ngăn bằng dấu chấm phẩy; mã CS2 thì không bao giờ có.
        if (text.Contains(';', StringComparison.Ordinal))
            ParseValorant(text);
        else
            ParseCs2(text);
    }

    private void ParseValorant(string text)
    {
        if (!ValorantCrosshairCode.TryDecode(text, out var crosshair, out var error))
        {
            Fail(Tr.Format("Import_FailedValorant", error));
            return;
        }

        _parsed = CrosshairCodeConverter.ToProfile(crosshair, Tr.Get("Import_NameValorant"));
        Succeed("Valorant");
    }

    private void ParseCs2(string text)
    {
        if (!Cs2ShareCode.TryDecode(text, out var crosshair, out var error))
        {
            Fail(Tr.Format("Import_FailedCs2", error));
            return;
        }

        _parsed = CrosshairCodeConverter.ToProfile(crosshair, Tr.Get("Import_NameCs2"));
        Succeed("Counter-Strike 2");
    }

    private void Succeed(string gameName)
    {
        StatusText.Text = Tr.Format("Import_Detected", gameName);
        StatusText.Foreground = TryBrush("Accent");
        OkButton.IsEnabled = true;

        if (_parsed is not { } p) return;

        var dot = p.CenterDot.Enabled
            ? Tr.Format("Import_On", p.CenterDot.Size.ToString("0.#"))
            : Tr.Get("Import_Off");

        var outline = p.Outline.Enabled
            ? Tr.Format("Import_OutlineOn", p.Outline.Thickness.ToString("0.#"))
            : Tr.Get("Import_Off");

        DetailText.Text = string.Join(
            Environment.NewLine,
            Row("Import_FieldShape", p.Shape.ToString()),
            Row("Import_FieldColor", JsonColorConverter.ToHex(p.Color)),
            Row("Import_FieldLength", p.Lines.Length.ToString("0.#")),
            Row("Import_FieldThickness", p.Lines.Thickness.ToString("0.#")),
            Row("Import_FieldGap", p.Lines.Gap.ToString("0.#")),
            Row("Import_FieldDot", dot),
            Row("Import_FieldOutline", outline),
            Row("Import_FieldOpacity", p.Opacity.ToString("0.##")));
    }

    /// <summary>Căn nhãn cho thẳng cột, không phụ thuộc độ dài nhãn của từng ngôn ngữ.</summary>
    private static string Row(string key, string value) => $"{Tr.Get(key),-16}: {value}";

    private void Fail(string message)
    {
        StatusText.Text = message;
        StatusText.Foreground = TryBrush("Danger");
    }

    private System.Windows.Media.Brush TryBrush(string key) =>
        TryFindResource(key) as System.Windows.Media.Brush
        ?? System.Windows.Media.Brushes.Gainsboro;

    private void OnAccept(object sender, RoutedEventArgs e)
    {
        if (_parsed is null) return;

        Result = _parsed;
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
