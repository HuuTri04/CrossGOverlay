using System.Windows;
using System.Windows.Controls;
using CrosshairOverlay.Core.Models;
using CrosshairOverlay.Localization;
using CrosshairOverlay.Services.Import;
using CrosshairOverlay.Services.Storage;
using CrosshairOverlay.Views.Dialogs;

namespace CrosshairOverlay.Views;

/// <summary>
/// Hộp thoại dán mã crosshair. Tự nhận diện mã hình ảnh của ứng dụng, CS2 hay Valorant và hiện
/// trước kết quả đọc được, để người dùng biết mình sắp tạo ra cái gì trước khi bấm.
/// </summary>
public partial class ImportCodeWindow : Window
{
    public ImportCodeWindow() => InitializeComponent();

    /// <summary>Preset đã dựng, chỉ có giá trị khi hộp thoại trả về true.</summary>
    public CrosshairProfile? Result { get; private set; }

    private CrosshairProfile? _parsed;

    /// <summary>
    /// Đang là mã hình ảnh nhưng đọc không được. Nút "Tạo preset" vẫn bấm được trong trường hợp
    /// này để hiện hộp thoại lỗi rõ ràng: mã hình ảnh dài hàng chục nghìn ký tự, người dùng không
    /// tự nhìn ra chỗ bị cắt, nên một dòng chữ đỏ nhỏ dễ bị bỏ qua.
    /// </summary>
    private bool _brokenImageCode;

    /// <summary>Tăng mỗi lần nội dung đổi; kết quả giải mã nền mang số cũ thì bị bỏ.</summary>
    private int _parseVersion;

    private void OnCodeChanged(object sender, TextChangedEventArgs e) => Parse();

    private void Parse()
    {
        _parseVersion++;
        _parsed = null;
        _brokenImageCode = false;
        OkButton.IsEnabled = false;
        DetailText.Text = "—";

        var text = CodeBox.Text?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            StatusText.Text = string.Empty;
            return;
        }

        // Mã hình ảnh xét TRƯỚC: nó cũng ngăn bằng dấu chấm phẩy như mã Valorant.
        if (ImageCrosshairCode.IsImageCode(text))
            ParseImage(text);

        // Mã nội bộ của ứng dụng: chở nguyên preset, không mất tính năng nào.
        else if (AppCrosshairCode.IsAppCode(text))
            ParseAppCode(text);

        // Mã Valorant là danh sách ngăn bằng dấu chấm phẩy; mã CS2 thì không bao giờ có.
        else if (text.Contains(';', StringComparison.Ordinal))
            ParseValorant(text);
        else
            ParseCs2(text);
    }

    /// <summary>
    /// Mã hình ảnh: Base64 + giải nén GZip + kiểm CRC + đọc kích thước ảnh — với mã dài hàng chục
    /// nghìn ký tự là việc đáng kể, nên chạy trên luồng nền. Gõ sửa từng ký tự thì chờ một nhịp ngắn
    /// cho người dùng gõ xong; kết quả về trễ của nội dung cũ bị bỏ qua.
    /// </summary>
    private async void ParseImage(string text)
    {
        var version = _parseVersion;
        StatusText.Text = Tr.Get("Import_Decoding");
        StatusText.Foreground = TryBrush("TextDim");

        ImageCrosshair? image = null;
        var ok = false;
        try
        {
            await Task.Delay(ParseDebounce);
            if (version != _parseVersion) return;

            (ok, image) = await Task.Run(() =>
            {
                var decoded = ImageCrosshairCode.TryDecode(text, out var result, out _);
                return (decoded && result is not null, result);
            });
        }
        catch (Exception)
        {
            ok = false;
        }

        // Người dùng đã sửa mã hoặc đóng hộp thoại (OnClosed tăng số phiên bản) trong lúc chờ.
        if (version != _parseVersion) return;

        if (!ok || image is null)
        {
            Fail(Tr.Get("Import_ImageCodeInvalid"));
            _brokenImageCode = true;
            OkButton.IsEnabled = true;
            return;
        }

        _parsed = ImageCrosshairCode.ToProfile(image, Tr.Get("Import_NameImage"));

        StatusText.Text = Tr.Format("Import_Detected", Tr.Get("Import_KindImage"));
        StatusText.Foreground = TryBrush("Accent");
        OkButton.IsEnabled = true;

        var p = _parsed;
        DetailText.Text = string.Join(
            Environment.NewLine,
            Row("Import_FieldImage", Tr.Format("Import_ImageInfo",
                image.Extension.TrimStart('.').ToUpperInvariant(), image.PixelWidth, image.PixelHeight,
                Math.Max(1, image.ImageBytes.Length / 1024))),
            Row("Import_FieldScale", p.Image.Scale.ToString("0.##")),
            Row("Import_FieldOpacity", p.Image.Opacity.ToString("0.##")),
            Row("Import_FieldOffset", $"X {p.Image.OffsetX:0.#} · Y {p.Image.OffsetY:0.#}"),
            Row("Import_FieldRotation", p.Rotation.ToString("0.#")));
    }

    /// <summary>Đủ ngắn để dán mã thấy kết quả gần như ngay, đủ dài để gõ sửa không giải mã từng phím.</summary>
    private static readonly TimeSpan ParseDebounce = TimeSpan.FromMilliseconds(120);

    /// <summary>
    /// Mã nội bộ CGO-CH. Nhẹ hơn mã ảnh rất nhiều (preset chỉ vài KB JSON) nên giải ngay tại chỗ.
    /// </summary>
    private void ParseAppCode(string text)
    {
        if (!AppCrosshairCode.TryDecode(text, out var profile, out _) || profile is null)
        {
            Fail(Tr.Get("Import_ImageCodeInvalid"));
            return;
        }

        _parsed = profile;
        Succeed(Tr.Get("Import_KindApp"));
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
            Row("Import_FieldInner", Describe(p.InnerLines)),
            Row("Import_FieldOuter", Describe(p.OuterLines)),
            Row("Import_FieldDot", dot),
            Row("Import_FieldOutline", outline),
            Row("Import_FieldOpacity", p.Opacity.ToString("0.##")));
    }

    private static string Describe(Core.Models.LineLayerSettings layer) =>
        layer.Enabled
            ? Tr.Format(
                "Import_LinesOn",
                layer.SeparateVerticalLength
                    ? Tr.Format("Import_LengthSplit", layer.Length.ToString("0.#"), layer.VerticalLength.ToString("0.#"))
                    : layer.Length.ToString("0.#"),
                layer.Thickness.ToString("0.#"),
                layer.Offset.ToString("0.#"),
                layer.Opacity.ToString("0.##"))
            : Tr.Get("Import_Off");

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
        if (_brokenImageCode)
        {
            AppDialogWindow.Show(this, Tr.Get("Import_Title"), Tr.Get("Import_ImageCodeInvalid"), DialogKind.Error);
            return;
        }

        if (_parsed is null) return;

        Result = _parsed;
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;

    protected override void OnClosed(EventArgs e)
    {
        // Vô hiệu mọi lượt giải mã nền còn đang chạy.
        _parseVersion++;
        base.OnClosed(e);
    }
}
